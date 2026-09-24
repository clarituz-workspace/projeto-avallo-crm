using Avallo.Web.Domain;
using Avallo.Web.Features.Updates;

namespace Avallo.Web.Features.Deployment;

public static class DeploymentEndpoints
{
    public static IEndpointRouteBuilder MapDeploymentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHub<DeploymentHub>("/hubs/deployment");
        endpoints.MapHub<UpdateHub>("/updateHub");

        endpoints.MapPost("/api/system/deployment-notice", AnnounceAsync)
            .RequireAuthorization(Policies.CanManageUsers)
            .WithName("AnnounceDeployment")
            .WithSummary("Avisa clientes conectados sobre uma nova versao.");

        return endpoints;
    }

    private static async Task<IResult> AnnounceAsync(
        DeploymentNoticeRequest request,
        DeploymentNotificationService notifications,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Version))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.Version)] = ["A versao e obrigatoria."]
            });
        if (request.RestartInSeconds is < 15 or > 600)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.RestartInSeconds)] = ["Informe um prazo entre 15 e 600 segundos."]
            });

        return Results.Ok(await notifications.AnnounceAsync(request, cancellationToken));
    }
}
