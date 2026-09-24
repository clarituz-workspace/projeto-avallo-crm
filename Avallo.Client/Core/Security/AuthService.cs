using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Avallo.Client.Models;

namespace Avallo.Client.Services;

public sealed class AuthService(HttpClient httpClient)
{
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;

    public UserModel? CurrentUser { get; private set; }
    public bool IsAuthenticated => CurrentUser is not null && !string.IsNullOrWhiteSpace(_accessToken);
    public bool IsInitialized { get; private set; }
    public event Action? SessionChanged;

    public async Task InitializeAsync(bool forceRefresh = false)
    {
        if (!OperatingSystem.IsBrowser())
        {
            IsInitialized = true;
            return;
        }

        if (IsInitialized && !forceRefresh)
            return;

        var observedToken = _accessToken;
        await _initializationLock.WaitAsync();
        try
        {
            if (IsInitialized && !forceRefresh)
                return;
            if (forceRefresh && !string.Equals(observedToken, _accessToken, StringComparison.Ordinal) &&
                _accessTokenExpiresAt > DateTimeOffset.UtcNow.AddSeconds(30))
                return;

            using var response = await httpClient.PostAsync("api/auth/refresh", null);
            if (response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.NoContent)
                    ClearSession();
                else
                    SetSession(await response.Content.ReadFromJsonAsync<TokenModel>());
            }
            else
                ClearSession();
            IsInitialized = true;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    public async Task<AuthResult> LoginAsync(LoginModel model)
    {
        using var response = await httpClient.PostAsJsonAsync("api/auth/login", model);
        return await HandleTokenResponseAsync(response);
    }

    public async Task<AuthResult> RegisterAsync(RegisterModel model)
    {
        using var response = await httpClient.PostAsJsonAsync("api/auth/register", model);
        return await HandleTokenResponseAsync(response);
    }

    public async Task<string?> GetAccessTokenAsync()
    {
        if (_accessTokenExpiresAt <= DateTimeOffset.UtcNow.AddSeconds(30))
            await InitializeAsync(forceRefresh: true);
        return _accessToken;
    }

    public string GetGoogleLoginUrl(string tenantName) =>
        $"api/auth/google?tenantName={Uri.EscapeDataString(tenantName)}";

    public async Task<AuthResult> UpdateProfileAsync(string displayName)
    {
        using var response = await SendAuthorizedAsync(() =>
        {
            var request = CreateAuthorizedRequest(HttpMethod.Patch, "api/auth/me");
            request.Content = JsonContent.Create(new { displayName });
            return request;
        });
        if (!response.IsSuccessStatusCode)
            return AuthResult.Failure(await ReadErrorAsync(response));

        CurrentUser = await response.Content.ReadFromJsonAsync<UserModel>();
        SessionChanged?.Invoke();
        return AuthResult.Success();
    }

    public async Task<AuthResult> CreateUserAsync(CreateUserModel model)
    {
        using var response = await SendAuthorizedAsync(() =>
        {
            var request = CreateAuthorizedRequest(HttpMethod.Post, "api/users");
            request.Content = JsonContent.Create(model);
            return request;
        });
        return response.IsSuccessStatusCode
            ? AuthResult.Success()
            : AuthResult.Failure(await ReadErrorAsync(response));
    }

    public async Task<AuthResult> ChangePasswordAsync(string currentPassword, string newPassword)
    {
        var result = await PostAsync<object, UserModel>("api/auth/change-password",
            new { currentPassword, newPassword });
        if (!result.Succeeded)
            return AuthResult.Failure(result.Error!);
        CurrentUser = result.Value;
        await InitializeAsync(forceRefresh: true);
        return AuthResult.Success();
    }

    public async Task<ApiResult<T>> GetAsync<T>(string uri, CancellationToken cancellationToken = default)
    {
        using var response = await SendAuthorizedAsync(
            () => CreateAuthorizedRequest(HttpMethod.Get, uri), cancellationToken);
        if (!response.IsSuccessStatusCode)
            return new ApiResult<T>(false, Error: await ReadErrorAsync(response));

        var value = await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
        return new ApiResult<T>(value is not null, value, value is null ? "A resposta do servidor esta vazia." : null);
    }

    public async Task<ApiResult<DownloadedFile>> DownloadAsync(
        string uri,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAuthorizedAsync(
            () => CreateAuthorizedRequest(HttpMethod.Get, uri), cancellationToken);
        if (!response.IsSuccessStatusCode)
            return new ApiResult<DownloadedFile>(false, Error: await ReadErrorAsync(response));

        var content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var disposition = response.Content.Headers.ContentDisposition;
        var fileName = (disposition?.FileNameStar ?? disposition?.FileName ?? "relatorio")
            .Trim('"');
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        return new ApiResult<DownloadedFile>(true, new DownloadedFile(content, contentType, fileName));
    }

    public async Task<AuthResult> PostAsync(string uri, CancellationToken cancellationToken = default)
    {
        using var response = await SendAuthorizedAsync(
            () => CreateAuthorizedRequest(HttpMethod.Post, uri), cancellationToken);
        return response.IsSuccessStatusCode
            ? AuthResult.Success()
            : AuthResult.Failure(await ReadErrorAsync(response));
    }

    public async Task<ApiResult<TResponse>> PutAsync<TRequest, TResponse>(
        string uri,
        TRequest value,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAuthorizedAsync(() =>
        {
            var request = CreateAuthorizedRequest(HttpMethod.Put, uri);
            request.Content = JsonContent.Create(value);
            return request;
        }, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return new ApiResult<TResponse>(false, Error: await ReadErrorAsync(response));
        var result = await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: cancellationToken);
        return new ApiResult<TResponse>(result is not null, result, result is null ? "A resposta do servidor esta vazia." : null);
    }

    public async Task<ApiResult<TResponse>> PostAsync<TRequest, TResponse>(
        string uri,
        TRequest value,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAuthorizedAsync(() =>
        {
            var request = CreateAuthorizedRequest(HttpMethod.Post, uri);
            request.Content = JsonContent.Create(value);
            return request;
        }, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return new ApiResult<TResponse>(false, Error: await ReadErrorAsync(response));
        var result = await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: cancellationToken);
        return new ApiResult<TResponse>(result is not null, result, result is null ? "A resposta do servidor esta vazia." : null);
    }

    public async Task<ApiResult<TResponse>> PatchAsync<TRequest, TResponse>(
        string uri, TRequest value, CancellationToken cancellationToken = default)
    {
        using var response = await SendAuthorizedAsync(() =>
        {
            var request = CreateAuthorizedRequest(HttpMethod.Patch, uri);
            request.Content = JsonContent.Create(value);
            return request;
        }, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return new ApiResult<TResponse>(false, Error: await ReadErrorAsync(response));
        var result = await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: cancellationToken);
        return new ApiResult<TResponse>(result is not null, result,
            result is null ? "A resposta do servidor esta vazia." : null);
    }

    public async Task<ApiResult<TResponse>> PostAsync<TResponse>(
        string uri,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAuthorizedAsync(
            () => CreateAuthorizedRequest(HttpMethod.Post, uri), cancellationToken);
        if (!response.IsSuccessStatusCode)
            return new ApiResult<TResponse>(false, Error: await ReadErrorAsync(response));
        var result = await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: cancellationToken);
        return new ApiResult<TResponse>(result is not null, result, result is null ? "A resposta do servidor esta vazia." : null);
    }

