using Avallo.Web.Features.Accounting;
using Avallo.Web.Features.Auth;
using Avallo.Web.Features.Bpo;
using Avallo.Web.Features.Fiscal;
using Avallo.Web.Features.Inventory;
using Avallo.Web.Features.PeriodClosing;
using Avallo.Web.Features.Reconciliation;
using Avallo.Web.Features.Reports;

namespace Avallo.Web.Application;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<TokenService>();
        services.AddScoped<UserAccessService>();
        services.AddScoped<BpoOperationsService>();
        services.AddScoped<ReportExportService>();
        services.AddSingleton<IReportExportEngine, ReportExportEngine>();
        services.AddScoped<AccountingEngine>();
        services.AddScoped<LegalAccountingService>();
        services.AddScoped<InventoryCostService>();
        services.AddScoped<TaxEngine>();
        services.AddScoped<PeriodClosingService>();
        services.AddScoped<ReconciliationService>();

        return services;
    }
}
