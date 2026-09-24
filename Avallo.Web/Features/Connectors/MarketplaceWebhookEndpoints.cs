using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Avallo.Web.Domain;
using Avallo.Web.Infrastructure;

namespace Avallo.Web.Features.Connectors;

public sealed class MarketplaceWebhookOptions
{
    public const string SectionName = "MarketplaceWebhooks";
    public bool Enabled { get; init; } = true;
    public int MaximumBodyBytes { get; init; } = 256 * 1024;
    public int MaximumAgeMinutes { get; init; } = 10;
    public int SyncLookbackMinutes { get; init; } = 15;
    public Dictionary<string, string> Secrets { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public static class MarketplaceWebhookEndpoints
{
    public static IEndpointRouteBuilder MapMarketplaceWebhookEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/connectors/webhooks/{platform}/{tenantId:guid}/{connectionId:guid}", ReceiveAsync)
            .AllowAnonymous()
            .WithTags("Connectors")
            .WithName("ReceiveMarketplaceStatusWebhook")
            .WithSummary("Recebe mudancas de status assinadas e agenda sincronizacao imediata")
            .DisableAntiforgery();
        return endpoints;
    }

    private static async Task<IResult> ReceiveAsync(
        string platform,
        Guid tenantId,
        Guid connectionId,
        HttpContext httpContext,
        AppDbContext db,
        ITenantScope tenantScope,
        ConnectorGateway gateway,
        MarketplaceSyncQueue queue,
        IOptions<MarketplaceWebhookOptions> options,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var config = options.Value;
        if (!config.Enabled)
            return Results.NotFound();
        if (!config.Secrets.TryGetValue(platform, out var secret) || string.IsNullOrWhiteSpace(secret))
            return Results.Problem("Webhook receiver is not configured for this platform.", statusCode: StatusCodes.Status503ServiceUnavailable);

        byte[] body;
        try
        {
            body = await ReadBodyAsync(httpContext.Request, config.MaximumBodyBytes, cancellationToken);
        }
        catch (InvalidDataException exception)
        {
            return Results.BadRequest(new { message = exception.Message });
        }

        var timestamp = httpContext.Request.Headers["X-Webhook-Timestamp"].ToString();
        var signature = httpContext.Request.Headers["X-Webhook-Signature"].ToString();
        if (!MarketplaceWebhookSignature.IsValid(
                secret, timestamp, signature, body, timeProvider.GetUtcNow(),
                TimeSpan.FromMinutes(Math.Max(1, config.MaximumAgeMinutes))))
            return Results.Unauthorized();

        using var tenant = tenantScope.BeginScope(tenantId);
        var connection = await db.MarketplaceConnections.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == connectionId && x.Status == MarketplaceConnectionStates.Active,
            cancellationToken);
        if (connection is null || !string.Equals(connection.ConnectorName, platform, StringComparison.OrdinalIgnoreCase))
            return Results.NotFound();

        var eventId = httpContext.Request.Headers["X-Webhook-Id"].ToString();
        if (string.IsNullOrWhiteSpace(eventId))
            eventId = Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant();
        var triggerHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(eventId)))
            .ToLowerInvariant()[..32];
        var leaseId = Guid.NewGuid();
        var now = timeProvider.GetUtcNow();
        if (!await gateway.TryAcquireSyncLeaseAsync(connectionId, leaseId, now, cancellationToken))
            return Results.Accepted(value: new { accepted = true, duplicateOrInProgress = true });

        var queued = false;
        try
        {
            queued = await queue.EnqueueAsync(new MarketplaceSyncWorkItem(
                tenantId, connectionId, now.AddMinutes(-Math.Max(1, config.SyncLookbackMinutes)),
                leaseId, $"webhook:{connectionId:N}:{triggerHash}"), cancellationToken);
            if (!queued)
                return Results.Problem("The webhook was authenticated, but the sync queue is unavailable.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            loggerFactory.CreateLogger("MarketplaceWebhooks").LogInformation(
                "Webhook {EventId} from {Platform} queued sync for connection {ConnectionId}.",
                eventId, platform, connectionId);
            return Results.Accepted(value: new { accepted = true });
        }
        finally
        {
            if (!queued)
                await gateway.ReleaseSyncLeaseAsync(connectionId, leaseId, CancellationToken.None);
        }
    }

    private static async Task<byte[]> ReadBodyAsync(
        HttpRequest request, int maximumBodyBytes, CancellationToken cancellationToken)
    {
        var maximum = Math.Clamp(maximumBodyBytes, 1, 1024 * 1024);
        if (request.ContentLength > maximum)
            throw new InvalidDataException("Webhook payload is too large.");
        await using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maximum)
                throw new InvalidDataException("Webhook payload is too large.");
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }
        return buffer.ToArray();
    }
}

public static class MarketplaceWebhookSignature
{
    public static bool IsValid(
        string secret,
        string timestamp,
        string signature,
        ReadOnlySpan<byte> body,
        DateTimeOffset now,
        TimeSpan maximumAge)
    {
        if (!long.TryParse(timestamp, out var unixSeconds) || string.IsNullOrWhiteSpace(signature))
            return false;
        DateTimeOffset sentAt;
        try { sentAt = DateTimeOffset.FromUnixTimeSeconds(unixSeconds); }
        catch (ArgumentOutOfRangeException) { return false; }
        if ((now - sentAt).Duration() > maximumAge)
            return false;

        var prefix = Encoding.UTF8.GetBytes(timestamp + ".");
        var signed = new byte[prefix.Length + body.Length];
        prefix.CopyTo(signed, 0);
        body.CopyTo(signed.AsSpan(prefix.Length));
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), signed);
        var suppliedValue = signature.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase)
            ? signature[7..]
            : signature;
        byte[] supplied;
        try { supplied = Convert.FromHexString(suppliedValue); }
        catch (FormatException) { return false; }
        if (supplied.Length != expected.Length)
            return false;
        return CryptographicOperations.FixedTimeEquals(expected, supplied);
    }
}
