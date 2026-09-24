namespace Avallo.Web.Domain;

public sealed class FinancialEntry : ITenantEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public required string ExternalId { get; init; }
    public required string Description { get; set; }
    public required string Marketplace { get; init; }
    public required string PaymentMethod { get; set; }
    public required string Status { get; set; }
    public DateTimeOffset OccurredAt { get; init; }
    public DateTimeOffset? ExpectedAt { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal ReceivedAmount { get; set; }
    public decimal FeeAmount { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
