using Avallo.Client.Services;

namespace Avallo.Client;

public static class ClientServiceCollectionExtensions
{
    public static IServiceCollection AddClientServices(this IServiceCollection services)
    {
        // Core Infrastructure Services
        services.AddScoped<AuthService>();
        services.AddScoped<GeoLocationService>();
        services.AddScoped<WebPushNotificationService>();
        services.AddScoped<DeploymentRealtimeService>();
        services.AddSingleton<AppLocalizer>();

        // Domain API Client Services
        services.AddScoped<UserAccessClient>();
        services.AddScoped<ReportService>();
        services.AddScoped<NotificationService>();
        services.AddScoped<ConnectorService>();
        services.AddScoped<ChatService>();
        services.AddScoped<AccountingService>();
        services.AddScoped<ExpenseService>();
        services.AddScoped<InventoryService>();
        services.AddScoped<FiscalService>();
        services.AddScoped<PeriodClosingClient>();
        services.AddScoped<ReconciliationClient>();
        services.AddScoped<BpoClient>();

        return services;
    }
}
