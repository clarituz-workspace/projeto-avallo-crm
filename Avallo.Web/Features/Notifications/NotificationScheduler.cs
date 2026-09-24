using System.Globalization;
using System.Text.Encodings.Web;
using Microsoft.EntityFrameworkCore;
using Avallo.Web.Domain;
using Avallo.Web.Features.Reports;
using Avallo.Web.Infrastructure;

namespace Avallo.Web.Features.Notifications;

public sealed class NotificationScheduler(
    AppDbContext db,
    ITenantContext tenantContext,
    NotificationDispatchService dispatch,
    ReportExportService reportExport,
    TimeProvider timeProvider)
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await ScheduleMonthlyCloseAsync(cancellationToken);
        await ScheduleMarketplaceReleasesAsync(cancellationToken);
        await ScheduleWeeklyAccountantReportAsync(cancellationToken);
    }

    public async Task QueueNewSaleAsync(FinancialEntry entry, CancellationToken cancellationToken = default)
    {
        var users = await GetUsersInRolesAsync([Roles.Admin, Roles.Seller], cancellationToken);
        var preferences = await GetPreferencesAsync(users.Select(x => x.Id), cancellationToken);
        foreach (var user in users.Where(x => preferences.GetValueOrDefault(x.Id)?.NewSaleNotification == true))
        {
            var title = $"Nova venda em {entry.Marketplace}";
            var message = $"{entry.Description} no valor de {Money(entry.GrossAmount)}.";
            await dispatch.QueueAsync(user.Id, user.Email, NotificationTypes.NewSale,
                $"sale:{entry.Marketplace}:{entry.ExternalId}", title, message,
                EmailLayout(title, message), sendEmail: true, link: "/dashboard", cancellationToken: cancellationToken);
        }
    }

    private async Task ScheduleMonthlyCloseAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var monthEnd = new DateOnly(now.Year, now.Month, 1).AddDays(-1);
        var monthStart = new DateOnly(monthEnd.Year, monthEnd.Month, 1);
        var from = new DateTimeOffset(monthStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var to = new DateTimeOffset(monthEnd.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var totals = await db.FinancialEntries.Where(x => x.OccurredAt >= from && x.OccurredAt < to)
            .GroupBy(_ => 1).Select(group => new
            {
                Billed = group.Sum(x => x.GrossAmount),
                Received = group.Sum(x => x.ReceivedAmount),
                Fees = group.Sum(x => x.FeeAmount)
            }).SingleOrDefaultAsync(cancellationToken);
        var users = await GetUsersInRolesAsync([Roles.Admin, Roles.Seller], cancellationToken);
        var preferences = await GetPreferencesAsync(users.Select(x => x.Id), cancellationToken);
        var label = monthStart.ToString("MMMM 'de' yyyy", PtBr);
        foreach (var user in users)
        {
            var preference = preferences.GetValueOrDefault(user.Id);
            var title = $"Fechamento mensal - {label}";
            var message = totals is null
                ? "O periodo foi encerrado sem lancamentos financeiros."
                : $"Faturado {Money(totals.Billed)}, recebido {Money(totals.Received)} e taxas de {Money(totals.Fees)}.";
            await dispatch.QueueAsync(user.Id, user.Email, NotificationTypes.MonthlyClose,
                $"monthly:{monthStart:yyyy-MM}", title, message, EmailLayout(title, message),
                sendEmail: preference?.MonthlyCloseEmail ?? true, link: "/dashboard", cancellationToken: cancellationToken);
        }
    }

    /// <summary>
    /// Alerta de repasse proximo da liberacao. Vale para qualquer marketplace conectado:
    /// o criterio e financeiro (existe saldo a receber com data prevista dentro da janela),
    /// nunca o nome da plataforma. Um conector novo passa a alertar sem alterar este codigo.
    /// </summary>
    private async Task ScheduleMarketplaceReleasesAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var users = await GetUsersInRolesAsync([Roles.Admin, Roles.Seller], cancellationToken);
        var preferences = await GetPreferencesAsync(users.Select(x => x.Id), cancellationToken);
        foreach (var user in users)
        {
            var preference = preferences.GetValueOrDefault(user.Id);
            if (preference is { MarketplaceReleaseAlert: false })
                continue;
            var alertDays = preference?.MarketplaceReleaseAlertDays ?? 2;
            var limit = now.AddDays(alertDays);
            var entries = await db.FinancialEntries
                .Where(x => x.ExpectedAt >= now && x.ExpectedAt <= limit &&
                            x.ReceivedAmount < x.GrossAmount - x.FeeAmount)
                .ToListAsync(cancellationToken);
            foreach (var entry in entries)
            {
                var title = $"Repasse de {entry.Marketplace} proximo da liberacao";
                var message = $"{Money(entry.Receivable())} da venda #{entry.ExternalId} tem previsao para {entry.ExpectedAt:dd/MM/yyyy}.";
                await dispatch.QueueAsync(user.Id, user.Email, NotificationTypes.MarketplaceRelease,
                    $"marketplace-release:{entry.Id}:{entry.ExpectedAt:yyyyMMdd}", title, message,
                    EmailLayout(title, message), sendEmail: true, link: "/dashboard", cancellationToken: cancellationToken);
            }
        }
    }

    private async Task ScheduleWeeklyAccountantReportAsync(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var daysSinceMonday = ((int)today.DayOfWeek + 6) % 7;
        var thisMonday = today.AddDays(-daysSinceMonday);
        var previousMonday = thisMonday.AddDays(-7);
        var users = await GetUsersInRolesAsync([Roles.Accountant], cancellationToken);
        var preferences = await GetPreferencesAsync(users.Select(x => x.Id), cancellationToken);
        if (users.All(x => preferences.GetValueOrDefault(x.Id) is { WeeklyAccountantReport: false }))
            return;

        var report = await reportExport.ExportAsync(new ExportReportRequest
        {
            Format = "pdf",
            Mode = "consolidated",
            From = previousMonday,
            To = thisMonday.AddDays(-1)
        }, cancellationToken);
        var attachment = new ExportedAttachment(report.FileName, report.ContentType, report.Content);
        foreach (var user in users)
        {
            if (preferences.GetValueOrDefault(user.Id) is { WeeklyAccountantReport: false })
                continue;
            var title = $"Relatorio semanal - {previousMonday:dd/MM} a {thisMonday.AddDays(-1):dd/MM}";
            var message = "O relatorio consolidado da semana esta anexado e pronto para conferencia.";
            await dispatch.QueueAsync(user.Id, user.Email, NotificationTypes.WeeklyAccountantReport,
                $"weekly-accountant:{previousMonday:yyyy-MM-dd}", title, message,
                EmailLayout(title, message), sendEmail: true, attachment: attachment, cancellationToken: cancellationToken);
        }
    }

    private async Task<List<Recipient>> GetUsersInRolesAsync(string[] roles, CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.TenantId!.Value;
        return await (from user in db.Users
            join userRole in db.UserRoles on user.Id equals userRole.UserId
            join role in db.Roles on userRole.RoleId equals role.Id
            where user.TenantId == tenantId && user.IsActive && roles.Contains(role.Name!)
            select new Recipient(user.Id, user.Email!)).Distinct().ToListAsync(cancellationToken);
    }

    private async Task<Dictionary<Guid, NotificationPreference>> GetPreferencesAsync(
        IEnumerable<Guid> userIds,
        CancellationToken cancellationToken)
    {
        var ids = userIds.ToArray();
        return await db.NotificationPreferences.Where(x => ids.Contains(x.UserId))
            .ToDictionaryAsync(x => x.UserId, cancellationToken);
    }

    private static string EmailLayout(string title, string message) => $"""
        <!doctype html><html><body style="margin:0;background:#f3f3f2;font-family:Arial,sans-serif;color:#181818">
        <div style="max-width:620px;margin:32px auto;background:white;border:1px solid #d6d6d6;border-radius:12px;overflow:hidden">
                    <div style="background:#26343b;color:white;padding:24px 28px;font-size:20px;font-weight:bold">Avallo</div>
        <div style="padding:30px 28px"><h1 style="font-size:22px;margin:0 0 14px">{HtmlEncoder.Default.Encode(title)}</h1><p style="line-height:1.6;color:#555555">{HtmlEncoder.Default.Encode(message)}</p>
        <p style="margin-top:26px;font-size:12px;color:#777777">Mensagem automatica do seu workspace seguro.</p></div></div></body></html>
        """;

    private static string Money(decimal value) => value.ToString("C", PtBr);
    private sealed record Recipient(Guid Id, string Email);
}

internal static class FinancialEntryNotificationExtensions
{
    public static decimal Receivable(this FinancialEntry entry) =>
        entry.GrossAmount - entry.FeeAmount - entry.ReceivedAmount;
}
