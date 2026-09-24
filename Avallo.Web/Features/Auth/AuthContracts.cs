using System.ComponentModel.DataAnnotations;

namespace Avallo.Web.Features.Auth;

public sealed record RegisterRequest(
    [property: Required, EmailAddress] string Email,
    [property: Required, MinLength(12), MaxLength(128)] string Password,
    [property: Required, MaxLength(160)] string DisplayName,
    [property: Required, MaxLength(160)] string TenantName);

public sealed record LoginRequest(
    [property: Required, EmailAddress] string Email,
    [property: Required] string Password);

public sealed record CreateTenantUserRequest(
    [property: Required, EmailAddress] string Email,
    [property: Required, MinLength(12), MaxLength(128)] string TemporaryPassword,
    [property: Required, MaxLength(160)] string DisplayName,
    [property: Required] string Role);

public sealed record UpdateProfileRequest(
    [property: Required, MinLength(2), MaxLength(160)] string DisplayName);

public sealed record ChangePasswordRequest(
    [property: Required] string CurrentPassword,
    [property: Required, MinLength(12), MaxLength(128)] string NewPassword);

public sealed record UpdateTenantUserStatusRequest(bool IsActive);

public sealed record TokenResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    UserResponse User);

public sealed record UserResponse(
    Guid Id,
    Guid TenantId,
    string Email,
    string DisplayName,
    IReadOnlyCollection<string> Roles,
    bool RequiresPasswordChange,
    string? ProfilePhotoUrl = null);

public sealed record TenantUserResponse(Guid Id, string Email, string DisplayName,
    string Role, bool IsActive, bool RequiresPasswordChange, DateTimeOffset CreatedAt);
