using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Avallo.Web.Domain;
using Avallo.Web.Features.Auth;
using Avallo.Web.Infrastructure;

namespace Avallo.Web.Features.PeriodClosing;

public static class PeriodClosingEndpoints
{
    public static IEndpointRouteBuilder MapPeriodClosingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/accounting-periods").WithTags("Accounting periods")
            .RequireAuthorization(Policies.TenantMember);
        group.MapGet("/", ListAsync);
        group.MapPost("/{year:int}/{month:int}", CreateAsync).RequireAuthorization(Policies.CanWrite);
        group.MapPost("/{id:guid}/validate", ValidateAsync).RequireAuthorization(Policies.CanWrite);
        group.MapPost("/{id:guid}/approve", ApproveAsync).RequireAuthorization(Policies.CanReviewAccounting);
        group.MapPost("/{id:guid}/close", CloseAsync).RequireAuthorization(Policies.CanReviewAccounting);
        group.MapPost("/{id:guid}/reopen", ReopenAsync).RequireAuthorization(Policies.CanManageUsers);
        group.MapGet("/{id:guid}/snapshots/{snapshotId:guid}/download", DownloadAsync);
        return endpoints;
    }

    private static async Task<AccountingPeriodResponse[]> ListAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var periods = await db.AccountingPeriods.AsNoTracking().Include(x => x.Checks).Include(x => x.Snapshots)
            .OrderByDescending(x => x.Year).ThenByDescending(x => x.Month).ToArrayAsync(cancellationToken);
        return periods.Select(ToResponse).ToArray();
    }

    private static async Task<IResult> CreateAsync(
        int year, int month, PeriodClosingService service, AppDbContext db, CancellationToken cancellationToken)
    {
        try
        {
            var period = await service.GetOrCreateAsync(year, month, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ToResponse(period));
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["period"] = [exception.Message] });
        }
    }

    private static async Task<IResult> ValidateAsync(
        Guid id, ClaimsPrincipal user, PeriodClosingService service, CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.ValidateAsync(id, UserId(user), cancellationToken);
            return Results.Ok(new PeriodValidationResponse(ToResponse(result.Period), result.ValidationRunId,
                result.Checks.Select(ToResponse).ToArray(), result.Passed));
        }
        catch (InvalidOperationException exception) { return Results.Conflict(new { message = exception.Message }); }
    }

    private static async Task<IResult> ApproveAsync(
        Guid id, ClaimsPrincipal user, PeriodClosingService service, CancellationToken cancellationToken)
    {
        try { return Results.Ok(ToResponse(await service.ApproveAsync(id, UserId(user), cancellationToken))); }
        catch (InvalidOperationException exception) { return Results.Conflict(new { message = exception.Message }); }
    }

    private static async Task<IResult> CloseAsync(
        Guid id, ClaimsPrincipal user, PeriodClosingService service, AppDbContext db,
        CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await service.CloseAsync(id, UserId(user), cancellationToken);
            var period = await db.AccountingPeriods.AsNoTracking().Include(x => x.Checks).Include(x => x.Snapshots)
                .SingleAsync(x => x.Id == id, cancellationToken);
            return Results.Ok(ToResponse(period));
        }
        catch (InvalidOperationException exception) { return Results.Conflict(new { message = exception.Message }); }
    }

    private static async Task<IResult> ReopenAsync(
        Guid id, ReopenPeriodRequest request, ClaimsPrincipal user, PeriodClosingService service,
        CancellationToken cancellationToken)
    {
        try { return Results.Ok(ToResponse(await service.ReopenAsync(id, UserId(user), request.Reason, cancellationToken))); }
        catch (ArgumentException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["reason"] = [exception.Message] }); }
        catch (InvalidOperationException exception) { return Results.Conflict(new { message = exception.Message }); }
    }

    private static async Task<IResult> DownloadAsync(
        Guid id, Guid snapshotId, AppDbContext db, PeriodClosingService service, CancellationToken cancellationToken)
    {
        if (!await db.DreSnapshots.AnyAsync(x => x.Id == snapshotId && x.AccountingPeriodId == id, cancellationToken))
            return Results.NotFound();
        return Results.Ok(new SnapshotDownloadResponse(
            await service.CreateSnapshotDownloadUrlAsync(snapshotId, cancellationToken)));
    }

    private static AccountingPeriodResponse ToResponse(AccountingPeriod period)
    {
        var latestRun = period.Checks.OrderByDescending(x => x.CheckedAt).Select(x => (Guid?)x.ValidationRunId).FirstOrDefault();
        return new AccountingPeriodResponse(
            period.Id, period.Year, period.Month, period.StartDate, period.EndDate, period.Status,
            period.Version, period.ValidatedAt, period.ApprovedAt, period.ClosedAt, period.ReopenedAt,
            period.ReopenReason,
            period.Checks.Where(x => latestRun is not null && x.ValidationRunId == latestRun)
                .OrderBy(x => x.Code).Select(ToResponse).ToArray(),
            period.Snapshots.OrderByDescending(x => x.Revision).Select(ToResponse).ToArray());
    }

    private static PeriodCheckResponse ToResponse(AccountingPeriodCheck check) => new(
        check.Id, check.ValidationRunId, check.Code, check.Description, check.Passed,
        check.BlockerCount, check.BlockerDetails, check.CheckedAt);
    private static DreSnapshotResponse ToResponse(DreSnapshot snapshot) => new(
        snapshot.Id, snapshot.Revision, snapshot.CanonicalJsonSha256, snapshot.PdfSha256,
        snapshot.GeneratedAt, snapshot.GrossRevenue, snapshot.Deductions, snapshot.Taxes,
        snapshot.NetRevenue, snapshot.Cmv, snapshot.GrossProfit, snapshot.SellingExpense,
        snapshot.OperatingExpense, snapshot.Result);
    private static Guid UserId(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
}

public sealed record AccountingPeriodResponse(
    Guid Id, int Year, int Month, DateOnly StartDate, DateOnly EndDate, string Status, int Version,
    DateTimeOffset? ValidatedAt, DateTimeOffset? ApprovedAt, DateTimeOffset? ClosedAt,
    DateTimeOffset? ReopenedAt, string? ReopenReason, PeriodCheckResponse[] Checks, DreSnapshotResponse[] Snapshots);
public sealed record PeriodCheckResponse(
    Guid Id, Guid ValidationRunId, string Code, string Description, bool Passed,
    int BlockerCount, string BlockerDetails, DateTimeOffset CheckedAt);
public sealed record DreSnapshotResponse(
    Guid Id, int Revision, string CanonicalJsonSha256, string PdfSha256, DateTimeOffset GeneratedAt,
    decimal GrossRevenue, decimal Deductions, decimal Taxes, decimal NetRevenue, decimal Cmv,
    decimal GrossProfit, decimal SellingExpense, decimal OperatingExpense, decimal Result);
public sealed record PeriodValidationResponse(
    AccountingPeriodResponse Period, Guid ValidationRunId, PeriodCheckResponse[] Checks, bool Passed);
public sealed record ReopenPeriodRequest(string Reason);
public sealed record SnapshotDownloadResponse(string Url);
