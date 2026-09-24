using System.ComponentModel.DataAnnotations;

namespace Avallo.Web.Features.AI;

public sealed class AzureAiChatOptions
{
    public const string SectionName = "AzureAI";
    [Url] public string Endpoint { get; init; } = string.Empty;
    [Required] public string Deployment { get; init; } = "Phi-4-mini-instruct";
    public string Scope { get; init; } = "https://ai.azure.com/.default";
    public string? ManagedIdentityClientId { get; init; }
    public int MaxOutputTokens { get; init; } = 800;
    public double Temperature { get; init; } = 0.3;
    public string SystemPrompt { get; init; } = "Você é o assistente conversacional da plataforma Avallo. Responda em português do Brasil, seja objetivo e nunca invente dados financeiros ou operacionais.";
}