    public async Task<ApiResult<TResponse>> PostFileAsync<TResponse>(
        string uri, byte[] content, string fileName, string contentType,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAuthorizedAsync(() =>
        {
            var request = CreateAuthorizedRequest(HttpMethod.Post, uri);
            var multipart = new MultipartFormDataContent();
            var file = new ByteArrayContent(content);
            file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            multipart.Add(file, "file", fileName);
            request.Content = multipart;
            return request;
        }, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return new ApiResult<TResponse>(false, Error: await ReadErrorAsync(response));
        var result = await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: cancellationToken);
        return new ApiResult<TResponse>(result is not null, result, result is null ? "A resposta do servidor esta vazia." : null);
    }

    public async Task<ApiResult<TResponse>> PutFileAsync<TResponse>(
        string uri, byte[] content, string fileName, string contentType,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAuthorizedAsync(() =>
        {
            var request = CreateAuthorizedRequest(HttpMethod.Put, uri);
            var multipart = new MultipartFormDataContent();
            var file = new ByteArrayContent(content);
            file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            multipart.Add(file, "file", fileName);
            request.Content = multipart;
            return request;
        }, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return new ApiResult<TResponse>(false, Error: await ReadErrorAsync(response));
        var result = await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: cancellationToken);
        return new ApiResult<TResponse>(result is not null, result,
            result is null ? "A resposta do servidor esta vazia." : null);
    }

