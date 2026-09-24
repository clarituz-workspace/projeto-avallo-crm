using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Avallo.Connectors.Abstractions;
using Microsoft.Extensions.Options;

namespace Avallo.Connector.MercadoLivre;

public sealed class MercadoLivreConnector(
    HttpClient httpClient,
    IOptions<MercadoLivreOptions> options,
    MercadoLivreRateLimiter rateLimiter) : IMarketplaceConnector, IOAuthMarketplaceConnector
{
    private static readonly TimeZoneInfo SaoPaulo = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    private static readonly ConnectorPresentation Branding = new(
        Tagline: "Integre sua loja do Mercado Livre",
        LogoUrl: "https://res.cloudinary.com/lz9vero5/image/upload/v1786366355/mercado-livre-logo-2022_tpowon.svg",
        BannerLogoUrl: "https://http2.mlstatic.com/frontend-assets/ml-web-navigation/ui-navigation/5.21.22/mercadolibre/logo__large.png",
        BannerHeadline: "Venda mais com o Mercado Livre",
        BannerSubtitle: "Acompanhe pedidos e recebimentos em um só lugar.",
        AccentFrom: "rgba(255, 244, 194, .88)",
        AccentTo: "rgba(255, 226, 109, .88)",
        SoundUrl: "https://www.myinstants.com/media/sounds/mercado-livre-entrega.mp3");

    public ConnectorDescriptor Descriptor
    {
        get
        {
            var settings = options.Value;
            var configured = !string.IsNullOrWhiteSpace(settings.ClientId) &&
                             !string.IsNullOrWhiteSpace(settings.ClientSecret) &&
                             Uri.TryCreate(settings.RedirectUri, UriKind.Absolute, out _);
            return new ConnectorDescriptor(
                "mercado-livre", "Mercado Livre", "2.0.0", SupportsInvoices: false,
                CredentialFields: null, UsesOAuth: true,
                Presentation: Branding,
                IsConfigured: configured,
                ConfigurationHint: configured
                    ? null
                    : "Configure Connectors:MercadoLivre:ClientId, ClientSecret e RedirectUri.");
        }
    }

    public Task<OAuthAuthorization> BeginAuthenticationAsync(OAuthStartRequest request, CancellationToken cancellationToken = default)
    {
        var settings = Settings;
        var query = $"response_type=code&client_id={Uri.EscapeDataString(settings.ClientId)}&redirect_uri={Uri.EscapeDataString(settings.RedirectUri)}&state={Uri.EscapeDataString(request.State)}&code_challenge={Uri.EscapeDataString(request.CodeChallenge)}&code_challenge_method=S256";
        return Task.FromResult(new OAuthAuthorization(new Uri($"https://auth.mercadolivre.com.br/authorization?{query}")));
    }

    public async Task<ConnectorAuthentication> AuthenticateAsync(AuthenticationRequest request, CancellationToken cancellationToken = default)
    {
        if (!request.Credentials.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
            throw Error("authorization_code_required", "Mercado Livre authorization code is required.");
        if (!request.Credentials.TryGetValue("code_verifier", out var codeVerifier) || string.IsNullOrWhiteSpace(codeVerifier))
            throw Error("code_verifier_required", "Mercado Livre PKCE code verifier is required.");
        var token = await RequestTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["client_id"] = Settings.ClientId,
            ["client_secret"] = Settings.ClientSecret, ["code"] = code,
            ["redirect_uri"] = Settings.RedirectUri, ["code_verifier"] = codeVerifier
        }, cancellationToken);
        var user = await GetJsonAsync($"users/{token.UserId}", token.AccessToken, cancellationToken);
        return new ConnectorAuthentication(token.AccessToken, token.RefreshToken,
            DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn), token.UserId,
            String(user, "nickname") ?? token.UserId);
    }

    public async Task<ConnectorAuthentication> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default)
    {
        var token = await RequestTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token", ["client_id"] = Settings.ClientId,
            ["client_secret"] = Settings.ClientSecret, ["refresh_token"] = request.RefreshToken
        }, cancellationToken);
        return new ConnectorAuthentication(token.AccessToken, token.RefreshToken,
            DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn), token.UserId);
    }

    public async Task<ConnectorPage<StandardOrder>> GetOrdersAsync(
        ConnectorContext context,
        OrderFilter filter,
        CancellationToken cancellationToken = default)
    {
        var offset = int.TryParse(filter.Cursor, out var parsedOffset) ? parsedOffset : 0;
        var limit = Math.Clamp(filter.PageSize, 1, 50);
        var parameters = new List<string>
        {
            $"seller={Uri.EscapeDataString(context.ExternalAccountId)}", $"offset={offset}", $"limit={limit}", "sort=date_desc"
        };
        if (filter.From is { } from)
            parameters.Add($"order.date_created.from={Uri.EscapeDataString(from.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture))}");
        if (filter.To is { } to)
            parameters.Add($"order.date_created.to={Uri.EscapeDataString(to.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture))}");
        var json = await GetJsonAsync($"orders/search?{string.Join('&', parameters)}", context.AccessToken, cancellationToken);
        var orders = new List<StandardOrder>();
        foreach (var source in Array(json, "results"))
        {
            var order = await EnrichFulfillmentAsync(MapOrder(source), source, context.AccessToken, cancellationToken);
            if (filter.Status is null || order.Status == filter.Status) orders.Add(order);
        }
        var total = Property(json, "paging") is { } paging ? Integer(paging, "total") : offset + orders.Count;
        var nextOffset = offset + limit;
        return new ConnectorPage<StandardOrder>(orders, nextOffset < total ? nextOffset.ToString() : null, nextOffset < total);
    }

    public async Task<StandardOrder> GetOrderDetailAsync(
        ConnectorContext context,
        string orderId,
        CancellationToken cancellationToken = default)
    {
        var source = await GetJsonAsync($"orders/{Uri.EscapeDataString(orderId)}", context.AccessToken, cancellationToken);
        return await EnrichFulfillmentAsync(MapOrder(source), source, context.AccessToken, cancellationToken);
    }

    public async Task<IReadOnlyCollection<StandardPayment>> GetPaymentsAsync(
        ConnectorContext context,
        string orderId,
        CancellationToken cancellationToken = default)
    {
        var order = await GetJsonAsync($"orders/{Uri.EscapeDataString(orderId)}", context.AccessToken, cancellationToken);
        var platformFee = Array(order, "order_items").Sum(x => Decimal(x, "sale_fee"));
        var shippingCost = Property(order, "shipping") is { } shipping ? Decimal(shipping, "cost") : 0;
        var payments = new List<StandardPayment>();
        foreach (var embedded in Array(order, "payments"))
        {
            var paymentId = Identifier(embedded, "id");
            if (paymentId is null) continue;
            var payment = await GetJsonAsync($"payments/{Uri.EscapeDataString(paymentId)}", context.AccessToken, cancellationToken);
            payments.Add(MapPayment(payment, orderId, platformFee, shippingCost));
            // Comissao e frete pertencem ao pedido: atribui so ao primeiro pagamento para
            // nao multiplicar o valor quando a venda e paga em mais de uma transacao.
            platformFee = 0;
            shippingCost = 0;
        }
        return payments;
    }

    public async Task<IReadOnlyCollection<StandardFee>> GetFeesAsync(
        ConnectorContext context,
        string orderId,
        CancellationToken cancellationToken = default)
    {
        var order = await GetJsonAsync($"orders/{Uri.EscapeDataString(orderId)}", context.AccessToken, cancellationToken);
        var fees = new List<StandardFee>();
        foreach (var item in Array(order, "order_items"))
        {
            var amount = Decimal(item, "sale_fee");
            if (amount > 0) fees.Add(new StandardFee("sale_fee", $"Comissao - {NestedString(item, "item", "title") ?? "item"}", amount,
                Category: StandardFeeCategory.MarketplaceCommission));
        }
        var shipping = Property(order, "shipping") is { } shippingElement ? Decimal(shippingElement, "cost") : 0;
        if (shipping > 0) fees.Add(new StandardFee("shipping_cost", "Custo de envio", shipping,
            Category: StandardFeeCategory.SellerShipping, ExternalId: "shipping_cost"));
        return fees;
    }

    public async IAsyncEnumerable<StandardOrder> SyncAllAsync(
        ConnectorContext context,
        DateTimeOffset since,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string? cursor = null;
        do
        {
            var page = await GetOrdersAsync(context, new OrderFilter(since, null, null, cursor, 50), cancellationToken);
            foreach (var order in page.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return order;
            }
            cursor = page.NextCursor;
            if (!page.HasMore) break;
        } while (cursor is not null);
    }

    public async Task<ConnectorStatus> GetStatusAsync(ConnectorContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var user = await GetJsonAsync($"users/{context.ExternalAccountId}", context.AccessToken, cancellationToken);
            var status = String(user, "status") ?? "active";
            return new ConnectorStatus(status.Equals("active", StringComparison.OrdinalIgnoreCase)
                ? ConnectorConnectionState.Active : ConnectorConnectionState.Error, status, DateTimeOffset.UtcNow);
        }
        catch (ConnectorException exception) when (exception.Code is "unauthorized" or "forbidden")
        {
            return new ConnectorStatus(ConnectorConnectionState.Expired, exception.Message, DateTimeOffset.UtcNow);
        }
    }

    private async Task<TokenResponse> RequestTokenAsync(Dictionary<string, string> values, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsync("oauth/token", new FormUrlEncodedContent(values), cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw await ApiErrorAsync(response, cancellationToken);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
        return new TokenResponse(
            String(json, "access_token") ?? throw Error("invalid_token_response", "MELI did not return an access token."),
            String(json, "refresh_token"), Integer(json, "expires_in", 21600),
            Identifier(json, "user_id") ?? throw Error("invalid_token_response", "MELI did not return the seller id."));
    }

    private async Task<JsonElement> GetJsonAsync(string path, string accessToken, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await rateLimiter.AcquireAsync(accessToken, cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < 3)
            {
                var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(attempt * 2);
                await Task.Delay(delay > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : delay, cancellationToken);
                continue;
            }
            if (!response.IsSuccessStatusCode)
                throw await ApiErrorAsync(response, cancellationToken);
            return (await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken)).Clone();
        }
        throw Error("rate_limit", "Mercado Livre rate limit was exceeded.", true);
    }

    private static async Task<ConnectorException> ApiErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        string? apiCode = null;
        string? apiMessage = null;
        try
        {
            using var document = JsonDocument.Parse(body);
            apiCode = String(document.RootElement, "error");
            apiMessage = String(document.RootElement, "message") ?? String(document.RootElement, "error_description");
        }
        catch (JsonException)
        {
        }
        var code = apiCode ?? response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "unauthorized",
            HttpStatusCode.Forbidden => "forbidden",
            HttpStatusCode.TooManyRequests => "rate_limit",
            _ => $"meli_{(int)response.StatusCode}"
        };
        var detail = apiMessage ?? body[..Math.Min(body.Length, 500)];
        return Error(code, $"Mercado Livre API error ({(int)response.StatusCode}): {detail}",
            response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500);
    }

    private static StandardOrder MapOrder(JsonElement order)
    {
        var payments = Array(order, "payments").ToArray();
        var approvedPayment = payments.FirstOrDefault(x => String(x, "status") == "approved");
        var gross = Decimal(order, "total_amount");
        var items = Array(order, "order_items").Select(item => new StandardOrderItem(
            NestedString(item, "item", "seller_sku") ?? NestedString(item, "item", "id"),
            NestedString(item, "item", "title") ?? "Produto",
            Integer(item, "quantity"), Decimal(item, "unit_price"))).ToArray();
        var saleFee = Array(order, "order_items").Sum(item => Decimal(item, "sale_fee"));
        var shipping = Property(order, "shipping");
        var shippingFee = shipping is { } shippingElement ? Decimal(shippingElement, "cost") : 0;
        var fee = saleFee + shippingFee;
        var net = Property(approvedPayment, "transaction_details") is { } transaction
            ? Decimal(transaction, "net_received_amount", gross - fee) : gross - fee;
        var status = String(order, "status") == "cancelled" ? StandardOrderStatus.Cancelled
            : approvedPayment.ValueKind != JsonValueKind.Undefined ? StandardOrderStatus.Paid : StandardOrderStatus.Pending;
        var fulfillmentStatus = shipping is { } shipment ? String(shipment, "status") switch
        {
            "delivered" => StandardFulfillmentStatus.Delivered,
            "shipped" or "ready_to_ship" => StandardFulfillmentStatus.Shipped,
            "returned" => StandardFulfillmentStatus.Returned,
            null => StandardFulfillmentStatus.Unknown,
            _ => StandardFulfillmentStatus.Pending
        } : StandardFulfillmentStatus.Unknown;
        var buyer = Property(order, "buyer");
        var buyerName = buyer is null ? "" : String(buyer.Value, "nickname") ??
            $"{String(buyer.Value, "first_name")} {String(buyer.Value, "last_name")}".Trim();
        return new StandardOrder(
            Identifier(order, "id") ?? throw Error("invalid_order", "MELI order id is missing."),
            "mercado-livre", LocalDate(Date(order, "date_created")), gross, fee, Math.Max(0, net),
            String(approvedPayment, "payment_type") ?? String(approvedPayment, "payment_method_id") ?? "Nao informado",
            NullableLocalDate(approvedPayment, "date_approved"), NullableLocalDate(approvedPayment, "money_release_date"),
            status, buyerName, items, String(order, "invoice_number"), fulfillmentStatus,
            shipping is { } deliveredShipment ? NullableLocalDate(deliveredShipment, "date_delivered") : null,
            String(order, "currency_id") ?? "BRL");
    }

    // O split do documento pede comissao e frete no proprio pagamento. No Mercado Livre a
    // comissao vem em order_items[].sale_fee e o frete do seller em shipping.cost, fora do
    // recurso de pagamento — por isso sao passados pelo pedido.
    private static StandardPayment MapPayment(
        JsonElement payment,
        string orderId,
        decimal platformFee = 0,
        decimal shippingCost = 0)
    {
        var status = String(payment, "status") switch
        {
            "approved" => StandardPaymentStatus.Paid, "cancelled" or "rejected" => StandardPaymentStatus.Cancelled,
            "refunded" or "charged_back" => StandardPaymentStatus.Refunded, _ => StandardPaymentStatus.Pending
        };
        var transaction = Property(payment, "transaction_details");
        var paymentFee = Array(payment, "fee_details").Sum(x => Decimal(x, "amount"));
        return new StandardPayment(
            Identifier(payment, "id") ?? "", orderId, Decimal(payment, "transaction_amount"),
            transaction is null ? 0 : Decimal(transaction.Value, "net_received_amount"),
            String(payment, "payment_type_id") ?? String(payment, "payment_method_id") ?? "Nao informado",
            status, NullableLocalDate(payment, "date_approved"), NullableLocalDate(payment, "money_release_date"),
            paymentFee, String(payment, "currency_id") ?? "BRL", platformFee, shippingCost);
    }

    private async Task<StandardOrder> EnrichFulfillmentAsync(
        StandardOrder order,
        JsonElement source,
        string accessToken,
        CancellationToken cancellationToken)
    {
        if (order.FulfillmentStatus != StandardFulfillmentStatus.Unknown ||
            Property(source, "shipping") is not { } shipping || Identifier(shipping, "id") is not { } shipmentId)
            return order;
        var shipment = await GetJsonAsync($"shipments/{Uri.EscapeDataString(shipmentId)}", accessToken, cancellationToken);
        var status = String(shipment, "status") switch
        {
            "delivered" => StandardFulfillmentStatus.Delivered,
            "shipped" or "ready_to_ship" => StandardFulfillmentStatus.Shipped,
            "returned" or "not_delivered" when String(shipment, "substatus") == "returned_to_sender" => StandardFulfillmentStatus.Returned,
            _ => StandardFulfillmentStatus.Pending
        };
        var deliveredAt = NullableLocalDate(shipment, "date_delivered") ??
                          (status == StandardFulfillmentStatus.Delivered ? NullableLocalDate(shipment, "date_last_updated") : null);
        return order with { FulfillmentStatus = status, DeliveredAt = deliveredAt };
    }

    private static ConnectorException Error(string code, string message, bool transient = false) =>
        new("mercado-livre", code, message, transient);
    private MercadoLivreOptions Settings
    {
        get
        {
            var settings = options.Value;
            if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.ClientSecret) ||
                !Uri.TryCreate(settings.RedirectUri, UriKind.Absolute, out _))
                throw Error("connector_not_configured", "Mercado Livre app credentials and redirect URI are not configured.");
            return settings;
        }
    }
    private static JsonElement? Property(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value : null;
    private static IEnumerable<JsonElement> Array(JsonElement element, string name) =>
        Property(element, name) is { ValueKind: JsonValueKind.Array } value ? value.EnumerateArray() : [];
    private static string? String(JsonElement element, string name) => Property(element, name)?.ToString();
    private static string? Identifier(JsonElement element, string name) => Property(element, name)?.ToString();
    private static string? NestedString(JsonElement element, string parent, string name) =>
        Property(element, parent) is { } nested ? String(nested, name) : null;
    private static int Integer(JsonElement element, string name, int fallback = 0) =>
        Property(element, name) is { } value && value.TryGetInt32(out var result) ? result : fallback;
    private static decimal Decimal(JsonElement element, string name, decimal fallback = 0) =>
        Property(element, name) is { } value && value.TryGetDecimal(out var result) ? result : fallback;
    private static DateTimeOffset Date(JsonElement element, string name) =>
        DateTimeOffset.TryParse(String(element, name), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var result) ? result : DateTimeOffset.UtcNow;
    private static DateTimeOffset? NullableLocalDate(JsonElement element, string name) =>
        DateTimeOffset.TryParse(String(element, name), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var result) ? LocalDate(result) : null;
    private static DateTimeOffset LocalDate(DateTimeOffset date) => TimeZoneInfo.ConvertTime(date, SaoPaulo);

    private sealed record TokenResponse(string AccessToken, string? RefreshToken, int ExpiresIn, string UserId);
}
