using Avallo.Connector.Amazon;
using Avallo.Connector.MercadoLivre;
using Avallo.Connector.Shopee;
using Avallo.Connectors.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Avallo.Tests.Features;

/// <summary>
/// Regra inegociavel do documento de arquitetura: nenhuma camada acima do plugin pode
/// ramificar por nome de marketplace. Estes testes garantem que o descritor carrega tudo
/// o que a interface precisa e que a UI nao voltou a citar plataformas nominalmente.
/// </summary>
public sealed class ConnectorPresentationTests
{
    private static readonly string[] MarketplaceNames = ["mercado", "shopee", "amazon"];

    [Fact]
    public void Every_shipped_connector_declares_its_own_presentation()
    {
        foreach (var descriptor in Descriptors(configured: false))
        {
            Assert.NotNull(descriptor.Presentation);
            Assert.False(string.IsNullOrWhiteSpace(descriptor.Presentation!.Tagline),
                $"O conector '{descriptor.Name}' precisa declarar uma Tagline.");
            Assert.False(string.IsNullOrWhiteSpace(descriptor.Presentation.LogoUrl),
                $"O conector '{descriptor.Name}' precisa declarar uma LogoUrl.");
            Assert.False(string.IsNullOrWhiteSpace(descriptor.Presentation.BannerHeadline),
                $"O conector '{descriptor.Name}' precisa declarar um BannerHeadline.");
        }
    }

    [Fact]
    public void Unconfigured_connectors_report_themselves_as_pending_without_throwing()
    {
        foreach (var descriptor in Descriptors(configured: false))
        {
            Assert.False(descriptor.IsConfigured,
                $"O conector '{descriptor.Name}' nao deveria estar pronto sem credenciais.");
            Assert.False(string.IsNullOrWhiteSpace(descriptor.ConfigurationHint),
                $"O conector '{descriptor.Name}' precisa explicar o que falta configurar.");
        }
    }

    [Fact]
    public void Configured_connectors_report_themselves_as_ready()
    {
        foreach (var descriptor in Descriptors(configured: true))
        {
            Assert.True(descriptor.IsConfigured,
                $"O conector '{descriptor.Name}' deveria estar pronto com credenciais completas.");
            Assert.Null(descriptor.ConfigurationHint);
        }
    }

    [Fact]
    public void Core_assembly_has_no_compile_time_dependency_on_any_marketplace()
    {
        var core = typeof(Avallo.Web.Features.Connectors.ConnectorRegistry).Assembly;

        var referenced = core.GetReferencedAssemblies()
            .Select(x => x.Name ?? string.Empty)
            .Where(x => x.StartsWith("Avallo.Connector.", StringComparison.Ordinal))
            .ToArray();
        Assert.True(referenced.Length == 0,
            $"O Core referencia conector(es) em tempo de compilacao: {string.Join(", ", referenced)}.");

        // Falha se os fontes dos plugins voltarem via <Compile Include> no csproj do Core.
        foreach (var type in new[]
                 {
                     "Avallo.Connector.MercadoLivre.MercadoLivreConnector",
                     "Avallo.Connector.Shopee.ShopeeConnector",
                     "Avallo.Connector.Amazon.AmazonConnector"
                 })
            Assert.True(core.GetType(type) is null,
                $"O tipo '{type}' foi compilado dentro do assembly do Core.");
    }

    [Theory]
    [InlineData("Avallo.Web/Domain/UserNotification.cs")]
    [InlineData("Avallo.Web/Features/Notifications/NotificationScheduler.cs")]
    [InlineData("Avallo.Web/Features/Notifications/NotificationContracts.cs")]
    [InlineData("Avallo.Web/Features/Notifications/NotificationEndpoints.cs")]
    public void Domain_and_notifications_never_mention_a_marketplace_by_name(string relativePath) =>
        AssertNoMarketplaceName(relativePath);

    [Theory]
    [InlineData("Avallo.Client/Pages/Integrations/Connectors.razor")]
    [InlineData("Avallo.Client/Components/Shared/MarketplaceCarousel.razor")]
    [InlineData("Avallo.Client/Components/Shared/ConnectorCard.razor")]
    [InlineData("Avallo.Client/Pages/Finance/Dashboard.razor")]
    [InlineData("Avallo.Client/Pages/Integrations/Notifications.razor")]
    [InlineData("Avallo.Web/wwwroot/notifications.js")]
    [InlineData("Avallo.Web/wwwroot/app.css")]
    public void Ui_layer_never_mentions_a_marketplace_by_name(string relativePath) =>
        AssertNoMarketplaceName(relativePath);

    private static void AssertNoMarketplaceName(string relativePath)
    {
        var root = RepositoryRoot();
        Assert.NotNull(root);

        var file = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(file), $"Arquivo esperado nao encontrado: {relativePath}");

        var content = File.ReadAllText(file);
        foreach (var marketplace in MarketplaceNames)
            Assert.DoesNotContain(marketplace, content, StringComparison.OrdinalIgnoreCase);
    }

    private static List<ConnectorDescriptor> Descriptors(bool configured)
    {
        using var http = new HttpClient();
        using var mercadoLivreLimiter = new MercadoLivreRateLimiter();
        using var shopeeLimiter = new ShopeeRateLimiter();

        var mercadoLivre = new MercadoLivreConnector(http, Options.Create(configured
            ? new MercadoLivreOptions
            {
                ClientId = "client-id",
                ClientSecret = "client-secret",
                RedirectUri = "https://localhost/api/connectors/oauth/callback"
            }
            : new MercadoLivreOptions()), mercadoLivreLimiter);

        var shopee = new ShopeeConnector(http, Options.Create(configured
            ? new ShopeeOptions
            {
                PartnerId = 1234,
                PartnerKey = "partner-key",
                RedirectUri = "https://localhost/api/connectors/oauth/callback",
                BaseUrl = "https://partner.shopeemobile.com"
            }
            : new ShopeeOptions()), shopeeLimiter);

        var amazon = new AmazonConnector(http, Options.Create(configured
            ? new AmazonOptions
            {
                ClientId = "client-id",
                ClientSecret = "client-secret",
                ApplicationId = "application-id",
                RedirectUri = "https://localhost/api/connectors/oauth/callback",
                AwsAccessKeyId = "aws-key",
                AwsSecretAccessKey = "aws-secret"
            }
            : new AmazonOptions()));

        return [mercadoLivre.Descriptor, shopee.Descriptor, amazon.Descriptor];
    }

    private static string? RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (directory.EnumerateFiles("Avallo.slnx").Any())
                return directory.FullName;
            directory = directory.Parent;
        }
        return null;
    }
}
