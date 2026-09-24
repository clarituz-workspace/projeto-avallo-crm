using Azure.Identity;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel.Primitives;
using Microsoft.Extensions.Options;

#pragma warning disable OPENAI001

namespace Avallo.Web.Features.AI;

public sealed class AzureAiChatService
{
    private readonly AzureAiChatOptions _settings;
    private readonly ChatClient _client;

    public AzureAiChatService(IOptions<AzureAiChatOptions> options)
    {
        _settings = options.Value;
        if (!Uri.TryCreate(_settings.Endpoint, UriKind.Absolute, out var endpoint) ||
            string.IsNullOrWhiteSpace(_settings.Deployment))
            throw new InvalidOperationException("Azure AI Chat is not configured.");

        var credentialOptions = new DefaultAzureCredentialOptions
        {
            ManagedIdentityClientId = _settings.ManagedIdentityClientId
        };
        var tokenPolicy = new BearerTokenPolicy(new DefaultAzureCredential(credentialOptions), _settings.Scope);
        _client = new ChatClient(
            authenticationPolicy: tokenPolicy,
            model: _settings.Deployment,
            options: new OpenAIClientOptions { Endpoint = endpoint });
    }

    public async Task<string> CompleteAsync(IReadOnlyCollection<ChatMessageRequest> messages, CancellationToken cancellationToken)
    {
        var chatMessages = new List<ChatMessage> { new SystemChatMessage(_settings.SystemPrompt) };
        foreach (var message in messages.TakeLast(30))
        {
            var content = message.Content.Trim();
            if (content.Length == 0) continue;
            content = content[..Math.Min(content.Length, 6000)];
            chatMessages.Add(message.Role == "assistant"
                ? new AssistantChatMessage(content)
                : new UserChatMessage(content));
        }

        var completion = await _client.CompleteChatAsync(chatMessages, cancellationToken: cancellationToken);
        return completion.Value.Content.FirstOrDefault()?.Text
            ?? throw new InvalidOperationException("Azure AI returned an empty response.");
    }
}

public sealed record ChatMessageRequest(string Role, string Content);
