using Avallo.Client.Models;

namespace Avallo.Client.Services;

public sealed class ChatService(AuthService authService)
{
    public Task<ApiResult<ChatResponseModel>> SendAsync(
        IReadOnlyCollection<ChatMessageModel> messages,
        CancellationToken cancellationToken = default) =>
        authService.PostAsync<object, ChatResponseModel>(
            "api/ai/chat", new { messages }, cancellationToken);
}
