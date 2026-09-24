using Avallo.Connectors.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Avallo.Connector.Shopee;

public sealed class ShopeeModule : IConnectorModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ShopeeOptions>().BindConfiguration(ShopeeOptions.SectionName);
        services.AddSingleton<ShopeeRateLimiter>();
        services.AddHttpClient<ShopeeConnector>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Avallo-Shopee/1.0");
        });
        services.AddTransient<IMarketplaceConnector>(services => services.GetRequiredService<ShopeeConnector>());
    }
}
