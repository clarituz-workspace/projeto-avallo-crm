using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Avallo.Connectors.Abstractions;
using Microsoft.Extensions.Options;

namespace Avallo.Connector.Shopee;

public sealed class ShopeeConnector(
    HttpClient httpClient,
    IOptions<ShopeeOptions> options,
    ShopeeRateLimiter rateLimiter) : IMarketplaceConnector, IOAuthMarketplaceConnector
{
    private const string AuthPath = "/api/v2/shop/auth_partner";
    private const string TokenPath = "/api/v2/auth/token/get";
    private const string RefreshPath = "/api/v2/auth/access_token/get";
    private const string OrdersPath = "/api/v2/order/get_order_list";
    private const string OrderDetailPath = "/api/v2/order/get_order_detail";
    private const string PaymentListPath = "/api/v2/payment/get_payment_list";
    private const string ShopInfoPath = "/api/v2/shop/get_shop_info";

    private static readonly ConnectorPresentation Branding = new(
        Tagline: "Conecte sua conta Shopee",
        LogoUrl: "https://upload.wikimedia.org/wikipedia/commons/f/fe/Shopee.svg",
        BannerLogoUrl: "https://upload.wikimedia.org/wikipedia/commons/f/fe/Shopee.svg",
        BannerHeadline: "Sua operação Shopee mais simples",
        BannerSubtitle: "Centralize vendas, taxas e sincronizações.",
        AccentFrom: "rgba(255, 227, 218, .88)",
        AccentTo: "rgba(255, 155, 127, .88)",
        SoundUrl: "https://www.myinstants.com/media/sounds/shopee-ringtone.mp3");

    public ConnectorDescriptor Descriptor
    {
        get
        {
            var settings = options.Value;
            var configured = settings.PartnerId > 0 &&
                             !string.IsNullOrWhiteSpace(settings.PartnerKey) &&
                             Uri.TryCreate(settings.RedirectUri, UriKind.Absolute, out _) &&
                             Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out _);
            return new ConnectorDescriptor(
                "shopee", "Shopee", "2.0.0", SupportsInvoices: false,
                CredentialFields: null, UsesOAuth: true,
                Presentation: Branding,
                IsConfigured: configured,
                ConfigurationHint: configured
                    ? null
                    : "Aguardando credenciais da Shopee Open Platform (Connectors:Shopee:PartnerId, PartnerKey, RedirectUri e BaseUrl).");
        }
    }

    public Task<OAuthAuthorization> BeginAuthenticationAsync(OAuthStartRequest request, CancellationToken cancellationToken = default)
    {
        var settings = Settings;
        var timestamp = Timestamp();
        var sign = ShopeeSignature.CreatePublic(settings.PartnerId, AuthPath, timestamp, settings.PartnerKey);
        var redirectBase = string.IsNullOrWhiteSpace(settings.RedirectUri) ? request.CallbackUrl : settings.RedirectUri;
        var redirect = $"{redirectBase}{(redirectBase.Contains('?') ? '&' : '?')}state={Uri.EscapeDataString(request.State)}";
        var uri = BuildUri(AuthPath, new Dictionary<string, string>
        {
            ["partner_id"] = settings.PartnerId.ToString(CultureInfo.InvariantCulture),
            ["timestamp"] = timestamp.ToString(CultureInfo.InvariantCulture),
            ["sign"] = sign,
            ["redirect"] = redirect
        });
        return Task.FromResult(new OAuthAuthorization(uri));
    }

    public async Task<ConnectorAuthentication> AuthenticateAsync(AuthenticationRequest request, CancellationToken cancellationToken = default)
    {
        if (!request.Credentials.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
            throw Error("authorization_code_required", "Shopee authorization code is required.");
        if (!request.Credentials.TryGetValue("shop_id", out var rawShopId) || !long.TryParse(rawShopId, out var shopId) || shopId <= 0)
            throw Error("shop_id_required", "Shopee shop_id is required.");

        var token = await RequestTokenAsync(TokenPath, new { code, shop_id = shopId, partner_id = Settings.PartnerId }, cancellationToken);
        var shop = await GetAsync(ShopInfoPath, token.AccessToken, shopId, null, cancellationToken);
        return new ConnectorAuthentication(token.AccessToken, token.RefreshToken,
            DateTimeOffset.UtcNow.AddSeconds(token.ExpireIn), shopId.ToString(CultureInfo.InvariantCulture),
            String(Response(shop), "shop_name") ?? $"Shopee {shopId}");
    }

    public async Task<ConnectorAuthentication> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default)
    {
        if (!long.TryParse(request.ExternalAccountId, out var shopId) || shopId <= 0)
            throw Error("shop_id_required", "Shopee shop_id is required to refresh the access token.");
        var token = await RequestTokenAsync(RefreshPath,
            new { refresh_token = request.RefreshToken, shop_id = shopId, partner_id = Settings.PartnerId }, cancellationToken);
        return new ConnectorAuthentication(token.AccessToken, token.RefreshToken,
            DateTimeOffset.UtcNow.AddSeconds(token.ExpireIn), shopId.ToString(CultureInfo.InvariantCulture));
    }

    public async Task<ConnectorPage<StandardOrder>> GetOrdersAsync(ConnectorContext context, OrderFilter filter, CancellationToken cancellationToken = default)
    {
        var shopId = ShopId(context);
        var from = (filter.From ?? DateTimeOffset.UtcNow.AddDays(-15)).ToUnixTimeSeconds();
        var to = (filter.To ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
        var parameters = new Dictionary<string, string>
        {
            ["time_range_field"] = "create_time",
            ["time_from"] = from.ToString(CultureInfo.InvariantCulture),
            ["time_to"] = to.ToString(CultureInfo.InvariantCulture),
            ["page_size"] = Math.Clamp(filter.PageSize, 1, 100).ToString(CultureInfo.InvariantCulture),
            ["response_optional_fields"] = "order_status"
        };
        if (!string.IsNullOrWhiteSpace(filter.Cursor)) parameters["cursor"] = filter.Cursor;
        var list = Response(await GetAsync(OrdersPath, context.AccessToken, shopId, parameters, cancellationToken));
        var summaries = Array(list, "order_list").ToArray();
        if (summaries.Length == 0)
            return new ConnectorPage<StandardOrder>([], String(list, "next_cursor"), Boolean(list, "more"));
        var ids = summaries.Select(x => String(x, "order_sn")).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        var detail = Response(await GetAsync(OrderDetailPath, context.AccessToken, shopId,
            new Dictionary<string, string>
            {
                ["order_sn_list"] = string.Join(',', ids!),
                ["response_optional_fields"] = "buyer_user_id,buyer_username,estimated_shipping_fee,actual_shipping_fee,recipient_address,item_list,pay_time,total_amount,currency,order_status,shipping_carrier,create_time,update_time"
            }, cancellationToken));
        var orders = Array(detail, "order_list").Select(MapOrder)
            .Where(x => filter.Status is null || x.Status == filter.Status).ToArray();
        return new ConnectorPage<StandardOrder>(orders, String(list, "next_cursor"), Boolean(list, "more"));
    }

    public async Task<StandardOrder> GetOrderDetailAsync(ConnectorContext context, string orderId, CancellationToken cancellationToken = default)
    {
        var response = Response(await GetAsync(OrderDetailPath, context.AccessToken, ShopId(context),
            new Dictionary<string, string>
            {
                ["order_sn_list"] = orderId,
                ["response_optional_fields"] = "buyer_user_id,buyer_username,estimated_shipping_fee,actual_shipping_fee,recipient_address,item_list,pay_time,total_amount,currency,order_status,shipping_carrier,create_time,update_time"
            }, cancellationToken));
        return Array(response, "order_list").Select(MapOrder).FirstOrDefault()
               ?? throw Error("order_not_found", $"Shopee order {orderId} was not found.");
    }

    public async Task<IReadOnlyCollection<StandardPayment>> GetPaymentsAsync(ConnectorContext context, string orderId, CancellationToken cancellationToken = default)
    {
        var response = Response(await GetAsync(PaymentListPath, context.AccessToken, ShopId(context),
            new Dictionary<string, string> { ["order_sn"] = orderId, ["page_size"] = "100" }, cancellationToken));
        return Array(response, "payment_list").Select(x => MapPayment(x, orderId)).ToArray();
    }

    public async Task<IReadOnlyCollection<StandardFee>> GetFeesAsync(ConnectorContext context, string orderId, CancellationToken cancellationToken = default)
    {
        var response = Response(await GetAsync(PaymentListPath, context.AccessToken, ShopId(context),
            new Dictionary<string, string> { ["order_sn"] = orderId, ["page_size"] = "100" }, cancellationToken));
        var fees = new List<StandardFee>();
        foreach (var payment in Array(response, "payment_list"))
        {
            AddFee(fees, payment, "commission_fee", "Comissao Shopee", StandardFeeCategory.MarketplaceCommission);
            AddFee(fees, payment, "service_fee", "Taxa de servico Shopee", StandardFeeCategory.MarketplaceCommission);
            AddFee(fees, payment, "transaction_fee", "Taxa de pagamento Shopee", StandardFeeCategory.PaymentProcessing);
            AddFee(fees, payment, "seller_shipping_fee", "Frete do vendedor", StandardFeeCategory.SellerShipping);
        }
        return fees;
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
            if (!page.HasMore) break;
        } while (!string.IsNullOrWhiteSpace(cursor));
    }

    public async Task<ConnectorStatus> GetStatusAsync(ConnectorContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var shop = Response(await GetAsync(ShopInfoPath, context.AccessToken, ShopId(context), null, cancellationToken));
            return new ConnectorStatus(ConnectorConnectionState.Active, String(shop, "shop_name"), DateTimeOffset.UtcNow);
        }
        catch (ConnectorException exception) when (exception.Code is "unauthorized" or "invalid_access_token")
        {
            return new ConnectorStatus(ConnectorConnectionState.Expired, exception.Message, DateTimeOffset.UtcNow);
        }
    }

    private async Task<TokenResponse> RequestTokenAsync(string path, object body, CancellationToken cancellationToken)
    {
        var timestamp = Timestamp();
        var sign = ShopeeSignature.CreatePublic(Settings.PartnerId, path, timestamp, Settings.PartnerKey);
        var uri = BuildUri(path, CommonQuery(timestamp, sign));
        using var response = await httpClient.PostAsJsonAsync(uri, body, cancellationToken);
        var json = await ReadAsync(response, cancellationToken);
        EnsureSuccess(response, json);
        return new TokenResponse(
            String(json, "access_token") ?? throw Error("invalid_token_response", "Shopee did not return an access token."),
            String(json, "refresh_token") ?? throw Error("invalid_token_response", "Shopee did not return a refresh token."),
            Integer(json, "expire_in", 14400));
    }

    private async Task<JsonElement> GetAsync(string path, string accessToken, long shopId,
        IReadOnlyDictionary<string, string>? parameters, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await rateLimiter.AcquireAsync(shopId, cancellationToken);
            var timestamp = Timestamp();
            var sign = ShopeeSignature.CreateShop(Settings.PartnerId, path, timestamp, accessToken, shopId, Settings.PartnerKey);
            var query = CommonQuery(timestamp, sign);
            query["access_token"] = accessToken;
            query["shop_id"] = shopId.ToString(CultureInfo.InvariantCulture);
            if (parameters is not null) foreach (var item in parameters) query[item.Key] = item.Value;
            using var response = await httpClient.GetAsync(BuildUri(path, query), cancellationToken);
            var json = await ReadAsync(response, cancellationToken);
            if ((response.StatusCode == HttpStatusCode.TooManyRequests || ErrorCode(json) == "error_too_many_request") && attempt < 3)
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2), cancellationToken);
                continue;
            }
            EnsureSuccess(response, json);
            return json;
        }
        throw Error("rate_limit", "Shopee rate limit was exceeded.", true);
    }

    private Dictionary<string, string> CommonQuery(long timestamp, string sign) => new()
    {
        ["partner_id"] = Settings.PartnerId.ToString(CultureInfo.InvariantCulture),
        ["timestamp"] = timestamp.ToString(CultureInfo.InvariantCulture),
        ["sign"] = sign
    };

    private Uri BuildUri(string path, IReadOnlyDictionary<string, string> query)
    {
        var builder = new UriBuilder(new Uri(new Uri(Settings.BaseUrl.TrimEnd('/') + "/"), path.TrimStart('/')));
        builder.Query = string.Join('&', query.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));
        return builder.Uri;
    }

    private static StandardOrder MapOrder(JsonElement order)
    {
        var gross = Decimal(order, "total_amount");
        var fee = Math.Max(0, Decimal(order, "estimated_shipping_fee"));
        var statusText = String(order, "order_status") ?? "UNPAID";
        var status = statusText is "CANCELLED" or "IN_CANCEL" ? StandardOrderStatus.Cancelled
            : Long(order, "pay_time") > 0 ? StandardOrderStatus.Paid : StandardOrderStatus.Pending;
        var fulfillment = statusText switch
        {
            "COMPLETED" => StandardFulfillmentStatus.Delivered,
            "SHIPPED" or "TO_CONFIRM_RECEIVE" => StandardFulfillmentStatus.Shipped,
            "READY_TO_SHIP" or "PROCESSED" => StandardFulfillmentStatus.Pending,
            "CANCELLED" => StandardFulfillmentStatus.Returned,
            _ => StandardFulfillmentStatus.Unknown
        };
        var items = Array(order, "item_list").Select(x => new StandardOrderItem(
            String(x, "model_sku") ?? String(x, "item_sku"), String(x, "item_name") ?? "Produto",
            Integer(x, "model_quantity_purchased", 1), Decimal(x, "model_discounted_price", Decimal(x, "model_original_price")))).ToArray();
        return new StandardOrder(String(order, "order_sn") ?? throw Error("invalid_order", "Shopee order_sn is missing."),
            "shopee", FromUnix(Long(order, "create_time")) ?? DateTimeOffset.UtcNow, gross, fee, Math.Max(0, gross - fee),
            "Shopee", FromUnix(Long(order, "pay_time")), null, status, String(order, "buyer_username") ?? "", items,
            null, fulfillment, statusText == "COMPLETED" ? FromUnix(Long(order, "update_time")) : null,
            String(order, "currency") ?? "BRL");
    }

    private static StandardPayment MapPayment(JsonElement payment, string orderId)
    {
        var gross = Decimal(payment, "buyer_total_amount", Decimal(payment, "order_income"));
        var net = Decimal(payment, "escrow_amount", Decimal(payment, "seller_income"));
        // A Shopee ja desmembra o split no payment_list; o que sobrar de diferenca entre bruto
        // e liquido depois das taxas conhecidas fica como taxa de pagamento.
        var platformFee = Math.Abs(Decimal(payment, "commission_fee")) + Math.Abs(Decimal(payment, "service_fee"));
        var shippingCost = Math.Abs(Decimal(payment, "seller_shipping_fee"));
        var transactionFee = Math.Abs(Decimal(payment, "transaction_fee"));
        var fee = transactionFee > 0
            ? transactionFee
            : Math.Max(0, gross - net - platformFee - shippingCost);
        return new StandardPayment(String(payment, "payment_id") ?? String(payment, "order_sn") ?? orderId,
            orderId, gross, net, String(payment, "payment_method") ?? "Shopee",
            net > 0 ? StandardPaymentStatus.Paid : StandardPaymentStatus.Pending,
            FromUnix(Long(payment, "payment_time")), FromUnix(Long(payment, "release_time")), fee,
            String(payment, "currency") ?? "BRL", platformFee, shippingCost);
    }

    private static void AddFee(List<StandardFee> fees, JsonElement payment, string field, string description, StandardFeeCategory category)
    {
        var amount = Math.Abs(Decimal(payment, field));
        if (amount > 0) fees.Add(new StandardFee(field, description, amount, Category: category, ExternalId: field));
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        try { return JsonDocument.Parse(content).RootElement.Clone(); }
        catch (JsonException exception) { throw Error("invalid_response", "Shopee returned invalid JSON.", (int)response.StatusCode >= 500, exception); }
    }

    private static void EnsureSuccess(HttpResponseMessage response, JsonElement json)
    {
        var apiError = ErrorCode(json);
        if (response.IsSuccessStatusCode && string.IsNullOrWhiteSpace(apiError)) return;
        var code = apiError ?? response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "unauthorized",
            HttpStatusCode.TooManyRequests => "rate_limit",
            _ => $"shopee_{(int)response.StatusCode}"
        };
        throw Error(code, $"Shopee API error: {String(json, "message") ?? String(json, "msg") ?? response.ReasonPhrase}",
            response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500);
    }

    private long Timestamp() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    private long ShopId(ConnectorContext context) => long.TryParse(context.ExternalAccountId, out var value) && value > 0
        ? value : throw Error("invalid_shop_id", "The connected Shopee shop id is invalid.");
    private ShopeeOptions Settings => options.Value.PartnerId > 0 && !string.IsNullOrWhiteSpace(options.Value.PartnerKey) &&
                                      Uri.TryCreate(options.Value.RedirectUri, UriKind.Absolute, out _) &&
                                      Uri.TryCreate(options.Value.BaseUrl, UriKind.Absolute, out _)
        ? options.Value : throw Error("connector_not_configured", "Shopee partner credentials, redirect URI and base URL are not configured.");
    private static ConnectorException Error(string code, string message, bool transient = false, Exception? inner = null) =>
        new("shopee", code, message, transient, inner);
    private static JsonElement Response(JsonElement json) => Property(json, "response") ?? json;
    private static string? ErrorCode(JsonElement json) => String(json, "error");
    private static JsonElement? Property(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) && value.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined ? value : null;
    private static IEnumerable<JsonElement> Array(JsonElement element, string name) =>
        Property(element, name) is { ValueKind: JsonValueKind.Array } value ? value.EnumerateArray() : [];
    private static string? String(JsonElement element, string name) => Property(element, name)?.ToString();
    private static int Integer(JsonElement element, string name, int fallback = 0) =>
        Property(element, name) is { } value && value.TryGetInt32(out var result) ? result : fallback;
    private static long Long(JsonElement element, string name) =>
        Property(element, name) is { } value && value.TryGetInt64(out var result) ? result : 0;
    private static bool Boolean(JsonElement element, string name) =>
        Property(element, name) is { } value && value.ValueKind == JsonValueKind.True;
    private static decimal Decimal(JsonElement element, string name, decimal fallback = 0) =>
        Property(element, name) is { } value && value.TryGetDecimal(out var result) ? result : fallback;
    private static DateTimeOffset? FromUnix(long value) => value > 0 ? DateTimeOffset.FromUnixTimeSeconds(value) : null;
    private sealed record TokenResponse(string AccessToken, string RefreshToken, int ExpireIn);
}
