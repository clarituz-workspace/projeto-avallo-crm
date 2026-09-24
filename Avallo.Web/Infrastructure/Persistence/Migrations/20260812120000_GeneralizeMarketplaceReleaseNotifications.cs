using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avallo.Web.Infrastructure.Persistence.Migrations;

/// <summary>
/// O alerta de liberacao deixa de ser exclusivo do Mercado Livre e passa a valer para
/// qualquer marketplace conectado.
///
/// As colunas sao renomeadas (nao recriadas) para preservar a preferencia ja escolhida
/// por cada usuario, e as linhas existentes de notificacao e de outbox sao reescritas
/// para o novo tipo e para a nova chave de evento. Sem essa reescrita, os alertas ja
/// enviados perderiam a deduplicacao e seriam reenviados uma vez.
/// </summary>
public partial class GeneralizeMarketplaceReleaseNotifications : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameColumn(
            name: "MercadoLivreReleaseAlert",
            table: "NotificationPreferences",
            newName: "MarketplaceReleaseAlert");

        migrationBuilder.RenameColumn(
            name: "MercadoLivreAlertDays",
            table: "NotificationPreferences",
            newName: "MarketplaceReleaseAlertDays");

        migrationBuilder.Sql("""
            UPDATE "Notifications"
            SET "Type" = 'MarketplaceRelease'
            WHERE "Type" = 'MercadoLivreRelease';
            """);

        // 'ml-release:' tem 11 caracteres; o restante da chave (id + data) e preservado.
        migrationBuilder.Sql("""
            UPDATE "Notifications"
            SET "EventKey" = 'marketplace-release:' || substring("EventKey" from 12)
            WHERE "EventKey" LIKE 'ml-release:%';
            """);

        migrationBuilder.Sql("""
            UPDATE "EmailOutbox"
            SET "EventKey" = 'marketplace-release:' || substring("EventKey" from 12)
            WHERE "EventKey" LIKE 'ml-release:%';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // 'marketplace-release:' tem 20 caracteres.
        migrationBuilder.Sql("""
            UPDATE "EmailOutbox"
            SET "EventKey" = 'ml-release:' || substring("EventKey" from 21)
            WHERE "EventKey" LIKE 'marketplace-release:%';
            """);

        migrationBuilder.Sql("""
            UPDATE "Notifications"
            SET "EventKey" = 'ml-release:' || substring("EventKey" from 21)
            WHERE "EventKey" LIKE 'marketplace-release:%';
            """);

        migrationBuilder.Sql("""
            UPDATE "Notifications"
            SET "Type" = 'MercadoLivreRelease'
            WHERE "Type" = 'MarketplaceRelease';
            """);

        migrationBuilder.RenameColumn(
            name: "MarketplaceReleaseAlertDays",
            table: "NotificationPreferences",
            newName: "MercadoLivreAlertDays");

        migrationBuilder.RenameColumn(
            name: "MarketplaceReleaseAlert",
            table: "NotificationPreferences",
            newName: "MercadoLivreReleaseAlert");
    }
}
