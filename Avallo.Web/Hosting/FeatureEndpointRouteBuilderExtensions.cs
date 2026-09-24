using Avallo.Web.Features.Accounting;
using Avallo.Web.Features.AI;
using Avallo.Web.Features.Auth;
using Avallo.Web.Features.Bpo;
using Avallo.Web.Features.Connectors;
using Avallo.Web.Features.Expenses;
using Avallo.Web.Features.Deployment;
using Avallo.Web.Features.Fiscal;
using Avallo.Web.Features.Inventory;
using Avallo.Web.Features.Notifications;
using Avallo.Web.Features.PeriodClosing;
using Avallo.Web.Features.Reconciliation;
using Avallo.Web.Features.Reports;

namespace Avallo.Web.Hosting;

/// <summary>
/// Keeps the host as a composition root. Each business module remains responsible
/// for its own routes instead of exposing endpoint details to Program.cs.
/// </summary>
public static class FeatureEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapBusinessModules(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapAuthEndpoints();
        endpoints.MapBpoEndpoints();
        endpoints.MapChatEndpoints();
        endpoints.MapDeploymentEndpoints();
        endpoints.MapReportEndpoints();
        endpoints.MapNotificationEndpoints();
        endpoints.MapConnectorEndpoints();
        endpoints.MapAccountingEndpoints();
        endpoints.MapExpenseEndpoints();
        endpoints.MapInventoryEndpoints();
        endpoints.MapFiscalEndpoints();
        endpoints.MapPeriodClosingEndpoints();
        endpoints.MapReconciliationEndpoints();

        return endpoints;
    }
}
