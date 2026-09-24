using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Avallo.Web.Infrastructure;

public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        // dotnet ef precisa da credencial dona do schema: o role da aplicacao nao tem DDL.
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__MigrationConnection")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=localhost;Database=Avallo_design;Username=postgres;Password=postgres";
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options;
        return new AppDbContext(options, new NullTenantContext());
    }
}
