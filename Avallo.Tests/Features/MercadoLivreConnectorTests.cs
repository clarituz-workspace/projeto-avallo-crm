using System.Net;
using System.Text;
using Avallo.Connector.MercadoLivre;
using Avallo.Connectors.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Avallo.Tests.Features;

public sealed class MercadoLivreConnectorTests
{
    [Fact]
    public async Task OAuth_authentication_returns_seller_and_six_hour_token()
    {
        using var connector = CreateConnector();
        var before = DateTimeOffset.UtcNow;

        var authentication = await connector.Value.AuthenticateAsync(
            new AuthenticationRequest(Guid.NewGuid(), new Dictionary<string, string>
            {
                ["code"] = "valid-code",
                ["code_verifier"] = "valid-code-verifier"
            }),
            TestContext.Current.CancellationToken);

        Assert.Equal("access-token", authentication.AccessToken);
        Assert.Equal("refresh-token", authentication.RefreshToken);
        Assert.Equal("123", authentication.ExternalAccountId);
        Assert.Equal("seller_nickname", authentication.AccountDisplayName);
        Assert.InRange(authentication.ExpiresAt, before.AddHours(5.9), before.AddHours(6.1));
    }

    [Fact]
    public async Task OAuth_start_and_refresh_follow_the_meli_protocol()
    {
        using var connector = CreateConnector();

        var start = await connector.Value.BeginAuthenticationAsync(
            new OAuthStartRequest(Guid.NewGuid(), "protected-state", "https://localhost/callback", "code-challenge"),
            TestContext.Current.CancellationToken);
        var refreshed = await connector.Value.RefreshTokenAsync(
            new RefreshTokenRequest(Guid.NewGuid(), "refresh-token"),
            TestContext.Current.CancellationToken);

        Assert.Equal("auth.mercadolivre.com.br", start.AuthorizationUri.Host);
        Assert.Contains("state=protected-state", start.AuthorizationUri.Query);
        Assert.Contains("code_challenge=code-challenge", start.AuthorizationUri.Query);
        Assert.Contains("code_challenge_method=S256", start.AuthorizationUri.Query);
        Assert.Equal("access-token", refreshed.AccessToken);
        Assert.Equal("refresh-token", refreshed.RefreshToken);
    }

    [Fact]
    public async Task Order_is_normalized_with_fees_payment_and_sao_paulo_timezone()
    {
        using var connector = CreateConnector();
        var context = new ConnectorContext(Guid.NewGuid(), Guid.NewGuid(), "access-token", "123");

        var page = await connector.Value.GetOrdersAsync(context, new OrderFilter(PageSize: 50), TestContext.Current.CancellationToken);
        var order = Assert.Single(page.Items);
        var fees = await connector.Value.GetFeesAsync(context, "ORDER-1", TestContext.Current.CancellationToken);
        var payments = await connector.Value.GetPaymentsAsync(context, "ORDER-1", TestContext.Current.CancellationToken);

        Assert.Equal("mercado-livre", order.Platform);
        Assert.Equal(StandardOrderStatus.Paid, order.Status);
        Assert.Equal(TimeSpan.FromHours(-3), order.Date.Offset);
        Assert.Equal(100, order.GrossValue);
        Assert.Equal(15, order.PlatformFee);
        Assert.Equal(85, order.NetValue);
        Assert.Equal(2, fees.Count);
        Assert.Equal(85, Assert.Single(payments).NetValue);
    }

    private static ConnectorFixture CreateConnector()
    {
        var handler = new MercadoLivreHandler();
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.mercadolibre.com/") };
        var limiter = new MercadoLivreRateLimiter();
        var connector = new MercadoLivreConnector(client, Options.Create(new MercadoLivreOptions
        {
            ClientId = "client-id", ClientSecret = "client-secret",
            RedirectUri = "https://localhost:7128/api/connectors/oauth/callback"
        }), limiter);
        return new ConnectorFixture(connector, client, limiter);
    }

    private sealed class ConnectorFixture(
        MercadoLivreConnector value,
        HttpClient client,
        MercadoLivreRateLimiter limiter) : IDisposable
    {
        public MercadoLivreConnector Value { get; } = value;
        public void Dispose() { client.Dispose(); limiter.Dispose(); }
    }

    private sealed class MercadoLivreHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            var json = path switch
            {
                "/oauth/token" => """{"access_token":"access-token","refresh_token":"refresh-token","expires_in":21600,"user_id":123}""",
                "/users/123" => """{"id":123,"nickname":"seller_nickname","status":"active"}""",
                var value when value.StartsWith("/orders/search", StringComparison.Ordinal) => $$"""{"paging":{"total":1},"results":[{{OrderJson}}]}""",
                "/orders/ORDER-1" => OrderJson,
                "/payments/99" => PaymentJson,
                _ => "{}"
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }

        private const string OrderJson = """
            {"id":"ORDER-1","status":"paid","date_created":"2026-05-15T15:00:00Z","total_amount":100,
             "buyer":{"nickname":"buyer"},"shipping":{"cost":5},
             "order_items":[{"item":{"id":"ITEM-1","seller_sku":"SKU-1","title":"Product"},"quantity":1,"unit_price":100,"sale_fee":10}],
             "payments":[{"id":99,"status":"approved","payment_type":"credit_card","date_approved":"2026-05-15T16:00:00Z","money_release_date":"2026-05-20T12:00:00Z","transaction_details":{"net_received_amount":85}}]}
            """;
        private const string PaymentJson = """
            {"id":99,"status":"approved","transaction_amount":100,"payment_type_id":"credit_card",
             "date_approved":"2026-05-15T16:00:00Z","money_release_date":"2026-05-20T12:00:00Z","transaction_details":{"net_received_amount":85}}
            """;
    }
}
