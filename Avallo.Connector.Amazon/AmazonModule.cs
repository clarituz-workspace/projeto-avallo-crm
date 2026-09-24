using Avallo.Connectors.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Avallo.Connector.Amazon;

public sealed class AmazonModule : IConnectorModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AmazonOptions>().BindConfiguration(AmazonOptions.SectionName);
        services.AddHttpClient<AmazonConnector>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(45);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Avallo-Amazon-SP-API/1.0");
        });
        services.AddTransient<IMarketplaceConnector>(services => services.GetRequiredService<AmazonConnector>());
    }
}
