using System.Security.Claims;
using Avallo.Web.Domain;
using Avallo.Web.Features.Auth;

namespace Avallo.Web.Features.Reconciliation;

public static class ReconciliationEndpoints
{
    private const long MaximumStatementSize = 5 * 1024 * 1024;

    public static IEndpointRouteBuilder MapReconciliationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/reconciliation").WithTags("Reconciliation")
            .RequireAuthorization(Policies.TenantMember);
        group.MapGet("/overview", GetOverviewAsync);
        group.MapPost("/imports", ImportAsync).RequireAuthorization(Policies.CanWrite).DisableAntiforgery();
        group.MapPost("/transactions/{transactionId:guid}/confirm", ConfirmAsync)
            .RequireAuthorization(Policies.CanReviewAccounting);
        group.MapPost("/transactions/{transactionId:guid}/ignore", IgnoreAsync)
            .RequireAuthorization(Policies.CanReviewAccounting);
        return endpoints;
    }

    private static async Task<IResult> GetOverviewAsync(DateOnly? from, DateOnly? to,
        ReconciliationService service, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var start = from ?? new DateOnly(today.Year, today.Month, 1);
        var end = to ?? start.AddMonths(1).AddDays(-1);
        try { return Results.Ok(await service.GetOverviewAsync(start, end, cancellationToken)); }
        catch (ArgumentException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["period"] = [exception.Message] });
        }
    }

    private static async Task<IResult> ImportAsync(IFormFile file, ClaimsPrincipal user,
        ReconciliationService service, CancellationToken cancellationToken)
    {
        if (file.Length is <= 0 or > MaximumStatementSize)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["O extrato deve ter entre 1 byte e 5 MB."] });
        if (!file.FileName.EndsWith(".ofx", StringComparison.OrdinalIgnoreCase) &&
            !file.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["Envie um extrato OFX ou CSV."] });
        await using var memory = new MemoryStream((int)file.Length);
        await file.CopyToAsync(memory, cancellationToken);
        try
        {
            var imported = await service.ImportAsync(memory.ToArray(), file.FileName,
                file.ContentType ?? "application/octet-stream", RequireUser(user), cancellationToken);
            return Results.Ok(new ReconciliationImportResponse(imported.Id, imported.Source,
                imported.OriginalFileName, imported.AccountReference, imported.PeriodStart, imported.PeriodEnd,
                imported.Transactions.Count, imported.ImportedAt));
        }
        catch (InvalidDataException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = [exception.Message] });
        }
        catch (ReconciliationConflictException exception)
        {
            return Results.Conflict(new { message = exception.Message });
        }
    }

    private static async Task<IResult> ConfirmAsync(Guid transactionId, ConfirmReconciliationRequest request,
        ClaimsPrincipal user, ReconciliationService service, CancellationToken cancellationToken)
    {
        try
        {
            await service.ConfirmAsync(transactionId, request.Allocations, RequireUser(user), cancellationToken);
            return Results.Ok(new ReconciliationActionResponse(transactionId, ReconciliationTransactionStatuses.Matched));
        }
        catch (ArgumentException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["allocations"] = [exception.Message] });
        }
        catch (ReconciliationConflictException exception)
        {
            return Results.Conflict(new { message = exception.Message });
        }
    }

    private static async Task<IResult> IgnoreAsync(Guid transactionId, IgnoreReconciliationRequest request,
        ClaimsPrincipal user, ReconciliationService service, CancellationToken cancellationToken)
    {
        try
        {
            await service.IgnoreAsync(transactionId, request.Reason, RequireUser(user), cancellationToken);
            return Results.Ok(new ReconciliationActionResponse(transactionId, ReconciliationTransactionStatuses.Ignored));
        }
        catch (ArgumentException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["reason"] = [exception.Message] });
        }
        catch (ReconciliationConflictException exception)
        {
            return Results.Conflict(new { message = exception.Message });
        }
    }

    private static Guid RequireUser(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : throw new UnauthorizedAccessException("User is required.");
}

public sealed record ConfirmReconciliationRequest(ReconciliationAllocationRequest[] Allocations);
public sealed record IgnoreReconciliationRequest(string Reason);
public sealed record ReconciliationActionResponse(Guid TransactionId, string Status);
