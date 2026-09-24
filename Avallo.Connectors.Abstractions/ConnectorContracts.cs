using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json.Serialization;

namespace Avallo.Connectors.Abstractions;

public interface IMarketplaceConnector
{
    ConnectorDescriptor Descriptor { get; }
    Task<ConnectorAuthentication> AuthenticateAsync(AuthenticationRequest request, CancellationToken cancellationToken = default);
    Task<ConnectorAuthentication> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default);
    Task<ConnectorPage<StandardOrder>> GetOrdersAsync(ConnectorContext context, OrderFilter filter, CancellationToken cancellationToken = default);
    Task<StandardOrder> GetOrderDetailAsync(ConnectorContext context, string orderId, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<StandardPayment>> GetPaymentsAsync(ConnectorContext context, string orderId, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<StandardFee>> GetFeesAsync(ConnectorContext context, string orderId, CancellationToken cancellationToken = default);
    Task<ConnectorPage<StandardInvoice>> GetInvoicesAsync(ConnectorContext context, InvoiceFilter filter, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ConnectorPage<StandardInvoice>([], null, false));
    IAsyncEnumerable<StandardOrder> SyncAllAsync(ConnectorContext context, DateTimeOffset since, CancellationToken cancellationToken = default);
    Task<ConnectorStatus> GetStatusAsync(ConnectorContext context, CancellationToken cancellationToken = default);
}

public interface IConnectorModule
{
    void Register(IServiceCollection services, IConfiguration configuration);
}

/// <summary>
/// Tudo o que o Core e a interface precisam saber sobre um marketplace.
/// A UI monta cartoes, banners, formularios e estados a partir deste contrato:
/// nenhuma camada acima do plugin pode ramificar por nome de plataforma.
/// </summary>
public sealed record ConnectorDescriptor(
    string Name,
    string DisplayName,
    string Version,
    bool SupportsInvoices = false,
    IReadOnlyCollection<ConnectorCredentialField>? CredentialFields = null,
    bool UsesOAuth = false,
    ConnectorPresentation? Presentation = null,
    bool IsConfigured = true,
    string? ConfigurationHint = null);

/// <summary>
/// Metadados visuais declarados pelo proprio plugin. Sem isso a interface precisaria
/// conhecer cada marketplace para escolher logo, texto e cor — exatamente o acoplamento
/// que a arquitetura modular proibe.
/// </summary>
public sealed record ConnectorPresentation(
    string Tagline,
    string? LogoUrl = null,
    string? BannerLogoUrl = null,
    string? BannerHeadline = null,
    string? BannerSubtitle = null,
    string? AccentFrom = null,
    string? AccentTo = null,
    string? SoundUrl = null);

public sealed record ConnectorCredentialField(
    string Name,
    string Label,
    bool Secret = false,
    bool Required = true);

public interface IOAuthMarketplaceConnector
{
    Task<OAuthAuthorization> BeginAuthenticationAsync(
        OAuthStartRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record OAuthStartRequest(Guid TenantId, string State, string CallbackUrl, string CodeChallenge);
public sealed record OAuthAuthorization(Uri AuthorizationUri);

public sealed record AuthenticationRequest(
    Guid TenantId,
    IReadOnlyDictionary<string, string> Credentials,
    string? CallbackUrl = null);

public sealed record RefreshTokenRequest(Guid TenantId, string RefreshToken, string? ExternalAccountId = null);

public sealed record ConnectorAuthentication(
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset ExpiresAt,
    string ExternalAccountId,
    string? AccountDisplayName = null);

public sealed record ConnectorContext(
    Guid TenantId,
    Guid ConnectionId,
    string AccessToken,
    string ExternalAccountId);

public sealed record OrderFilter(
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    StandardOrderStatus? Status = null,
    string? Cursor = null,
    int PageSize = 50);

public sealed record InvoiceFilter(
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    string? Cursor = null,
    int PageSize = 50);

public sealed record ConnectorPage<T>(IReadOnlyCollection<T> Items, string? NextCursor, bool HasMore);

public sealed record StandardOrder(
    [property: JsonPropertyName("order_id")] string OrderId,
    [property: JsonPropertyName("platform")] string Platform,
    [property: JsonPropertyName("date")] DateTimeOffset Date,
    [property: JsonPropertyName("gross_value")] decimal GrossValue,
    [property: JsonPropertyName("platform_fee")] decimal PlatformFee,
    [property: JsonPropertyName("net_value")] decimal NetValue,
    [property: JsonPropertyName("payment_method")] string PaymentMethod,
    [property: JsonPropertyName("payment_date")] DateTimeOffset? PaymentDate,
    [property: JsonPropertyName("release_date")] DateTimeOffset? ReleaseDate,
    [property: JsonPropertyName("status")] StandardOrderStatus Status,
    [property: JsonPropertyName("buyer_name")] string BuyerName,
    [property: JsonPropertyName("items")] IReadOnlyCollection<StandardOrderItem> Items,
    [property: JsonPropertyName("invoice_number")] string? InvoiceNumber,
    [property: JsonPropertyName("fulfillment_status")] StandardFulfillmentStatus FulfillmentStatus = StandardFulfillmentStatus.Unknown,
    [property: JsonPropertyName("delivered_at")] DateTimeOffset? DeliveredAt = null,
    [property: JsonPropertyName("currency")] string Currency = "BRL");

public sealed record StandardOrderItem(
    string? Sku,
    string Title,
    int Quantity,
    decimal UnitValue);

public enum StandardOrderStatus
{
    Paid,
    Pending,
    Cancelled
}

public enum StandardFulfillmentStatus
{
    Unknown,
    Pending,
    Shipped,
    Delivered,
    Returned
}

/// <summary>
/// Split financeiro exato de um pagamento, na forma exigida pela secao 03 do documento de
/// arquitetura: o conector nao devolve so o bruto e o liquido, mas cada vazamento em campo
/// proprio, ja classificado contabilmente.
///
/// <para>
/// <c>gross_value</c> receita · <c>platform_fee</c> comissao do marketplace (despesa de venda) ·
/// <c>payment_fee</c> taxa de gateway (despesa financeira) · <c>shipping_cost</c> frete retido do
/// seller (despesa comercial) · <c>net_value</c> liquido liberado para repasse.
/// </para>
///
/// <para>
/// Estes campos sao o split declarado pela plataforma, usado para conciliacao e auditoria.
/// O razao contabil continua sendo montado a partir de <see cref="StandardFee"/>, que traz cada
/// taxa individualizada com a sua categoria — lancar os dois levaria a contagem dobrada.
/// </para>
/// </summary>
public sealed record StandardPayment(
    [property: JsonPropertyName("payment_id")] string PaymentId,
    [property: JsonPropertyName("order_id")] string OrderId,
    [property: JsonPropertyName("gross_value")] decimal GrossValue,
    [property: JsonPropertyName("net_value")] decimal NetValue,
    [property: JsonPropertyName("payment_method")] string Method,
    [property: JsonPropertyName("status")] StandardPaymentStatus Status,
    [property: JsonPropertyName("payment_date")] DateTimeOffset? PaidAt,
    [property: JsonPropertyName("release_date")] DateTimeOffset? ReleaseAt,
    [property: JsonPropertyName("payment_fee")] decimal PaymentFee = 0,
    [property: JsonPropertyName("currency")] string Currency = "BRL",
    [property: JsonPropertyName("platform_fee")] decimal PlatformFee = 0,
    [property: JsonPropertyName("shipping_cost")] decimal ShippingCost = 0)
{
    /// <summary>
    /// Sobra do split: bruto menos comissao, taxa de pagamento e frete. Quando a plataforma
    /// informa todos os campos, deve bater com <see cref="NetValue"/>.
    /// </summary>
    [JsonPropertyName("split_residual")]
    public decimal SplitResidual => GrossValue - PlatformFee - PaymentFee - ShippingCost - NetValue;
}

public enum StandardPaymentStatus
{
    Paid,
    Pending,
    Cancelled,
    Refunded
}

public sealed record StandardFee(
    string Type,
    string Description,
    decimal Amount,
    string Currency = "BRL",
    StandardFeeCategory Category = StandardFeeCategory.Other,
    string? ExternalId = null);

public enum StandardFeeCategory
{
    MarketplaceCommission,
    PaymentProcessing,
    SellerShipping,
    Refund,
    Chargeback,
    TaxWithholding,
    Other
}

public sealed record StandardInvoice(
    string InvoiceId,
    string? OrderId,
    string Number,
    DateTimeOffset IssuedAt,
    decimal TotalValue,
    string? AccessKey,
    Uri? DocumentUrl);

public sealed record ConnectorStatus(
    ConnectorConnectionState State,
    string? Message = null,
    DateTimeOffset? CheckedAt = null);

public enum ConnectorConnectionState
{
    Active,
    Expired,
    Revoked,
    Error
}

public class ConnectorException(
    string connectorName,
    string code,
    string message,
    bool isTransient = false,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string ConnectorName { get; } = connectorName;
    public string Code { get; } = code;
    public bool IsTransient { get; } = isTransient;
}
