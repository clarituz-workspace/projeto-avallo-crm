using Microsoft.AspNetCore.Identity;

namespace Avallo.Web.Domain;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public Guid TenantId { get; set; }
    public required string DisplayName { get; set; }
    public string? ProfilePhotoObjectKey { get; set; }
    public string? ProfilePhotoContentType { get; set; }
    public bool IsActive { get; set; } = true;
    public bool RequiresPasswordChange { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
