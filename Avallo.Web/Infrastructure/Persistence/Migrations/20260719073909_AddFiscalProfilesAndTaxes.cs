using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avallo.Web.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFiscalProfilesAndTaxes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TaxProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    EffectiveFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EffectiveTo = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Cnpj = table.Column<string>(type: "character(14)", fixedLength: true, maxLength: 14, nullable: false),
                    LegalName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    TradeName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    RegistrationStatus = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CompanySize = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    AddressSummary = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    MainCnaeCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    MainCnaeDescription = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    TaxRegime = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    SourceLookedUpAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaxProfiles_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TaxReconciliationIssues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketplaceOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventKey = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    Type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Details = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxReconciliationIssues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaxReconciliationIssues_MarketplaceOrders_MarketplaceOrderId",
                        column: x => x.MarketplaceOrderId,
                        principalTable: "MarketplaceOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TaxProfileSecondaryCnaes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaxProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxProfileSecondaryCnaes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaxProfileSecondaryCnaes_TaxProfiles_TaxProfileId",
                        column: x => x.TaxProfileId,
                        principalTable: "TaxProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TaxRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaxProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    EffectiveFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EffectiveTo = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TaxCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    TaxName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Rate = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReviewNotes = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaxRules_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaxRules_AspNetUsers_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaxRules_TaxProfiles_TaxProfileId",
                        column: x => x.TaxProfileId,
                        principalTable: "TaxProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TaxAssessments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketplaceOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaxRuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    TaxableBase = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Rate = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    AssessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReversesAssessmentId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxAssessments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaxAssessments_MarketplaceOrders_MarketplaceOrderId",
                        column: x => x.MarketplaceOrderId,
                        principalTable: "MarketplaceOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaxAssessments_TaxAssessments_ReversesAssessmentId",
                        column: x => x.ReversesAssessmentId,
                        principalTable: "TaxAssessments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaxAssessments_TaxRules_TaxRuleId",
                        column: x => x.TaxRuleId,
                        principalTable: "TaxRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaxAssessments_MarketplaceOrderId",
                table: "TaxAssessments",
                column: "MarketplaceOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_TaxAssessments_ReversesAssessmentId",
                table: "TaxAssessments",
                column: "ReversesAssessmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaxAssessments_TaxRuleId",
                table: "TaxAssessments",
                column: "TaxRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_TaxAssessments_TenantId_AssessedAt",
                table: "TaxAssessments",
                columns: new[] { "TenantId", "AssessedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TaxAssessments_TenantId_MarketplaceOrderId_TaxRuleId_Type",
                table: "TaxAssessments",
                columns: new[] { "TenantId", "MarketplaceOrderId", "TaxRuleId", "Type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaxProfiles_TenantId_Cnpj_Version",
                table: "TaxProfiles",
                columns: new[] { "TenantId", "Cnpj", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaxProfiles_TenantId_EffectiveFrom_EffectiveTo",
                table: "TaxProfiles",
                columns: new[] { "TenantId", "EffectiveFrom", "EffectiveTo" });

            migrationBuilder.CreateIndex(
                name: "IX_TaxProfileSecondaryCnaes_TaxProfileId",
                table: "TaxProfileSecondaryCnaes",
                column: "TaxProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_TaxProfileSecondaryCnaes_TenantId_TaxProfileId_Code",
                table: "TaxProfileSecondaryCnaes",
                columns: new[] { "TenantId", "TaxProfileId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaxReconciliationIssues_MarketplaceOrderId",
                table: "TaxReconciliationIssues",
                column: "MarketplaceOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_TaxReconciliationIssues_TenantId_EventKey",
                table: "TaxReconciliationIssues",
                columns: new[] { "TenantId", "EventKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaxReconciliationIssues_TenantId_ResolvedAt_CreatedAt",
                table: "TaxReconciliationIssues",
                columns: new[] { "TenantId", "ResolvedAt", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TaxRules_CreatedByUserId",
                table: "TaxRules",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TaxRules_ReviewedByUserId",
                table: "TaxRules",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TaxRules_TaxProfileId",
                table: "TaxRules",
                column: "TaxProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_TaxRules_TenantId_Status_EffectiveFrom_EffectiveTo",
                table: "TaxRules",
                columns: new[] { "TenantId", "Status", "EffectiveFrom", "EffectiveTo" });

            migrationBuilder.CreateIndex(
                name: "IX_TaxRules_TenantId_TaxProfileId_TaxCode_Version",
                table: "TaxRules",
                columns: new[] { "TenantId", "TaxProfileId", "TaxCode", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TaxAssessments");

            migrationBuilder.DropTable(
                name: "TaxProfileSecondaryCnaes");

            migrationBuilder.DropTable(
                name: "TaxReconciliationIssues");

            migrationBuilder.DropTable(
                name: "TaxRules");

            migrationBuilder.DropTable(
                name: "TaxProfiles");
        }
    }
}
