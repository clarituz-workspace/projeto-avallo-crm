using System.Security.Claims;
using Avallo.Web.Domain;

namespace Avallo.Web.Features.Bpo;

public static class BpoEndpoints
{
    public static IEndpointRouteBuilder MapBpoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/bpo").WithTags("BPO")
            .RequireAuthorization(Policies.CanOperateBpo);
        group.MapGet("/dashboard", GetDashboardAsync);
        group.MapPost("/periods/batch", ExecuteBatchAsync);
        endpoints.MapPost("/api/bpo/assignments", AssignAsync).WithTags("BPO")
            .RequireAuthorization(Policies.CanManageBpo);
        endpoints.MapDelete("/api/bpo/assignments/{id:guid}", RevokeAsync).WithTags("BPO")
            .RequireAuthorization(Policies.CanManageBpo);
        return endpoints;
    }

    private static async Task<IResult> AssignAsync(
        AssignBpoTenantRequest request, ClaimsPrincipal user, BpoOperationsService service,
        CancellationToken cancellationToken)
    {
        try { return Results.Ok(await service.AssignTenantAsync(UserId(user), request, cancellationToken)); }
        catch (ArgumentException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["assignment"] = [exception.Message] });
        }
    }

    private static async Task<IResult> RevokeAsync(
        Guid id, BpoOperationsService service, CancellationToken cancellationToken)
    {
        try { await service.RevokeTenantAsync(id, cancellationToken); return Results.NoContent(); }
        catch (KeyNotFoundException) { return Results.NotFound(); }
    }

    private static Task<BpoDashboard> GetDashboardAsync(
        ClaimsPrincipal user, BpoOperationsService service, CancellationToken cancellationToken) =>
        service.GetDashboardAsync(UserId(user), cancellationToken);

    private static async Task<IResult> ExecuteBatchAsync(
        BpoBatchRequest request, ClaimsPrincipal user, BpoOperationsService service,
        CancellationToken cancellationToken)
    {
        try { return Results.Ok(await service.ExecuteBatchAsync(UserId(user), request, cancellationToken)); }
        catch (ArgumentException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["batch"] = [exception.Message] });
        }
    }

    private static Guid UserId(ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