    public async Task<AuthResult> DeleteAsync(string uri, CancellationToken cancellationToken = default)
    {
        using var response = await SendAuthorizedAsync(
            () => CreateAuthorizedRequest(HttpMethod.Delete, uri), cancellationToken);
        return response.IsSuccessStatusCode
            ? AuthResult.Success()
            : AuthResult.Failure(await ReadErrorAsync(response));
    }

    public async Task LogoutAsync()
    {
        await httpClient.PostAsync("api/auth/logout", null);
        ClearSession();
        IsInitialized = true;
    }

    private async Task<AuthResult> HandleTokenResponseAsync(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            return AuthResult.Failure(await ReadErrorAsync(response));

        SetSession(await response.Content.ReadFromJsonAsync<TokenModel>());
        IsInitialized = true;
        return AuthResult.Success();
    }

    private HttpRequestMessage CreateAuthorizedRequest(HttpMethod method, string uri)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        return request;
    }

    private async Task<HttpResponseMessage> SendAuthorizedAsync(
        Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken = default)
    {
        if (_accessTokenExpiresAt <= DateTimeOffset.UtcNow.AddSeconds(30))
            await InitializeAsync(forceRefresh: true);

        // A page and its layout can initialize concurrently in Blazor. If session
        // restoration found no refresh cookie, do not issue a protected request
        // with an empty bearer token while the layout redirects to the login page.
        if (string.IsNullOrWhiteSpace(_accessToken))
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);

        using var request = requestFactory();
        var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
            return response;

        response.Dispose();
        await InitializeAsync(forceRefresh: true);
        if (string.IsNullOrWhiteSpace(_accessToken))
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        using var retry = requestFactory();
        return await httpClient.SendAsync(retry, cancellationToken);
    }

    private void SetSession(TokenModel? token)
    {
        _accessToken = token?.AccessToken;
        _accessTokenExpiresAt = token?.ExpiresAt ?? DateTimeOffset.MinValue;
        CurrentUser = token?.User;
        SessionChanged?.Invoke();
    }

    private void ClearSession()
    {
        _accessToken = null;
        _accessTokenExpiresAt = DateTimeOffset.MinValue;
        CurrentUser = null;
        SessionChanged?.Invoke();
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            return "Muitas tentativas. Aguarde um minuto e tente novamente.";

        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = document.RootElement;
            if (root.TryGetProperty("detail", out var detail) && !string.IsNullOrWhiteSpace(detail.GetString()))
                return detail.GetString()!;
            if (root.TryGetProperty("message", out var message))
                return message.GetString() ?? "Nao foi possivel concluir a operacao.";
            if (root.TryGetProperty("errors", out var errors))
            {
                var first = errors.EnumerateObject().FirstOrDefault();
                if (first.Value.ValueKind == JsonValueKind.Array)
                    return first.Value.EnumerateArray().FirstOrDefault().GetString()
                           ?? "Revise os dados informados.";
            }
        }
        catch (JsonException)
        {
        }

        return "Nao foi possivel concluir a operacao. Tente novamente.";
    }
}
