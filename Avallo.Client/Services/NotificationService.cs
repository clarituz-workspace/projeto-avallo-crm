using Avallo.Client.Models;

namespace Avallo.Client.Services;

public sealed class NotificationService(AuthService authService)
{
    public Task<ApiResult<NotificationListModel>> GetAsync(
        bool unreadOnly = false,
        CancellationToken cancellationToken = default) =>
        authService.GetAsync<NotificationListModel>(
            $"api/notifications?unreadOnly={unreadOnly.ToString().ToLowerInvariant()}", cancellationToken);

    public Task<ApiResult<NotificationPreferenceModel>> GetPreferencesAsync(CancellationToken cancellationToken = default) =>
        authService.GetAsync<NotificationPreferenceModel>("api/notifications/preferences", cancellationToken);

    public Task<ApiResult<NotificationPreferenceModel>> SavePreferencesAsync(
        NotificationPreferenceModel preferences,
        CancellationToken cancellationToken = default) =>
        authService.PutAsync<NotificationPreferenceModel, NotificationPreferenceModel>(
            "api/notifications/preferences", preferences, cancellationToken);

    public Task<AuthResult> MarkReadAsync(Guid id, CancellationToken cancellationToken = default) =>
        authService.PostAsync($"api/notifications/{id}/read", cancellationToken);

    public Task<AuthResult> MarkAllReadAsync(CancellationToken cancellationToken = default) =>
        authService.PostAsync("api/notifications/read-all", cancellationToken);
}
