using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;

using Microsoft.Extensions.Options;

using Avallo.Connectors.Abstractions;

namespace Avallo.Connector.Amazon;

public sealed class AmazonConnector(HttpClient httpClient, IOptions<AmazonOptions> options)
    : IMarketplaceConnector, IOAuthMarketplaceConnector
{
    private const string TokenUrl = "https://api.amazon.com/auth/o2/token";
    private const string OrdersPath = "/orders/v0/orders";
    private const string FinancialEventsPath = "/finances/v0/financialEvents";
    private const string ReportsPath = "/reports/2021-06-30/reports";

    private static readonly ConnectorPresentation Branding = new(
        Tagline: "Integre seus produtos da Amazon",
        LogoUrl: "https://upload.wikimedia.org/wikipedia/commons/a/a9/Amazon_logo.svg",
        BannerLogoUrl: "https://upload.wikimedia.org/wikipedia/commons/a/a9/Amazon_logo.svg",
        BannerHeadline: "Controle sua operação Amazon",
        BannerSubtitle: "Tenha clareza sobre vendas e resultados.",
        AccentFrom: "rgba(231, 237, 248, .88)",
        AccentTo: "rgba(169, 186, 220, .88)");

    public ConnectorDescriptor Descriptor
    {
        get
        {
            var settings = options.Value;
            var configured = !string.IsNullOrWhiteSpace(settings.ClientId) &&
                             !string.IsNullOrWhiteSpace(settings.ClientSecret) &&
                             !string.IsNullOrWhiteSpace(settings.ApplicationId) &&
                             !string.IsNullOrWhiteSpace(settings.AwsAccessKeyId) &&
                             !string.IsNullOrWhiteSpace(settings.AwsSecretAccessKey) &&
                             Uri.TryCreate(settings.RedirectUri, UriKind.Absolute, out _);
            return new ConnectorDescriptor(
                "amazon", "Amazon", "1.0.0", SupportsInvoices: false,
                CredentialFields: null, UsesOAuth: true,
                Presentation: Branding,
                IsConfigured: configured,
                ConfigurationHint: configured
                    ? null
                    : "Configure Connectors:Amazon:ClientId, ClientSecret, ApplicationId, RedirectUri e as chaves AWS.");
        }
    }

    public Task<OAuthAuthorization> BeginAuthenticationAsync(OAuthStartRequest request, CancellationToken cancellationToken = default)
    {
        var settings = Settings;
        var query = new Dictionary<string, string>
        {
            ["application_id"] = settings.ApplicationId,
            ["state"] = request.State,
            ["version"] = "beta"
        };
        var baseUrl = settings.AuthorizationBaseUrl.TrimEnd('/') + "/apps/authorize/consent";
        return Task.FromResult(new OAuthAuthorization(new Uri(baseUrl + "?" + Query(query))));
    }

    public async Task<ConnectorAuthentication> AuthenticateAsync(AuthenticationRequest request, CancellationToken cancellationToken = default)
    {
        if (!request.Credentials.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
            throw Error("authorization_code_required", "Amazon authorization code is required.");
        var token = await RequestTokenAsync("authorization_code", code, cancellationToken);
        var seller = await GetSellerAsync(token.AccessToken, cancellationToken);
        return new ConnectorAuthentication(token.AccessToken, token.RefreshToken,
            DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn), seller.Id, seller.Name);
    }

    public async Task<ConnectorAuthentication> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default)
    {
        var token = await RequestTokenAsync("refresh_token", request.RefreshToken, cancellationToken);
        return new ConnectorAuthentication(token.AccessToken, token.RefreshToken ?? request.RefreshToken,
            DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn), request.ExternalAccountId ?? "amazon-seller");
    }

    public async Task<ConnectorPage<StandardOrder>> GetOrdersAsync(ConnectorContext context, OrderFilter filter, CancellationToken cancellationToken = default)
    {
        var from = filter.From ?? DateTimeOffset.UtcNow.AddDays(-30);
        var query = new Dictionary<string, string> { ["MarketplaceIds"] = Settings.MarketplaceId, ["CreatedAfter"] = from.UtcDateTime.ToString("O") };
        if (filter.To is not null) query["CreatedBefore"] = filter.To.Value.UtcDateTime.ToString("O");
        if (!string.IsNullOrWhiteSpace(filter.Cursor)) query["NextToken"] = filter.Cursor;
        var json = await SendAsync(HttpMethod.Get, OrdersPath, context.AccessToken, query, cancellationToken);
        var orders = Array(json, "payload", "Orders").Select(MapOrder).Where(x => filter.Status is null || x.Status == filter.Status).ToArray();
        return new ConnectorPage<StandardOrder>(orders, String(json, "payload", "NextToken"), !string.IsNullOrWhiteSpace(String(json, "payload", "NextToken")));
    }

    public async Task<StandardOrder> GetOrderDetailAsync(ConnectorContext context, string orderId, CancellationToken cancellationToken = default)
    {
        var json = await SendAsync(HttpMethod.Get, $"{OrdersPath}/{Uri.EscapeDataString(orderId)}", context.AccessToken,
            new Dictionary<string, string>(), cancellationToken);
        return MapOrder(Property(json, "payload") ?? json);
    }

    public async Task<IReadOnlyCollection<StandardPayment>> GetPaymentsAsync(ConnectorContext context, string orderId, CancellationToken cancellationToken = default)
    {
        var json = await SendAsync(HttpMethod.Get, FinancialEventsPath, context.AccessToken,
            new Dictionary<string, string> { ["PostedAfter"] = DateTimeOffset.UtcNow.AddDays(-90).ToString("O") }, cancellationToken);
        return Array(json, "payload", "FinancialEvents", "ShipmentEventList").Where(x => String(x, "AmazonOrderId") == orderId)
            .Select(x =>
            {
                var gross = Decimal(x, "ItemChargeList", "ChargeAmount", "CurrencyAmount");
                var platformFee = FeesOfType(x, "Commission", "ReferralFee", "VariableClosingFee");
                var shippingCost = FeesOfType(x, "ShippingChargeback", "ShippingHB", "FBAPerUnitFulfillmentFee");
                var total = Fees(x);
                var paymentFee = Math.Max(0, total - platformFee - shippingCost);
                return new StandardPayment(orderId, orderId, gross, gross - total, "Amazon",
                    StandardPaymentStatus.Paid, Date(x, "PostedDate"), null, paymentFee, "BRL",
                    platformFee, shippingCost);
            }).ToArray();
    }

    public async Task<IReadOnlyCollection<StandardFee>> GetFeesAsync(ConnectorContext context, string orderId, CancellationToken cancellationToken = default)
    {
        var json = await SendAsync(HttpMethod.Get, FinancialEventsPath, context.AccessToken,
            new Dictionary<string, string> { ["PostedAfter"] = DateTimeOffset.UtcNow.AddDays(-90).ToString("O") }, cancellationToken);
        return Array(json, "payload", "FinancialEvents", "ShipmentEventList").Where(x => String(x, "AmazonOrderId") == orderId)
            .SelectMany(x => Array(x, "ItemFeeList").Select(f => new StandardFee(String(f, "FeeType") ?? "AmazonFee",
                String(f, "FeeAmount", "CurrencyAmount") ?? "Taxa Amazon", Math.Abs(Decimal(f, "FeeAmount", "CurrencyAmount")),
                String(f, "FeeAmount", "CurrencyCode") ?? "BRL", StandardFeeCategory.MarketplaceCommission))).ToArray();
    }

    public Task<ConnectorPage<StandardInvoice>> GetInvoicesAsync(ConnectorContext context, InvoiceFilter filter, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ConnectorPage<StandardInvoice>([], null, false));

    public async IAsyncEnumerable<StandardOrder> SyncAllAsync(ConnectorContext context, DateTimeOffset since,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string? cursor = null;
        do
        {
            var page = await GetOrdersAsync(context, new OrderFilter(since, null, null, cursor, 100), cancellationToken);
            foreach (var order in page.Items) yield return order;
            cursor = page.NextCursor;
            if (!page.HasMore) yield break;
        } while (!string.IsNullOrWhiteSpace(cursor));
    }

    public async Task<ConnectorStatus> GetStatusAsync(ConnectorContext context, CancellationToken cancellationToken = default)
    {
        try { await GetSellerAsync(context.AccessToken, cancellationToken); return new ConnectorStatus(ConnectorConnectionState.Active, "Amazon SP-API ativa", DateTimeOffset.UtcNow); }
        catch (ConnectorException ex) when (ex.Code is "unauthorized" or "access_denied") { return new ConnectorStatus(ConnectorConnectionState.Expired, ex.Message, DateTimeOffset.UtcNow); }
    }

    private async Task<TokenResponse> RequestTokenAsync(string grantType, string value, CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = grantType,
            [grantType == "authorization_code" ? "code" : "refresh_token"] = value,
            ["client_id"] = Settings.ClientId,
            ["client_secret"] = Settings.ClientSecret
        };
        using var response = await httpClient.PostAsync(TokenUrl, new FormUrlEncodedContent(form), cancellationToken);
        var json = await ReadAsync(response, cancellationToken);
        if (!response.IsSuccessStatusCode) throw Error("oauth_failed", $"Amazon OAuth error: {String(json, "error_description") ?? response.ReasonPhrase}");
        return new TokenResponse(String(json, "access_token") ?? throw Error("invalid_token_response", "Amazon did not return an access token."),
            String(json, "refresh_token"), Integer(json, "expires_in", 3600));
    }

    private async Task<(string Id, string Name)> GetSellerAsync(string accessToken, CancellationToken cancellationToken)
    {
        var json = await SendAsync(HttpMethod.Get, "/sellers/v1/marketplaceParticipations", accessToken, new Dictionary<string, string>(), cancellationToken);
        var participation = Array(json, "payload").FirstOrDefault(x => String(x, "marketplace", "id") == Settings.MarketplaceId);
        return (String(participation, "participation", "sellerId") ?? String(participation, "sellerId") ?? "amazon-seller",
            String(participation, "participation", "name") ?? "Amazon BR");
    }

    private async Task<JsonElement> SendAsync(HttpMethod method, string path, string accessToken, IReadOnlyDictionary<string, string> query, CancellationToken cancellationToken)
    {
        var settings = Settings; var now = DateTimeOffset.UtcNow; var uri = new Uri(settings.ApiBaseUrl.TrimEnd('/') + path + (query.Count == 0 ? "" : "?" + Query(query)));
        var headers = new Dictionary<string, string> { ["host"] = uri.Host, ["x-amz-access-token"] = accessToken, ["x-amz-date"] = now.UtcDateTime.ToString("yyyyMMddTHHmmssZ") };
        if (!string.IsNullOrWhiteSpace(settings.AwsSessionToken)) headers["x-amz-security-token"] = settings.AwsSessionToken;
        var hash = AmazonSignature.Hash(string.Empty); headers["x-amz-content-sha256"] = hash;
        using var request = new HttpRequestMessage(method, uri); foreach (var h in headers) request.Headers.TryAddWithoutValidation(h.Key, h.Value);
        request.Headers.TryAddWithoutValidation("Authorization", AmazonSignature.Sign(method.Method, uri, headers, hash, settings.AwsAccessKeyId, settings.AwsSecretAccessKey, settings.Region, "execute-api", now));
        using var response = await httpClient.SendAsync(request, cancellationToken); var json = await ReadAsync(response, cancellationToken);
        if (response.IsSuccessStatusCode) return json;
        var code = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden ? "unauthorized" : response.StatusCode == HttpStatusCode.TooManyRequests ? "rate_limit" : $"amazon_{(int)response.StatusCode}";
        throw Error(code, $"Amazon SP-API error: {String(json, "errors", "message") ?? response.ReasonPhrase}", response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500);
    }

    private static StandardOrder MapOrder(JsonElement x)
    {
        var total = Decimal(x, "OrderTotal", "Amount"); var status = String(x, "OrderStatus") ?? "Pending";
        return new StandardOrder(String(x, "AmazonOrderId") ?? throw Error("invalid_order", "Amazon order id is missing."), "amazon",
            Date(x, "PurchaseDate") ?? DateTimeOffset.UtcNow, total, 0, total, "Amazon", null, null,
            status is "Canceled" or "Cancelled" ? StandardOrderStatus.Cancelled : status is "Shipped" or "Unshipped" ? StandardOrderStatus.Paid : StandardOrderStatus.Pending,
            String(x, "BuyerInfo", "BuyerName") ?? "", [], null,
            status == "Delivered" ? StandardFulfillmentStatus.Delivered : status == "Shipped" ? StandardFulfillmentStatus.Shipped : StandardFulfillmentStatus.Unknown,
            null, String(x, "OrderTotal", "CurrencyCode") ?? "BRL");
    }

    private static decimal Fees(JsonElement x) => Array(x, "ItemFeeList").Sum(f => Math.Abs(Decimal(f, "FeeAmount", "CurrencyAmount")));
    private static decimal FeesOfType(JsonElement x, params string[] feeTypes) =>
        Array(x, "ItemFeeList")
            .Where(f => feeTypes.Contains(String(f, "FeeType") ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            .Sum(f => Math.Abs(Decimal(f, "FeeAmount", "CurrencyAmount")));
    private AmazonOptions Settings => options.Value;
    private static string Query(IReadOnlyDictionary<string, string> values) => string.Join('&', values.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));
    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken) { var text = await response.Content.ReadAsStringAsync(cancellationToken); try { return JsonDocument.Parse(text).RootElement.Clone(); } catch (JsonException ex) { throw Error("invalid_response", "Amazon returned invalid JSON.", true, ex); } }
    private static JsonElement? Property(JsonElement x, string name) => x.ValueKind == JsonValueKind.Object && x.TryGetProperty(name, out var p) ? p : null;
    private static JsonElement? Property(JsonElement x, params string[] names) { foreach (var n in names) { var p = Property(x, n); if (p is null) return null; x = p.Value; } return x; }
    private static string? String(JsonElement x, params string[] names) => Property(x, names)?.ToString();
    private static IEnumerable<JsonElement> Array(JsonElement x, params string[] names) => Property(x, names) is { ValueKind: JsonValueKind.Array } p ? p.EnumerateArray() : [];
    private static int Integer(JsonElement x, string n, int d) => Property(x, n) is { } p && p.TryGetInt32(out var v) ? v : d;
    private static decimal Decimal(JsonElement x, params string[] names) => Property(x, names) is { } p && p.TryGetDecimal(out var v) ? v : 0;
    private static DateTimeOffset? Date(JsonElement x, string n) => DateTimeOffset.TryParse(String(x, n), out var d) ? d : null;
    private static ConnectorException Error(string code, string message, bool transient = false, Exception? inner = null) => new("amazon", code, message, transient, inner);
    private sealed record TokenResponse(string AccessToken, string? RefreshToken, int ExpiresIn);
}
