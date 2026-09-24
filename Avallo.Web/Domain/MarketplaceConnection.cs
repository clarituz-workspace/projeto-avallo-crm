namespace Avallo.Web.Domain;

public static class MarketplaceConnectionStates
{
    public const string Active = "Active";
    public const string Expired = "Expired";
    public const string Revoked = "Revoked";
    public const string Error = "Error";
}

public sealed class MarketplaceConnection : ITenantEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public required string ConnectorName { get; init; }
    public required string ExternalAccountId { get; init; }
    public string? AccountDisplayName { get; set; }
    public required string EncryptedAccessToken { get; set; }
    public string? EncryptedRefreshToken { get; set; }
    public DateTimeOffset TokenExpiresAt { get; set; }
    public string Status { get; set; } = MarketplaceConnectionStates.Active;
    public string? StatusMessage { get; set; }
    public DateTimeOffset? LastStatusCheckAt { get; set; }
    public DateTimeOffset? LastSyncAt { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? SyncLeaseId { get; set; }
    public DateTimeOffset? SyncLeaseUntil { get; set; }
}

public sealed class MarketplaceOrder : ITenantEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ConnectionId { get; init; }
    public required string OrderId { get; init; }
    public required string Platform { get; init; }
    public DateTimeOffset SaleDate { get; set; }
    public decimal GrossValue { get; set; }
    public decimal PlatformFee { get; set; }
    public decimal NetValue { get; set; }
    public required string PaymentMethod { get; set; }
    public DateTimeOffset? PaymentDate { get; set; }
    public DateTimeOffset? ReleaseDate { get; set; }
    public required string Status { get; set; }
    public string FulfillmentStatus { get; set; } = "Unknown";
    public DateTimeOffset? DeliveredAt { get; set; }
    public string Currency { get; set; } = "BRL";
    public required string BuyerName { get; set; }
    public string? InvoiceNumber { get; set; }
    public DateTimeOffset SyncedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<MarketplaceOrderItem> Items { get; set; } = [];
}

public sealed class MarketplaceOrderItem : ITenantEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid MarketplaceOrderId { get; init; }
    public string? Sku { get; set; }
    public required string Title { get; set; }
    public int Quantity { get; set; }
    public decimal UnitValue { get; set; }
}
