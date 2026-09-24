using Microsoft.AspNetCore.Mvc;
using Avallo.Web.Domain;

namespace Avallo.Web.Features.AI;

public static class ChatEndpoints
{
    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/ai/chat", CompleteAsync)
            .RequireAuthorization(Policies.TenantMember)
            .WithTags("AI")
            .WithName("CompleteAvalloChat")
            .WithSummary("Conversa com o assistente do Avallo");
        return endpoints;
    }

    private static async Task<IResult> CompleteAsync(
        ChatRequest request,
        AzureAiChatService chat,
        CancellationToken cancellationToken)
    {
        if (request.Messages is null || request.Messages.Count == 0)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["messages"] = ["Informe ao menos uma mensagem."] });

        try
        {
            var answer = await chat.CompleteAsync(request.Messages.TakeLast(30).ToArray(), cancellationToken);
            return Results.Ok(new ChatResponse(answer));
        }
        catch (InvalidOperationException)
        {
            return Results.Problem("O assistente de IA ainda não foi configurado.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return Results.Problem("O serviço de IA está temporariamente indisponível.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}

public sealed record ChatRequest(IReadOnlyCollection<ChatMessageRequest> Messages);
public sealed record ChatResponse(string Answer);
