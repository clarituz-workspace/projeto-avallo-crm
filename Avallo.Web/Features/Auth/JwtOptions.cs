using System.ComponentModel.DataAnnotations;

namespace Avallo.Web.Features.Auth;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required, MinLength(32)]
    public required string Key { get; init; }

    [Required]
    public required string Issuer { get; init; }

    [Required]
    public required string Audience { get; init; }

    [Range(5, 30)]
    public int AccessTokenMinutes { get; init; } = 10;

    [Range(1, 90)]
    public int RefreshTokenDays { get; init; } = 14;
}
