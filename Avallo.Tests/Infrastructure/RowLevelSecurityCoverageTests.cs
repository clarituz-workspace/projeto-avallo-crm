using Microsoft.EntityFrameworkCore;
using Avallo.Web.Domain;
using Avallo.Web.Infrastructure;
using Avallo.Web.Infrastructure.Persistence.Migrations;
using Xunit;

namespace Avallo.Tests.Infrastructure;

/// <summary>
/// A secao 02 do documento de arquitetura exige isolamento por tenant no banco. Estes testes
/// nao precisam de PostgreSQL: comparam o modelo do EF com a lista de tabelas da migration,
/// de modo que uma entidade ITenantEntity nova nao possa entrar sem policy de RLS.
/// </summary>
public sealed class RowLevelSecurityCoverageTests
{
    [Fact]
    public void Every_tenant_table_is_covered_by_row_level_security()
    {
        var covered = EnableRowLevelSecurity.ProtectedTables
            .Concat(EnableRowLevelSecurity.AuthenticationBootstrapTables)
            .ToHashSet(StringComparer.Ordinal);

        var missing = TenantTables().Where(x => !covered.Contains(x)).OrderBy(x => x).ToArray();

        Assert.True(missing.Length == 0,
            "Tabela(s) com TenantId sem policy de RLS: " + string.Join(", ", missing) +
            ". Inclua em EnableRowLevelSecurity.ProtectedTables e crie uma migration que aplique a policy.");
    }

    [Fact]
    public void Row_level_security_list_has_no_table_that_left_the_model()
    {
        var tenantTables = TenantTables();
        var stale = EnableRowLevelSecurity.ProtectedTables
            .Where(x => !tenantTables.Contains(x)).OrderBy(x => x).ToArray();

        Assert.True(stale.Length == 0,
            "EnableRowLevelSecurity.ProtectedTables cita tabela(s) que nao existem mais no modelo: " +
            string.Join(", ", stale));
    }

    [Fact]
    public void Authentication_bootstrap_tables_stay_out_of_row_level_security()
    {
        // AspNetUsers e RefreshTokens sao consultadas antes de existir tenant (login e refresh).
        // Se entrarem na RLS, a autenticacao para de funcionar.
        foreach (var table in EnableRowLevelSecurity.AuthenticationBootstrapTables)
            Assert.DoesNotContain(table, EnableRowLevelSecurity.ProtectedTables);
    }

    private static HashSet<string> TenantTables()
    {
        // Construir o modelo nao abre conexao; a string existe so para o provider resolver o mapeamento.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=Avallo_model_only")
            .Options;
        using var db = new AppDbContext(options, new NullTenantContext());

        return db.Model.GetEntityTypes()
            .Where(x => typeof(ITenantEntity).IsAssignableFrom(x.ClrType))
            .Select(x => x.GetTableName())
            .Where(x => !string.IsNullOrEmpty(x))
            .Select(x => x!)
            .ToHashSet(StringComparer.Ordinal);
    }
}
