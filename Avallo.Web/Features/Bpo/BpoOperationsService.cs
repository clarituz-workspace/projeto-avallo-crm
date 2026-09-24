using Microsoft.EntityFrameworkCore;
using Avallo.Web.Domain;
using Avallo.Web.Features.PeriodClosing;
using Avallo.Web.Infrastructure;

namespace Avallo.Web.Features.Bpo;

public sealed record BpoPeriodItem(
    Guid TenantId, string TenantName, Guid PeriodId, int Year, int Month,
    string Status, DateTimeOffset? ValidatedAt, decimal? Result);
public sealed record BpoDashboard(BpoPeriodItem[] Periods, int AwaitingReview, int ReadyToClose);
public sealed record BpoBatchRequest(Guid[] PeriodIds, string Action);
public sealed record BpoBatchItemResult(Guid PeriodId, bool Succeeded, string? Error);
public sealed record BpoBatchResult(BpoBatchItemResult[] Items)
{
    public int Succeeded => Items.Count(x => x.Succeeded);
    public int Failed => Items.Length - Succeeded;
}
public sealed record AssignBpoTenantRequest(Guid OperatorUserId, Guid TargetTenantId);

public sealed class BpoOperationsService(
    AppDbContext operatorDb,
    IServiceScopeFactory scopeFactory,
    ITenantContext operatorTenantContext)
{
    public async Task<BpoTenantAssignment> AssignTenantAsync(
        Guid adminUserId, AssignBpoTenantRequest request, CancellationToken cancellationToken = default)
    {
        var internalTenantId = operatorTenantContext.TenantId
            ?? throw new UnauthorizedAccessException("Tenant interno BPO obrigatorio.");
        var operatorHasRole = await (from user in operatorDb.Users
                                     join userRole in operatorDb.UserRoles on user.Id equals userRole.UserId
                                     join role in operatorDb.Roles on userRole.RoleId equals role.Id
                                     where user.Id == request.OperatorUserId && user.TenantId == internalTenantId &&
                                           role.Name == Roles.BpoOperator && user.IsActive
                                     select user.Id).AnyAsync(cancellationToken);
        if (!operatorHasRole)
            throw new ArgumentException("O usuario informado nao e um operador BPO ativo deste tenant interno.");
        if (!await operatorDb.Tenants.AsNoTracking().AnyAsync(x => x.Id == request.TargetTenantId, cancellationToken))
            throw new ArgumentException("Empresa de destino nao encontrada.");

        var existing = await operatorDb.BpoTenantAssignments.SingleOrDefaultAsync(
            x => x.OperatorUserId == request.OperatorUserId && x.TargetTenantId == request.TargetTenantId,
            cancellationToken);
        if (existing is not null)
        {
            existing.RevokedAt = null;
            await operatorDb.SaveChangesAsync(cancellationToken);
            return existing;
        }
        var assignment = new BpoTenantAssignment
        {
            TenantId = internalTenantId,
            OperatorUserId = request.OperatorUserId,
            TargetTenantId = request.TargetTenantId,
            AssignedByUserId = adminUserId,
            AssignedAt = DateTimeOffset.UtcNow
        };
        operatorDb.BpoTenantAssignments.Add(assignment);
        await operatorDb.SaveChangesAsync(cancellationToken);
        return assignment;
    }

    public async Task RevokeTenantAsync(Guid assignmentId, CancellationToken cancellationToken = default)
    {
        var assignment = await operatorDb.BpoTenantAssignments.SingleOrDefaultAsync(
            x => x.Id == assignmentId, cancellationToken)
            ?? throw new KeyNotFoundException("Atribuicao BPO nao encontrada.");
        assignment.RevokedAt = DateTimeOffset.UtcNow;
        await operatorDb.SaveChangesAsync(cancellationToken);
    }

    public async Task<BpoDashboard> GetDashboardAsync(
        Guid operatorUserId, CancellationToken cancellationToken = default)
    {
        var targets = await AssignedTenantsAsync(operatorUserId, cancellationToken);
        var rows = new List<BpoPeriodItem>();
        foreach (var target in targets)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            using var tenant = scope.ServiceProvider.GetRequiredService<ITenantScope>().BeginScope(target.TenantId);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var periods = await db.AccountingPeriods.AsNoTracking().Include(x => x.Snapshots)
                .Where(x => x.Status == AccountingPeriodStatuses.PendingAccountant ||
                            x.Status == AccountingPeriodStatuses.Approved)
                .OrderBy(x => x.Year).ThenBy(x => x.Month).ToArrayAsync(cancellationToken);
            rows.AddRange(periods.Select(period => new BpoPeriodItem(
                target.TenantId, target.Name, period.Id, period.Year, period.Month,
                period.Status, period.ValidatedAt,
                period.Snapshots.OrderByDescending(x => x.Revision).Select(x => (decimal?)x.Result).FirstOrDefault())));
        }
        var result = rows.OrderBy(x => x.Year).ThenBy(x => x.Month).ThenBy(x => x.TenantName).ToArray();
        return new BpoDashboard(result,
            result.Count(x => x.Status == AccountingPeriodStatuses.PendingAccountant),
            result.Count(x => x.Status == AccountingPeriodStatuses.Approved));
    }

    public async Task<BpoBatchResult> ExecuteBatchAsync(
        Guid operatorUserId, BpoBatchRequest request, CancellationToken cancellationToken = default)
    {
        if (request.PeriodIds.Length is 0 or > 100)
            throw new ArgumentException("Selecione entre 1 e 100 competencias.");
        if (request.Action is not ("Approve" or "Close"))
            throw new ArgumentException("A acao deve ser Approve ou Close.");

        var targets = await AssignedTenantsAsync(operatorUserId, cancellationToken);
        var results = new List<BpoBatchItemResult>();
        foreach (var periodId in request.PeriodIds.Distinct())
        {
            var handled = false;
            foreach (var target in targets)
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                using var tenant = scope.ServiceProvider.GetRequiredService<ITenantScope>().BeginScope(target.TenantId);
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                if (!await db.AccountingPeriods.AnyAsync(x => x.Id == periodId, cancellationToken))
                    continue;
                handled = true;
                try
                {
                    var closing = scope.ServiceProvider.GetRequiredService<PeriodClosingService>();
                    if (request.Action == "Approve")
                        await closing.ApproveAsync(periodId, operatorUserId, cancellationToken);
                    else
                        await closing.CloseAsync(periodId, operatorUserId, cancellationToken);
                    results.Add(new BpoBatchItemResult(periodId, true, null));
                }
                catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
                {
                    results.Add(new BpoBatchItemResult(periodId, false, exception.Message));
                }
                break;
            }
            if (!handled)
                results.Add(new BpoBatchItemResult(periodId, false, "Competencia fora da carteira atribuida ao operador."));
        }
        return new BpoBatchResult(results.ToArray());
    }

    private async Task<(Guid TenantId, string Name)[]> AssignedTenantsAsync(
        Guid operatorUserId, CancellationToken cancellationToken)
    {
        _ = operatorTenantContext.TenantId ?? throw new UnauthorizedAccessException("Tenant interno BPO obrigatorio.");
        return await (from assignment in operatorDb.BpoTenantAssignments.AsNoTracking()
                      join tenant in operatorDb.Tenants.AsNoTracking().IgnoreQueryFilters()
                          on assignment.TargetTenantId equals tenant.Id
                      where assignment.OperatorUserId == operatorUserId && assignment.RevokedAt == null
                      select new ValueTuple<Guid, string>(tenant.Id, tenant.Name)).ToArrayAsync(cancellationToken);
    }
}
