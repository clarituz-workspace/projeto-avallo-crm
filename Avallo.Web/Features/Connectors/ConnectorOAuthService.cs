using System.Security.Claims;
using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using Avallo.Connectors.Abstractions;
using Microsoft.AspNetCore.DataProtection;
using Avallo.Web.Infrastructure;

namespace Avallo.Web.Features.Connectors;

public sealed class ConnectorOAuthService(
    ConnectorRegistry registry,
    ConnectorGateway gateway,
    ITenantContext tenantContext,
    ITenantScope tenantScope,
    IDataProtectionProvider dataProtectionProvider,
    TimeProvider timeProvider)
{
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector("Avallo.ConnectorOAuthState.v1");

    public async Task<Uri> StartAsync(
        string connectorName,
        ClaimsPrincipal user,
        string callbackUrl,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.TenantId ?? throw new UnauthorizedAccessException("Tenant is required.");
        var connector = registry.Get(connectorName);
        if (connector is not IOAuthMarketplaceConnector oauthConnector || !connector.Descriptor.UsesOAuth)
            throw new ConnectorException(connectorName, "oauth_not_supported", "This connector does not support OAuth authentication.");
        var codeVerifier = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var codeChallenge = WebEncoders.Base64UrlEncode(
            SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(codeVerifier)));
        var state = new OAuthState(
            tenantId,
            Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!),
            connector.Descriptor.Name,
            timeProvider.GetUtcNow().AddMinutes(10),
            codeVerifier);
        var protectedState = _protector.Protect(JsonSerializer.Serialize(state));
        var authorization = await oauthConnector.BeginAuthenticationAsync(
            new OAuthStartRequest(tenantId, protectedState, callbackUrl, codeChallenge), cancellationToken);
        return authorization.AuthorizationUri;
    }

    public async Task CompleteAsync(
        string code,
        string protectedState,
        string callbackUrl,
        IReadOnlyDictionary<string, string>? providerCredentials,
        CancellationToken cancellationToken)
    {
        OAuthState state;
        try
        {
            state = JsonSerializer.Deserialize<OAuthState>(_protector.Unprotect(protectedState))
                    ?? throw new InvalidOperationException();
        }
        catch (Exception exception) when (exception is not ConnectorException)
        {
            throw new ConnectorException("oauth", "invalid_state", "The OAuth state is invalid or has been modified.", innerException: exception);
        }
        if (state.ExpiresAt < timeProvider.GetUtcNow())
            throw new ConnectorException(state.ConnectorName, "expired_state", "The OAuth flow expired. Start the connection again.");

        using var _ = tenantScope.BeginScope(state.TenantId);
        var credentials = providerCredentials is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(providerCredentials);
        credentials["code"] = code;
        credentials["code_verifier"] = state.CodeVerifier;
        await gateway.AuthenticateAsync(state.ConnectorName, credentials, callbackUrl, cancellationToken);
    }

    private sealed record OAuthState(
        Guid TenantId,
        Guid UserId,
        string ConnectorName,
        DateTimeOffset ExpiresAt,
        string CodeVerifier);
}
