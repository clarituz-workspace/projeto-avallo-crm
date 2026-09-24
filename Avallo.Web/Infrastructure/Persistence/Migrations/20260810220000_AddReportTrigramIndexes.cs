using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avallo.Web.Infrastructure.Persistence.Migrations;

public partial class AddReportTrigramIndexes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");
        migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_FinancialEntries_Description_Trgm\" ON \"FinancialEntries\" USING gin (\"Description\" gin_trgm_ops);");
        migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_FinancialEntries_ExternalId_Trgm\" ON \"FinancialEntries\" USING gin (\"ExternalId\" gin_trgm_ops);");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_FinancialEntries_Description_Trgm\";");
        migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_FinancialEntries_ExternalId_Trgm\";");
    }
}
