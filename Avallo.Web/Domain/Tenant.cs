namespace Avallo.Web.Domain;

public sealed class Tenant
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public interface ITenantEntity
{
    Guid TenantId { get; set; }
}
