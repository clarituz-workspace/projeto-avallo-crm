using Avallo.Connectors.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Avallo.Connector.MercadoLivre;

public sealed class MercadoLivreModule : IConnectorModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MercadoLivreOptions>()
            .BindConfiguration(MercadoLivreOptions.SectionName);
        services.AddSingleton<MercadoLivreRateLimiter>();
        services.AddHttpClient<MercadoLivreConnector>(client =>
        {
            client.BaseAddress = new Uri("https://api.mercadolibre.com/");
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Avallo-MercadoLivre/1.0");
        });
        services.AddTransient<IMarketplaceConnector>(services => services.GetRequiredService<MercadoLivreConnector>());
    }
}
