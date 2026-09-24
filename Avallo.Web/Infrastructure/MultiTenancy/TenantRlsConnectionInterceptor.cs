using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Avallo.Web.Infrastructure;

/// <summary>
/// Publica o tenant corrente na variavel de sessao <c>app.tenant_id</c> a cada abertura de
/// conexao. E ela que as policies de Row Level Security leem no PostgreSQL.
///
/// Quando nao ha tenant no contexto, a variavel vai vazia e a policy nao casa com nenhuma
/// linha: o padrao e negar, nunca liberar. As tabelas de bootstrap de autenticacao
/// (AspNetUsers e RefreshTokens) ficam fora da RLS por necessidade — a identidade e
/// estabelecida antes de existir um tenant — e continuam protegidas pelos filtros do EF.
/// </summary>
public sealed class TenantRlsConnectionInterceptor(ITenantContext tenantContext) : DbConnectionInterceptor
{
    private const string SetTenantSql = "SELECT set_config('app.tenant_id', @tenant, false)";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using (var command = CreateCommand(connection))
            command.ExecuteNonQuery();
        base.ConnectionOpened(connection, eventData);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await using (var command = CreateCommand(connection))
            await command.ExecuteNonQueryAsync(cancellationToken);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    private DbCommand CreateCommand(DbConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = SetTenantSql;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "tenant";
        parameter.Value = tenantContext.TenantId?.ToString() ?? string.Empty;
        command.Parameters.Add(parameter);
        return command;
    }
}

/// <summary>
/// Contexto sem tenant, usado apenas por migrations e ferramentas de design-time.
/// Essas conexoes usam a credencial dona do schema, que nao esta sujeita a RLS.
/// </summary>
public sealed class NullTenantContext : ITenantContext
{
    public Guid? TenantId => null;
}
