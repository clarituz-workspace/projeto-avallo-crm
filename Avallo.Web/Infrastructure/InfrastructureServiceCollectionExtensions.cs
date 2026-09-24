using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Avallo.Web.Features.Expenses;
using Avallo.Web.Features.Fiscal;
using Avallo.Web.Features.Inventory;
using Avallo.Web.Features.Reconciliation;

namespace Avallo.Web.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required.");

        // 1. Data Access & Persistence
        // AppDbContext depends on the scoped tenant context, so it cannot use the pooled factory.
        // O interceptor publica app.tenant_id a cada abertura de conexao; e o que as policies
        // de Row Level Security leem no PostgreSQL.
        services.AddScoped<TenantRlsConnectionInterceptor>();
        services.AddDbContext<AppDbContext>((sp, options) => options
            .UseNpgsql(connectionString)
            .AddInterceptors(sp.GetRequiredService<TenantRlsConnectionInterceptor>()));

        // Sobrescreve a resolucao do AppDbContext: abre a conexao fisica uma unica vez por
        // scope (scoped = 1 por request/scope de worker) e o EF a mantem aberta ate o dispose.
        // Assim o set_config('app.tenant_id') do interceptor custa 1 roundtrip por scope em
        // vez de 1 por query — o EF abriria/fecharia a conexao a cada operacao e pagaria 1
        // RTT extra por statement (1-15ms por query em Azure).
        // A abertura antecipada so acontece quando o tenant ja esta definido na resolucao
        // (JWT ou ITenantScope.BeginScope anterior, como nos workers). Endpoints anonimos que
        // definem o tenant depois da resolucao do contexto (webhook de marketplace, callback
        // OAuth) mantem a abertura lazy por query — mais lenta, porem aplica o tenant correto
        // a cada abertura; abrir cedo nesses casos congelaria app.tenant_id vazio.
        services.AddScoped(sp =>
        {
            var tenantContext = sp.GetRequiredService<ITenantContext>();
            var context = new AppDbContext(
                sp.GetRequiredService<DbContextOptions<AppDbContext>>(),
                tenantContext);
            if (tenantContext.TenantId is not null)
                context.Database.OpenConnection();
            return context;
        });

        // 2. Multi-Tenancy Infrastructure
        services.AddHttpContextAccessor();
        services.AddScoped<HttpTenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<HttpTenantContext>());
        services.AddScoped<ITenantScope>(sp => sp.GetRequiredService<HttpTenantContext>());

        // 3. Storage & Cloud Infrastructure
        services.Configure<ObjectStorageOptions>(configuration.GetSection("ObjectStorage"));
        services.AddScoped<AzureBlobExpenseStorage>();
        services.AddScoped<S3ObjectStorage>();
        // Provider por config: "s3" = MinIO local / qualquer S3-compativel; "azure" = Azure Blob (producao).
        services.AddScoped<IExpenseStorage>(sp =>
            sp.GetRequiredService<IOptions<ObjectStorageOptions>>().Value.IsS3
                ? sp.GetRequiredService<S3ObjectStorage>()
                : (IExpenseStorage)sp.GetRequiredService<AzureBlobExpenseStorage>());

        // 4. External Clients & Services
        services.AddHttpClient<BrasilApiCnpjClient>(client =>
            client.BaseAddress = new Uri("https://brasilapi.com.br/"));

        // 5. Parsers & Converters
        services.AddSingleton<INfeXmlParser, NfeXmlParser>();
        services.AddSingleton<IStatementParser, StatementParser>();

        // 6. Security, Caching & Time Provider
        services.AddMemoryCache();
        services.AddDataProtection().SetApplicationName("Avallo");
        services.AddSingleton(TimeProvider.System);

        return services;
    }
}
