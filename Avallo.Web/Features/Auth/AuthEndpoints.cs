using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Avallo.Web.Domain;
using Avallo.Web.Features.Expenses;
using Avallo.Web.Infrastructure;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;

namespace Avallo.Web.Features.Auth;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var auth = endpoints.MapGroup("/api/auth").WithTags("Authentication");
        auth.MapPost("/register", RegisterAsync)
            .WithName("RegisterTenant")
            .WithSummary("Cadastra um tenant e seu administrador")
            .WithDescription("Cria a empresa isolada, o primeiro usuario com papel Admin e inicia a sessao.")
            .Produces<TokenResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .AllowAnonymous()
            .RequireRateLimiting("authentication");
        auth.MapPost("/login", LoginAsync)
            .WithName("Login")
            .WithSummary("Autentica com e-mail e senha")
            .Produces<TokenResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesValidationProblem()
            .AllowAnonymous()
            .RequireRateLimiting("authentication");
        auth.MapPost("/refresh", RefreshAsync)
            .WithName("RefreshToken")
            .WithSummary("Renova a sessao")
            .WithDescription("Rotaciona o refresh token recebido pelo cookie HttpOnly e devolve um novo access token.")
            .Produces<TokenResponse>()
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .AllowAnonymous()
            .RequireRateLimiting("authentication-session");
        auth.MapPost("/logout", LogoutAsync)
            .WithName("Logout")
            .WithSummary("Encerra a sessao atual")
            .Produces(StatusCodes.Status204NoContent)
            .AllowAnonymous();
        auth.MapGet("/google", GoogleAsync)
            .WithName("GoogleLogin")
            .WithSummary("Inicia cadastro ou login pelo Google")
            .WithDescription("Redireciona para o Google. Para uma conta nova, tenantName define o nome do tenant criado.")
            .Produces(StatusCodes.Status302Found)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .AllowAnonymous()
            .RequireRateLimiting("authentication");
        auth.MapGet("/google/callback", GoogleCallbackAsync)
            .WithName("GoogleCallback")
            .WithSummary("Finaliza internamente o login Google")
            .ExcludeFromDescription()
            .AllowAnonymous()
            .RequireRateLimiting("authentication");
        auth.MapGet("/me", MeAsync)
            .WithName("CurrentUser")
            .WithSummary("Retorna o usuario autenticado")
            .Produces<UserResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .RequireAuthorization();
        auth.MapPatch("/me", UpdateProfileAsync)
            .WithName("UpdateProfile")
            .WithSummary("Atualiza o perfil do usuario autenticado")
            .Produces<UserResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .RequireAuthorization();
        auth.MapPost("/me/photo", UploadProfilePhotoAsync)
            .WithName("UploadProfilePhoto")
            .RequireAuthorization();
        auth.MapGet("/me/photo", GetProfilePhotoAsync)
            .WithName("GetProfilePhoto")
            .RequireAuthorization();
        auth.MapPost("/change-password", ChangePasswordAsync)
            .WithName("ChangePassword")
            .WithSummary("Troca a senha do usuario autenticado")
            .Produces<UserResponse>()
            .ProducesValidationProblem()
            .RequireAuthorization();

        var users = endpoints.MapGroup("/api/users").WithTags("Users")
            .RequireAuthorization(Policies.CanManageUsers);
        users.MapGet("/", ListTenantUsersAsync)
            .WithName("ListTenantUsers")
            .WithSummary("Lista os acessos do tenant")
            .Produces<TenantUserResponse[]>();
        users.MapPost("/", CreateTenantUserAsync)
            .WithName("CreateTenantUser")
            .WithSummary("Cria um Vendedor ou Contador")
            .WithDescription("O usuario e criado exclusivamente dentro do tenant do Admin autenticado.")
            .Produces<UserResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);
        users.MapPatch("/{id:guid}/status", UpdateTenantUserStatusAsync)
            .WithName("UpdateTenantUserStatus")
            .WithSummary("Ativa ou desativa um acesso do tenant")
            .Produces<TenantUserResponse>();

        return endpoints;
    }

    private static async Task<IResult> UploadProfilePhotoAsync(
        IFormFile file,
        UserManager<ApplicationUser> userManager,
        ClaimsPrincipal principal,
        IExpenseStorage storage,
        CancellationToken cancellationToken)
    {
        if (file.Length == 0 || file.Length > 2 * 1024 * 1024)
            return Results.BadRequest(new { message = "A foto deve ter entre 1 byte e 2 MB." });
        var contentType = file.ContentType.ToLowerInvariant() switch
        {
            "image/jpeg" or "image/jpg" => "image/jpeg",
            "image/png" => "image/png",
            "image/webp" => "image/webp",
            _ => null
        };
        if (contentType is null)
            return Results.BadRequest(new { message = "Envie uma imagem JPEG, PNG ou WebP." });
        var userId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null) return Results.NotFound();
        var key = $"profile-photos/{user.TenantId:D}/{user.Id:D}";
        await using var stream = file.OpenReadStream();
        using var image = await Image.LoadAsync(stream, cancellationToken);
        await using var encoded = new MemoryStream();
        if (contentType == "image/jpeg")
            await image.SaveAsJpegAsync(encoded, new JpegEncoder { Quality = 85 }, cancellationToken);
        else if (contentType == "image/png")
            await image.SaveAsPngAsync(encoded, new PngEncoder(), cancellationToken);
        else
            await image.SaveAsWebpAsync(encoded, new WebpEncoder { Quality = 85 }, cancellationToken);
        encoded.Position = 0;
        await storage.PutAsync(key, encoded, contentType, cancellationToken);
        user.ProfilePhotoObjectKey = key;
        user.ProfilePhotoContentType = contentType;
        await userManager.UpdateAsync(user);
        return Results.Ok(new { url = storage.CreateReadUrl(key) });
    }

    private static async Task<IResult> GetProfilePhotoAsync(
        UserManager<ApplicationUser> userManager,
        ClaimsPrincipal principal,
        IExpenseStorage storage)
    {
        var userId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var user = await userManager.FindByIdAsync(userId.ToString());
        var url = user?.ProfilePhotoObjectKey is null ? null : storage.CreateReadUrl(user.ProfilePhotoObjectKey);
        return Results.Ok(new { url });
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request,
        AppDbContext db,
        UserManager<ApplicationUser> userManager,
        TokenService tokenService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (Validate(request) is { } validationProblem)
            return validationProblem;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var tenant = new Tenant { Name = request.TenantName.Trim() };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(cancellationToken);

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            UserName = request.Email.Trim(),
            Email = request.Email.Trim(),
            DisplayName = request.DisplayName.Trim(),
            EmailConfirmed = false
        };
        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
            return IdentityProblem(result);

        result = await userManager.AddToRoleAsync(user, Roles.Admin);
        if (!result.Succeeded)
            return IdentityProblem(result);

        var issued = await tokenService.IssueAsync(user, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        SetRefreshCookie(httpContext.Response, issued.RefreshToken, issued.Response.ExpiresAt.AddDays(14));
        return Results.Created("/api/auth/me", issued.Response);
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        TokenService tokenService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (Validate(request) is { } validationProblem)
            return validationProblem;

        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null || !user.IsActive)
            return InvalidCredentials();

        var result = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!result.Succeeded)
            return InvalidCredentials();

        var issued = await tokenService.IssueAsync(user, cancellationToken);
        SetRefreshCookie(httpContext.Response, issued.RefreshToken, issued.Response.ExpiresAt.AddDays(14));
        return Results.Ok(issued.Response);
    }

    private static async Task<IResult> RefreshAsync(
        HttpContext httpContext,
        TokenService tokenService,
        CancellationToken cancellationToken)
    {
        if (!httpContext.Request.Cookies.TryGetValue(TokenService.RefreshCookieName, out var rawToken))
            return Results.NoContent();

        var issued = await tokenService.RotateAsync(rawToken, cancellationToken);
        if (issued is null)
        {
            DeleteRefreshCookie(httpContext.Response);
            return Results.NoContent();
        }

        SetRefreshCookie(httpContext.Response, issued.Value.RefreshToken, issued.Value.Response.ExpiresAt.AddDays(14));
        return Results.Ok(issued.Value.Response);
    }

    private static async Task<IResult> LogoutAsync(
        HttpContext httpContext,
        TokenService tokenService,
        CancellationToken cancellationToken)
    {
        if (httpContext.Request.Cookies.TryGetValue(TokenService.RefreshCookieName, out var rawToken))
            await tokenService.RevokeAsync(rawToken, cancellationToken);

        DeleteRefreshCookie(httpContext.Response);
        return Results.NoContent();
    }

    private static IResult GoogleAsync(string? tenantName, IConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration["Authentication:Google:ClientId"]) ||
            string.IsNullOrWhiteSpace(configuration["Authentication:Google:ClientSecret"]))
            return Results.Redirect("/login?error=" + Uri.EscapeDataString("O login com Google nao esta configurado no servidor."));

        var properties = new AuthenticationProperties { RedirectUri = "/api/auth/google/callback" };
        if (!string.IsNullOrWhiteSpace(tenantName))
            properties.Items["tenant_name"] = tenantName.Trim();

        return Results.Challenge(properties, [GoogleDefaults.AuthenticationScheme]);
    }

    private static async Task<IResult> GoogleCallbackAsync(
        HttpContext httpContext,
        AppDbContext db,
        UserManager<ApplicationUser> userManager,
        TokenService tokenService,
        CancellationToken cancellationToken)
    {
        var external = await httpContext.AuthenticateAsync(IdentityConstants.ExternalScheme);
        if (!external.Succeeded || external.Principal is null)
            return Results.Redirect("/login?error=" + Uri.EscapeDataString("Nao foi possivel autenticar com a conta Google. Tente novamente."));

        var email = external.Principal.FindFirstValue(ClaimTypes.Email);
        var providerKey = external.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var displayName = external.Principal.FindFirstValue(ClaimTypes.Name) ?? email;
        var emailVerified = external.Principal.FindFirst("email_verified")?.Value
            ?? external.Principal.FindFirst("https://schemas.openid.net/claims/email_verified")?.Value;

        if (!bool.TryParse(emailVerified, out var isEmailVerified) || !isEmailVerified)
            return Results.Redirect("/login?error=" + Uri.EscapeDataString("A conta Google precisa ter o e-mail verificado."));

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(providerKey))
            return Results.Redirect("/login?error=" + Uri.EscapeDataString("O Google nao forneceu as informacoes necessarias da conta."));

        var user = await userManager.FindByLoginAsync(GoogleDefaults.AuthenticationScheme, providerKey);

        if (user is null)
        {
            user = await userManager.FindByEmailAsync(email);
            if (user is not null)
            {
                // Auto-link Google login provider to existing email account
                var linkResult = await userManager.AddLoginAsync(
                    user, new UserLoginInfo(GoogleDefaults.AuthenticationScheme, providerKey, "Google"));
                if (!linkResult.Succeeded)
                    return Results.Redirect("/login?error=" + Uri.EscapeDataString("Nao foi possivel vincular sua conta Google ao usuario existente."));
            }
            else
            {
                string? tenantName = null;
                if (external.Properties?.Items.TryGetValue("tenant_name", out var itemVal) == true)
                    tenantName = itemVal;

                if (string.IsNullOrWhiteSpace(tenantName))
                {
                    var firstFirstName = (displayName ?? email).Trim().Split(' ')[0];
                    tenantName = $"Workspace de {firstFirstName}";
                }

                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                var tenant = new Tenant { Name = tenantName };
                db.Tenants.Add(tenant);
                await db.SaveChangesAsync(cancellationToken);

                user = new ApplicationUser
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenant.Id,
                    UserName = email,
                    Email = email,
                    EmailConfirmed = true,
                    DisplayName = displayName
                };

                var created = await userManager.CreateAsync(user);
                if (!created.Succeeded)
                    return Results.Redirect("/login?error=" + Uri.EscapeDataString("Erro ao criar conta de usuario."));

                var roleResult = await userManager.AddToRoleAsync(user, Roles.Admin);
                if (!roleResult.Succeeded)
                    return Results.Redirect("/login?error=" + Uri.EscapeDataString("Erro ao atribuir papel administrativo."));

                var loginResult = await userManager.AddLoginAsync(
                    user, new UserLoginInfo(GoogleDefaults.AuthenticationScheme, providerKey, "Google"));
                if (!loginResult.Succeeded)
                    return Results.Redirect("/login?error=" + Uri.EscapeDataString("Erro ao vincular login Google."));

                await transaction.CommitAsync(cancellationToken);
            }
        }

        if (!user.IsActive)
            return Results.Redirect("/login?error=" + Uri.EscapeDataString("Sua conta esta inativa. Entre em contato com o suporte."));

        var issued = await tokenService.IssueAsync(user, cancellationToken);
        SetRefreshCookie(httpContext.Response, issued.RefreshToken, issued.Response.ExpiresAt.AddDays(14));
        await httpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        return Results.Redirect("/auth/callback");
    }

    private static async Task<IResult> MeAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> userManager)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null || !user.IsActive)
            return Results.Unauthorized();
        var roles = await userManager.GetRolesAsync(user);
        return Results.Ok(new UserResponse(user.Id, user.TenantId, user.Email!, user.DisplayName, roles.ToArray(),
            user.RequiresPasswordChange));
    }

    private static async Task<IResult> CreateTenantUserAsync(
        CreateTenantUserRequest request,
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        if (Validate(request) is { } validationProblem)
            return validationProblem;
        if (request.Role is not (Roles.Seller or Roles.Accountant))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["role"] = [$"Role must be '{Roles.Seller}' or '{Roles.Accountant}'."]
            });

        var owner = await userManager.GetUserAsync(principal);
        if (owner is null)
            return Results.Unauthorized();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            TenantId = owner.TenantId,
            UserName = request.Email.Trim(),
            Email = request.Email.Trim(),
            DisplayName = request.DisplayName.Trim(),
            RequiresPasswordChange = true
        };
        var result = await userManager.CreateAsync(user, request.TemporaryPassword);
        if (!result.Succeeded)
            return IdentityProblem(result);
        result = await userManager.AddToRoleAsync(user, request.Role);
        if (!result.Succeeded)
            return IdentityProblem(result);

        await transaction.CommitAsync(cancellationToken);
        return Results.Created($"/api/users/{user.Id}",
            new UserResponse(user.Id, user.TenantId, user.Email!, user.DisplayName, [request.Role],
                user.RequiresPasswordChange));
    }

    private static async Task<IResult> UpdateProfileAsync(
        UpdateProfileRequest request,
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager)
    {
        if (Validate(request) is { } validationProblem)
            return validationProblem;

        var user = await userManager.GetUserAsync(principal);
        if (user is null || !user.IsActive)
            return Results.Unauthorized();

        user.DisplayName = request.DisplayName.Trim();
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
            return IdentityProblem(result);

        var roles = await userManager.GetRolesAsync(user);
        return Results.Ok(new UserResponse(user.Id, user.TenantId, user.Email!, user.DisplayName, roles.ToArray(),
            user.RequiresPasswordChange));
    }

    private static async Task<IResult> ChangePasswordAsync(ChangePasswordRequest request,
        ClaimsPrincipal principal, UserManager<ApplicationUser> userManager)
    {
        if (Validate(request) is { } validationProblem)
            return validationProblem;
        if (request.CurrentPassword == request.NewPassword)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["newPassword"] = ["A nova senha deve ser diferente da senha atual."] });
        var user = await userManager.GetUserAsync(principal);
        if (user is null || !user.IsActive)
            return Results.Unauthorized();
        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
            return IdentityProblem(result);
        user.RequiresPasswordChange = false;
        result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
            return IdentityProblem(result);
        var roles = await userManager.GetRolesAsync(user);
        return Results.Ok(new UserResponse(user.Id, user.TenantId, user.Email!, user.DisplayName,
            roles.ToArray(), user.RequiresPasswordChange));
    }

    private static async Task<IResult> ListTenantUsersAsync(ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager, UserAccessService access,
        CancellationToken cancellationToken)
    {
        var owner = await userManager.GetUserAsync(principal);
        return owner is null
            ? Results.Unauthorized()
            : Results.Ok(await access.ListAsync(owner.TenantId, cancellationToken));
    }

    private static async Task<IResult> UpdateTenantUserStatusAsync(Guid id,
        UpdateTenantUserStatusRequest request, ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager, UserAccessService access,
        CancellationToken cancellationToken)
    {
        var owner = await userManager.GetUserAsync(principal);
        if (owner is null)
            return Results.Unauthorized();
        try
        {
            return Results.Ok(await access.SetActiveAsync(owner.TenantId, owner.Id, id,
                request.IsActive, cancellationToken));
        }
        catch (KeyNotFoundException exception) { return Results.NotFound(new { message = exception.Message }); }
        catch (InvalidOperationException exception) { return Results.Conflict(new { message = exception.Message }); }
    }

    private static IResult? Validate<T>(T value)
    {
        var results = new List<ValidationResult>();
        if (Validator.TryValidateObject(value!, new ValidationContext(value!), results, true))
            return null;
        return Results.ValidationProblem(results
            .GroupBy(x => x.MemberNames.FirstOrDefault() ?? string.Empty)
            .ToDictionary(x => x.Key, x => x.Select(y => y.ErrorMessage!).ToArray()));
    }

    private static IResult IdentityProblem(IdentityResult result) => Results.ValidationProblem(
        result.Errors.GroupBy(x => x.Code).ToDictionary(x => x.Key, x => x.Select(y => y.Description).ToArray()));

    private static IResult InvalidCredentials() => Results.Problem(
        "Invalid email or password.", statusCode: StatusCodes.Status401Unauthorized);

    private static void SetRefreshCookie(HttpResponse response, string token, DateTimeOffset expires) =>
        response.Cookies.Append(TokenService.RefreshCookieName, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            Expires = expires,
            IsEssential = true
        });

    private static void DeleteRefreshCookie(HttpResponse response) =>
        response.Cookies.Delete(TokenService.RefreshCookieName, new CookieOptions
        {
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/"
        });
}
