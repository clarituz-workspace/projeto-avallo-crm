using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avallo.Web.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddImmutableAccountingPeriods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccountingPeriods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Month = table.Column<int>(type: "integer", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    ValidatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ValidatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ClosedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReopenedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReopenedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReopenReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountingPeriods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountingPeriods_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AccountingPeriodChecks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountingPeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    ValidationRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Passed = table.Column<bool>(type: "boolean", nullable: false),
                    BlockerCount = table.Column<int>(type: "integer", nullable: false),
                    BlockerDetails = table.Column<string>(type: "jsonb", nullable: false),
                    CheckedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountingPeriodChecks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountingPeriodChecks_AccountingPeriods_AccountingPeriodId",
                        column: x => x.AccountingPeriodId,
                        principalTable: "AccountingPeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DreSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountingPeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    CanonicalJson = table.Column<string>(type: "jsonb", nullable: false),
                    CanonicalJsonSha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    PdfObjectKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    PdfSha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    GeneratedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    GrossRevenue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Deductions = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Taxes = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    NetRevenue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Cmv = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    GrossProfit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    SellingExpense = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OperatingExpense = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Result = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DreSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DreSnapshots_AccountingPeriods_AccountingPeriodId",
                        column: x => x.AccountingPeriodId,
                        principalTable: "AccountingPeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountingPeriodChecks_AccountingPeriodId",
                table: "AccountingPeriodChecks",
                column: "AccountingPeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountingPeriodChecks_TenantId_AccountingPeriodId_CheckedAt",
                table: "AccountingPeriodChecks",
                columns: new[] { "TenantId", "AccountingPeriodId", "CheckedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountingPeriodChecks_TenantId_AccountingPeriodId_Validati~",
                table: "AccountingPeriodChecks",
                columns: new[] { "TenantId", "AccountingPeriodId", "ValidationRunId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountingPeriods_TenantId_Status",
                table: "AccountingPeriods",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountingPeriods_TenantId_Year_Month",
                table: "AccountingPeriods",
                columns: new[] { "TenantId", "Year", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DreSnapshots_AccountingPeriodId",
                table: "DreSnapshots",
                column: "AccountingPeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_DreSnapshots_TenantId_AccountingPeriodId_Revision",
                table: "DreSnapshots",
                columns: new[] { "TenantId", "AccountingPeriodId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DreSnapshots_TenantId_PdfObjectKey",
                table: "DreSnapshots",
                columns: new[] { "TenantId", "PdfObjectKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountingPeriodChecks");

            migrationBuilder.DropTable(
                name: "DreSnapshots");

            migrationBuilder.DropTable(
                name: "AccountingPeriods");
        }
    }
}
