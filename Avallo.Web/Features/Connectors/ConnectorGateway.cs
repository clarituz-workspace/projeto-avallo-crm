using Avallo.Connectors.Abstractions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Avallo.Web.Domain;
using Avallo.Web.Infrastructure;

namespace Avallo.Web.Features.Connectors;

public sealed class ConnectorGateway(
    AppDbContext db,
    ConnectorRegistry registry,
    ITenantContext tenantContext,
    IDataProtectionProvider dataProtectionProvider,
    TimeProvider timeProvider)
{
    private readonly IDataProtectionProvider _dataProtectionProvider = dataProtectionProvider;

    public IReadOnlyCollection<ConnectorDescriptor> AvailableConnectors => registry.Descriptors;

    public async Task<MarketplaceConnection> AuthenticateAsync(
        string connectorName,
        IReadOnlyDictionary<string, string> credentials,
        string? callbackUrl,
        CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant();
        var connector = registry.Get(connectorName);
        var authentication = await connector.AuthenticateAsync(
            new AuthenticationRequest(tenantId, credentials, callbackUrl), cancellationToken);
        ValidateAuthentication(authentication);
        var connection = await db.MarketplaceConnections.SingleOrDefaultAsync(x =>
            x.ConnectorName == connector.Descriptor.Name &&
            x.ExternalAccountId == authentication.ExternalAccountId, cancellationToken);
        if (connection is null)
        {
            connection = new MarketplaceConnection
            {
                TenantId = tenantId,
                ConnectorName = connector.Descriptor.Name,
                ExternalAccountId = authentication.ExternalAccountId,
                EncryptedAccessToken = string.Empty
            };
            db.MarketplaceConnections.Add(connection);
        }
        ApplyAuthentication(connection, authentication);
        await db.SaveChangesAsync(cancellationToken);
        return connection;
    }

    public async Task<(MarketplaceConnection Connection, IMarketplaceConnector Connector, ConnectorContext Context)> GetExecutionAsync(
        Guid connectionId,
        CancellationToken cancellationToken)
    {
        var connection = await db.MarketplaceConnections.SingleOrDefaultAsync(x => x.Id == connectionId, cancellationToken)
            ?? throw new KeyNotFoundException("Marketplace connection was not found.");
        var connector = registry.Get(connection.ConnectorName);
        if (connection.TokenExpiresAt <= timeProvider.GetUtcNow().AddMinutes(2))
            await RefreshAsync(connection, connector, cancellationToken);
        if (connection.Status is MarketplaceConnectionStates.Expired or MarketplaceConnectionStates.Revoked)
            throw new ConnectorException(connection.ConnectorName, "connection_inactive", "The marketplace connection is not active.");
        return (connection, connector, Context(connection));
    }

    public async Task<ConnectorStatus> RefreshStatusAsync(Guid connectionId, CancellationToken cancellationToken)
    {
        var execution = await GetExecutionAsync(connectionId, cancellationToken);
        var status = await execution.Connector.GetStatusAsync(execution.Context, cancellationToken);
        execution.Connection.Status = status.State.ToString();
        execution.Connection.StatusMessage = status.Message;
        execution.Connection.LastStatusCheckAt = status.CheckedAt ?? timeProvider.GetUtcNow();
        execution.Connection.UpdatedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return status;
    }

    public async Task RefreshTokenAsync(Guid connectionId, CancellationToken cancellationToken)
    {
        var connection = await db.MarketplaceConnections.SingleOrDefaultAsync(x => x.Id == connectionId, cancellationToken)
            ?? throw new KeyNotFoundException("Marketplace connection was not found.");
        await RefreshAsync(connection, registry.Get(connection.ConnectorName), cancellationToken);
    }

    public async Task<IReadOnlyCollection<MarketplaceConnection>> ListConnectionsAsync(CancellationToken cancellationToken) =>
        await db.MarketplaceConnections.AsNoTracking().OrderBy(x => x.ConnectorName).ThenBy(x => x.AccountDisplayName).ToListAsync(cancellationToken);

    public async Task DisconnectAsync(Guid connectionId, CancellationToken cancellationToken)
    {
        var connection = await db.MarketplaceConnections.SingleOrDefaultAsync(x => x.Id == connectionId, cancellationToken)
            ?? throw new KeyNotFoundException("Marketplace connection was not found.");
        connection.EncryptedAccessToken = string.Empty;
        connection.EncryptedRefreshToken = null;
        connection.Status = MarketplaceConnectionStates.Revoked;
        connection.StatusMessage = "Connection disconnected by the user.";
        connection.UpdatedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> TryAcquireSyncLeaseAsync(Guid connectionId, Guid leaseId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var changed = await db.MarketplaceConnections
            .Where(x => x.Id == connectionId && (x.SyncLeaseUntil == null || x.SyncLeaseUntil < now))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.SyncLeaseId, leaseId)
                .SetProperty(x => x.SyncLeaseUntil, now.AddMinutes(30)), cancellationToken);
        return changed == 1;
    }

    public Task ReleaseSyncLeaseAsync(Guid connectionId, Guid leaseId, CancellationToken cancellationToken) =>
        db.MarketplaceConnections
            .Where(x => x.Id == connectionId && x.SyncLeaseId == leaseId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.SyncLeaseId, (Guid?)null)
                .SetProperty(x => x.SyncLeaseUntil, (DateTimeOffset?)null), cancellationToken);

    private async Task RefreshAsync(
        MarketplaceConnection connection,
        IMarketplaceConnector connector,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(connection.EncryptedRefreshToken))
        {
            connection.Status = MarketplaceConnectionStates.Expired;
            await db.SaveChangesAsync(cancellationToken);
            throw new ConnectorException(connection.ConnectorName, "refresh_token_missing", "The connection must be authenticated again.");
        }
        try
        {
            var authentication = await connector.RefreshTokenAsync(
                new RefreshTokenRequest(RequireTenant(), Protector(connection).Unprotect(connection.EncryptedRefreshToken), connection.ExternalAccountId), cancellationToken);
            ValidateAuthentication(authentication);
            ApplyAuthentication(connection, authentication);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (ConnectorException exception) when (!exception.IsTransient)
        {
            connection.Status = MarketplaceConnectionStates.Expired;
            connection.StatusMessage = exception.Message;
            await db.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    private ConnectorContext Context(MarketplaceConnection connection) => new(
        connection.TenantId, connection.Id, Protector(connection).Unprotect(connection.EncryptedAccessToken), connection.ExternalAccountId);

    private void ApplyAuthentication(MarketplaceConnection connection, ConnectorAuthentication authentication)
    {
        if (!string.IsNullOrWhiteSpace(authentication.AccountDisplayName))
            connection.AccountDisplayName = authentication.AccountDisplayName;
        connection.EncryptedAccessToken = Protector(connection).Protect(authentication.AccessToken);
        if (!string.IsNullOrWhiteSpace(authentication.RefreshToken))
            connection.EncryptedRefreshToken = Protector(connection).Protect(authentication.RefreshToken);
        connection.TokenExpiresAt = authentication.ExpiresAt;
        connection.Status = MarketplaceConnectionStates.Active;
        connection.StatusMessage = null;
        connection.UpdatedAt = timeProvider.GetUtcNow();
    }

    private static void ValidateAuthentication(ConnectorAuthentication authentication)
    {
        if (string.IsNullOrWhiteSpace(authentication.AccessToken) || string.IsNullOrWhiteSpace(authentication.ExternalAccountId))
            throw new InvalidOperationException("Connector returned an invalid authentication response.");
    }

    private Guid RequireTenant() => tenantContext.TenantId ?? throw new UnauthorizedAccessException("Tenant is required.");

    private IDataProtector Protector(MarketplaceConnection connection) =>
        _dataProtectionProvider.CreateProtector(
            "Avallo.ConnectorTokens.v1", connection.TenantId.ToString(), connection.ConnectorName);
}
