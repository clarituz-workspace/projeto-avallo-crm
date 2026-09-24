using Avallo.Client.Models;

namespace Avallo.Client.Services;

public sealed class UserAccessClient(AuthService authService)
{
    public Task<ApiResult<TenantUserModel[]>> ListAsync(CancellationToken cancellationToken = default) =>
        authService.GetAsync<TenantUserModel[]>("api/users", cancellationToken);

    public Task<ApiResult<UserModel>> CreateAsync(CreateUserModel model,
        CancellationToken cancellationToken = default) =>
        authService.PostAsync<CreateUserModel, UserModel>("api/users", model, cancellationToken);

    public Task<ApiResult<TenantUserModel>> SetActiveAsync(Guid userId, bool isActive,
        CancellationToken cancellationToken = default) =>
        authService.PatchAsync<object, TenantUserModel>($"api/users/{userId}/status",
            new { isActive }, cancellationToken);
}
