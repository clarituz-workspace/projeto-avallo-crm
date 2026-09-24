using System.ComponentModel.DataAnnotations;

namespace Avallo.Client.Models;

public sealed class LoginModel
{
    [Required(ErrorMessage = "Informe seu e-mail.")]
    [EmailAddress(ErrorMessage = "Informe um e-mail valido.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe sua senha.")]
    public string Password { get; set; } = string.Empty;
}

public sealed class RegisterModel
{
    [Required(ErrorMessage = "Informe seu nome.")]
    [StringLength(160, MinimumLength = 2, ErrorMessage = "Use entre 2 e 160 caracteres.")]
    public string DisplayName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe o nome da empresa.")]
    [StringLength(160, MinimumLength = 2, ErrorMessage = "Use entre 2 e 160 caracteres.")]
    public string TenantName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe seu e-mail.")]
    [EmailAddress(ErrorMessage = "Informe um e-mail valido.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe uma senha.")]
    [MinLength(12, ErrorMessage = "A senha precisa ter pelo menos 12 caracteres.")]
    public string Password { get; set; } = string.Empty;
}

public sealed class CreateUserModel
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required, MinLength(12)]
    public string TemporaryPassword { get; set; } = string.Empty;

    [Required, StringLength(160, MinimumLength = 2)]
    public string DisplayName { get; set; } = string.Empty;

    [Required]
    public string Role { get; set; } = "Contador";
}

public sealed record UserModel(Guid Id, Guid TenantId, string Email, string DisplayName, string[] Roles,
    bool RequiresPasswordChange)
{
    public string Initials => string.Join(string.Empty, DisplayName
        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Take(2)
        .Select(x => char.ToUpperInvariant(x[0])));

    public string PrimaryRole => Roles.FirstOrDefault() ?? "Usuario";
}

public sealed record TokenModel(string AccessToken, DateTimeOffset ExpiresAt, UserModel User);
public sealed record TenantUserModel(Guid Id, string Email, string DisplayName, string Role,
    bool IsActive, bool RequiresPasswordChange, DateTimeOffset CreatedAt);

public sealed record AuthResult(bool Succeeded, string? Error = null)
{
    public static AuthResult Success() => new(true);
    public static AuthResult Failure(string error) => new(false, error);
}

public sealed record ApiResult<T>(bool Succeeded, T? Value = default, string? Error = null);
public sealed record DownloadedFile(byte[] Content, string ContentType, string FileName);
