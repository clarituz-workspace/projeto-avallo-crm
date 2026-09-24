using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avallo.Web.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFinancialReconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReconciliationImports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    ObjectKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    AccountReference = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    ImportedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImportedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReconciliationImports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReconciliationImports_AspNetUsers_ImportedByUserId",
                        column: x => x.ImportedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReconciliationImports_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReconciliationTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReconciliationImportId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Reference = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ReviewNote = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReconciliationTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReconciliationTransactions_AspNetUsers_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReconciliationTransactions_ReconciliationImports_Reconcilia~",
                        column: x => x.ReconciliationImportId,
                        principalTable: "ReconciliationImports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReconciliationAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReconciliationTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketplacePaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    MatchMethod = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ConfirmedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AccountingEntryId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReconciliationAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReconciliationAllocations_AccountingEntries_AccountingEntry~",
                        column: x => x.AccountingEntryId,
                        principalTable: "AccountingEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReconciliationAllocations_AspNetUsers_ConfirmedByUserId",
                        column: x => x.ConfirmedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReconciliationAllocations_MarketplacePayments_MarketplacePa~",
                        column: x => x.MarketplacePaymentId,
                        principalTable: "MarketplacePayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReconciliationAllocations_ReconciliationTransactions_Reconc~",
                        column: x => x.ReconciliationTransactionId,
                        principalTable: "ReconciliationTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationAllocations_AccountingEntryId",
                table: "ReconciliationAllocations",
                column: "AccountingEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationAllocations_ConfirmedByUserId",
                table: "ReconciliationAllocations",
                column: "ConfirmedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationAllocations_MarketplacePaymentId",
                table: "ReconciliationAllocations",
                column: "MarketplacePaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationAllocations_ReconciliationTransactionId",
                table: "ReconciliationAllocations",
                column: "ReconciliationTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationAllocations_TenantId_AccountingEntryId",
                table: "ReconciliationAllocations",
                columns: new[] { "TenantId", "AccountingEntryId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationAllocations_TenantId_MarketplacePaymentId",
                table: "ReconciliationAllocations",
                columns: new[] { "TenantId", "MarketplacePaymentId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationAllocations_TenantId_ReconciliationTransactio~",
                table: "ReconciliationAllocations",
                columns: new[] { "TenantId", "ReconciliationTransactionId", "MarketplacePaymentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationImports_ImportedByUserId",
                table: "ReconciliationImports",
                column: "ImportedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationImports_TenantId_ImportedAt",
                table: "ReconciliationImports",
                columns: new[] { "TenantId", "ImportedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationImports_TenantId_PeriodStart_PeriodEnd",
                table: "ReconciliationImports",
                columns: new[] { "TenantId", "PeriodStart", "PeriodEnd" });

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationImports_TenantId_Sha256",
                table: "ReconciliationImports",
                columns: new[] { "TenantId", "Sha256" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationTransactions_ReconciliationImportId",
                table: "ReconciliationTransactions",
                column: "ReconciliationImportId");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationTransactions_ReviewedByUserId",
                table: "ReconciliationTransactions",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationTransactions_TenantId_Amount_OccurredAt",
                table: "ReconciliationTransactions",
                columns: new[] { "TenantId", "Amount", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationTransactions_TenantId_OccurredAt_Status",
                table: "ReconciliationTransactions",
                columns: new[] { "TenantId", "OccurredAt", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationTransactions_TenantId_ReconciliationImportId_~",
                table: "ReconciliationTransactions",
                columns: new[] { "TenantId", "ReconciliationImportId", "ExternalId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReconciliationAllocations");

            migrationBuilder.DropTable(
                name: "ReconciliationTransactions");

            migrationBuilder.DropTable(
                name: "ReconciliationImports");
        }
    }
}
