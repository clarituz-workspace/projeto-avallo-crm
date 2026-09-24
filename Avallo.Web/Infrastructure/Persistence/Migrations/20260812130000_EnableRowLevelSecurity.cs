using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avallo.Web.Infrastructure.Persistence.Migrations;

/// <summary>
/// Isolamento por tenant no proprio banco, conforme a secao 02 do documento de arquitetura.
///
/// Cada tabela com TenantId recebe ENABLE ROW LEVEL SECURITY e uma policy que compara a
/// coluna com a variavel de sessao <c>app.tenant_id</c>, publicada pelo
/// <c>TenantRlsConnectionInterceptor</c>. Variavel ausente ou vazia nao casa com nada:
/// o padrao e negar.
///
/// Nao usamos FORCE ROW LEVEL SECURITY de proposito. O dono do schema (a credencial que
/// roda migrations) precisa continuar enxergando tudo; quem fica sujeito a policy e a
/// credencial de aplicacao, que nao e dona das tabelas. Por isso a aplicacao deve conectar
/// com um role dedicado — ver scripts/sql/create-app-role.sql e o README.
///
/// Enquanto esse role nao existir, esta migration e inofensiva: o GRANT e condicional e a
/// aplicacao, conectando como dona, continua com o comportamento atual (filtros do EF).
///
/// AspNetUsers e RefreshTokens ficam de fora: sao consultadas no login e no refresh, antes
/// de existir um tenant conhecido. Colocá-las sob RLS quebraria a autenticacao. Continuam
/// cobertas pelos filtros globais do EF e, no caso do refresh token, pela entropia de
/// 512 bits do proprio token.
/// </summary>
public partial class EnableRowLevelSecurity : Migration
{
    /// <summary>
    /// Tabelas com TenantId protegidas por RLS. Uma entidade ITenantEntity nova precisa
    /// entrar aqui — o teste <c>Every_tenant_table_is_covered_by_row_level_security</c>
    /// falha se alguem esquecer.
    /// </summary>
    public static readonly string[] ProtectedTables =
    [
        "AccountingEntries",
        "AccountingPeriodChecks",
        "AccountingPeriods",
        "AccountingPostings",
        "BpoTenantAssignments",
        "CustomExpenseCategories",
        "DreSnapshots",
        "EmailOutbox",
        "ExpenseAttachments",
        "Expenses",
        "FinancialEntries",
        "InventoryItems",
        "InventoryMovements",
        "InventoryReconciliationIssues",
        "MarketplaceConnections",
        "MarketplaceFees",
        "MarketplaceOrderItems",
        "MarketplaceOrders",
        "MarketplacePayments",
        "MarketplaceSkuMappings",
        "NotificationPreferences",
        "Notifications",
        "ProfitDistributionAuthorizations",
        "ReconciliationAllocations",
        "ReconciliationImports",
        "ReconciliationTransactions",
        "SupplierInvoiceItems",
        "SupplierInvoices",
        "TaxAssessments",
        "TaxProfileSecondaryCnaes",
        "TaxProfiles",
        "TaxReconciliationIssues",
        "TaxRules"
    ];

    /// <summary>
    /// Fora da RLS por serem o bootstrap da autenticacao: consultadas antes de existir tenant.
    /// </summary>
    public static readonly string[] AuthenticationBootstrapTables =
    [
        "AspNetUsers",
        "RefreshTokens"
    ];

    public const string ApplicationRole = "Avallo_app";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql($"""
            DO $rls$
            DECLARE
                target text;
            BEGIN
                FOREACH target IN ARRAY ARRAY[{TableArray()}] LOOP
                    EXECUTE format('ALTER TABLE public.%I ENABLE ROW LEVEL SECURITY', target);
                    EXECUTE format('DROP POLICY IF EXISTS tenant_isolation ON public.%I', target);
                    EXECUTE format($policy$
                        CREATE POLICY tenant_isolation ON public.%I
                            USING ("TenantId" = nullif(current_setting('app.tenant_id', true), '')::uuid)
                            WITH CHECK ("TenantId" = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    $policy$, target);
                END LOOP;
            END
            $rls$;
            """);

        // O role pode ainda nao existir (producao antes da rotacao da credencial).
        // Sem ele a migration passa e nada muda de comportamento.
        migrationBuilder.Sql($"""
            DO $grants$
            BEGIN
                IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{ApplicationRole}') THEN
                    EXECUTE 'GRANT USAGE ON SCHEMA public TO {ApplicationRole}';
                    EXECUTE 'GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {ApplicationRole}';
                    EXECUTE 'GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO {ApplicationRole}';
                END IF;
            END
            $grants$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql($"""
            DO $rls$
            DECLARE
                target text;
            BEGIN
                FOREACH target IN ARRAY ARRAY[{TableArray()}] LOOP
                    EXECUTE format('DROP POLICY IF EXISTS tenant_isolation ON public.%I', target);
                    EXECUTE format('ALTER TABLE public.%I DISABLE ROW LEVEL SECURITY', target);
                END LOOP;
            END
            $rls$;
            """);

    private static string TableArray() =>
        string.Join(", ", Array.ConvertAll(ProtectedTables, x => $"'{x}'"));
}
