using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avallo.Web.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProfitDistributionAuthorization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProfitDistributionAuthorizations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountingPeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    DreSnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    BeneficiaryName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BeneficiaryTaxId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxTreatment = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    IrpfExemptionConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    LegalBasis = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    AuthorizedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorizedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfitDistributionAuthorizations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProfitDistributionAuthorizations_AccountingPeriods_Accounti~",
                        column: x => x.AccountingPeriodId,
                        principalTable: "AccountingPeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProfitDistributionAuthorizations_AspNetUsers_AuthorizedByUs~",
                        column: x => x.AuthorizedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProfitDistributionAuthorizations_DreSnapshots_DreSnapshotId",
                        column: x => x.DreSnapshotId,
                        principalTable: "DreSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProfitDistributionAuthorizations_AccountingPeriodId",
                table: "ProfitDistributionAuthorizations",
                column: "AccountingPeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_ProfitDistributionAuthorizations_AuthorizedByUserId",
                table: "ProfitDistributionAuthorizations",
                column: "AuthorizedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProfitDistributionAuthorizations_DreSnapshotId",
                table: "ProfitDistributionAuthorizations",
                column: "DreSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_ProfitDistributionAuthorizations_TenantId_AccountingPeriodI~",
                table: "ProfitDistributionAuthorizations",
                columns: new[] { "TenantId", "AccountingPeriodId", "AuthorizedAt" });

            migrationBuilder.Sql("""
                ALTER TABLE public."ProfitDistributionAuthorizations" ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON public."ProfitDistributionAuthorizations"
                    USING ("TenantId" = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK ("TenantId" = nullif(current_setting('app.tenant_id', true), '')::uuid);
                DO $grant$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'Avallo_app') THEN
                        GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE public."ProfitDistributionAuthorizations" TO Avallo_app;
                    END IF;
                END
                $grant$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProfitDistributionAuthorizations");
        }
    }
}
