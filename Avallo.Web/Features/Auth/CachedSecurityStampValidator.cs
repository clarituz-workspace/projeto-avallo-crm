using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Distributed;
using Avallo.Web.Domain;

namespace Avallo.Web.Features.Auth;

/// <summary>
/// Valida a claim "security_stamp" do JWT contra o estado atual do usuario
/// (IsActive + SecurityStamp) sem um roundtrip ao Postgres por request.
///
/// O snapshot fica no IDistributedCache (Redis em producao, memoria em dev) por
/// <see cref="CacheTtl"/>. Consequencia: a revogacao de um token — desativacao do
/// usuario ou rotacao do stamp — pode levar ate esse TTL para ser observada pelas
/// requests autenticadas. E uma janela pequena diante do lifetime do access token
/// (10 min) e e o custo aceito para tirar a query do caminho quente.
///
/// Para nao rejeitar por dado obsoleto logo apos uma mudanca legitima (ex.: rotacao
/// de stamp no refresh token), uma entrada de cache que REJEITA o token dispara uma
/// unica verificacao fresca no banco antes da falha — entradas que rejeitam nunca sao
/// confiaveis, apenas evitam trabalho quando aceitas.
/// </summary>
public sealed class CachedSecurityStampValidator(
    IDistributedCache cache,
    UserManager<ApplicationUser> userManager,
    ILogger<CachedSecurityStampValidator> logger)
{
    /// <summary>
    /// TTL do snapshot cacheado. 30s equilibra revogacao quase imediata com a
    /// eliminacao da query por request autenticada.
    /// </summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    private const string CacheKeyPrefix = "auth:secstamp:";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Retorna true quando o usuario existe, esta ativo e o stamp do token confere.
    /// Mesma semantica de: <c>user is not null &amp;&amp; user.IsActive &amp;&amp; user.SecurityStamp == stamp</c>.
    /// </summary>
    public async Task<bool> IsValidAsync(string? userId, string? stamp, CancellationToken cancellationToken = default)
    {
        if (userId is null)
            return false;

        var key = CacheKeyPrefix + userId;
        var cached = await TryReadAsync(key, cancellationToken);
        if (cached is not null)
        {
            if (Accepts(cached, stamp))
                return true;

            // O cache diz "rejeitar", mas pode estar defasado. Uma unica leitura fresca
            // decide de verdade e ja atualiza a entrada para as proximas requests.
            var fresh = await LoadAsync(userId);
            await WriteAsync(key, fresh, cancellationToken);
            return fresh is not null && Accepts(fresh, stamp);
        }

        var snapshot = await LoadAsync(userId);
        await WriteAsync(key, snapshot, cancellationToken);
        return snapshot is not null && Accepts(snapshot, stamp);
    }

    private static bool Accepts(CachedSnapshot snapshot, string? stamp) =>
        snapshot.IsActive && snapshot.SecurityStamp == stamp;

    private async Task<CachedSnapshot?> LoadAsync(string userId)
    {
        var user = await userManager.FindByIdAsync(userId);
        return user is null ? null : new CachedSnapshot(user.IsActive, user.SecurityStamp);
    }

    private async Task<CachedSnapshot?> TryReadAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            var json = await cache.GetStringAsync(key, cancellationToken);
            return json is null ? null : JsonSerializer.Deserialize<CachedSnapshot>(json, JsonOptions);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Cache indisponivel nao pode derrubar a autenticacao: cai para o banco.
            logger.LogWarning(exception, "Falha ao ler {CacheKey} do cache distribuido; validando no banco.", key);
            return null;
        }
    }

    private async Task WriteAsync(string key, CachedSnapshot? snapshot, CancellationToken cancellationToken)
    {
        try
        {
            if (snapshot is null)
            {
                await cache.RemoveAsync(key, cancellationToken);
                return;
            }

            await cache.SetStringAsync(
                key,
                JsonSerializer.Serialize(snapshot, JsonOptions),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl },
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Falha ao gravar {CacheKey} no cache distribuido.", key);
        }
    }

    private sealed record CachedSnapshot(bool IsActive, string? SecurityStamp);
}
