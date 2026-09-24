using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avallo.Web.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryAndCogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InventoryItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sku = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    QuantityOnHand = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    AverageUnitCost = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryItems_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InventoryReconciliationIssues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventKey = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    Type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    MarketplaceOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketplaceOrderItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Details = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryReconciliationIssues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryReconciliationIssues_MarketplaceOrderItems_Marketp~",
                        column: x => x.MarketplaceOrderItemId,
                        principalTable: "MarketplaceOrderItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InventoryReconciliationIssues_MarketplaceOrders_Marketplace~",
                        column: x => x.MarketplaceOrderId,
                        principalTable: "MarketplaceOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SupplierInvoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccessKey = table.Column<string>(type: "character(44)", fixedLength: true, maxLength: 44, nullable: false),
                    InvoiceNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Series = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SupplierTaxId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SupplierName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    XmlObjectKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    XmlSha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: true),
                    ImportedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierInvoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierInvoices_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MarketplaceSkuMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Platform = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ExternalSku = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    InventoryItemId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketplaceSkuMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MarketplaceSkuMappings_InventoryItems_InventoryItemId",
                        column: x => x.InventoryItemId,
                        principalTable: "InventoryItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplierInvoiceItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierInvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    InventoryItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierSku = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Barcode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitCost = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    Total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierInvoiceItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierInvoiceItems_InventoryItems_InventoryItemId",
                        column: x => x.InventoryItemId,
                        principalTable: "InventoryItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierInvoiceItems_SupplierInvoices_SupplierInvoiceId",
                        column: x => x.SupplierInvoiceId,
                        principalTable: "SupplierInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InventoryMovements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    InventoryItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitCost = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    Total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    EventKey = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SupplierInvoiceItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    MarketplaceOrderItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReversesMovementId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryMovements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryMovements_InventoryItems_InventoryItemId",
                        column: x => x.InventoryItemId,
                        principalTable: "InventoryItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryMovements_InventoryMovements_ReversesMovementId",
                        column: x => x.ReversesMovementId,
                        principalTable: "InventoryMovements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryMovements_MarketplaceOrderItems_MarketplaceOrderIt~",
                        column: x => x.MarketplaceOrderItemId,
                        principalTable: "MarketplaceOrderItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryMovements_SupplierInvoiceItems_SupplierInvoiceItem~",
                        column: x => x.SupplierInvoiceItemId,
                        principalTable: "SupplierInvoiceItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryItems_TenantId_Sku",
                table: "InventoryItems",
                columns: new[] { "TenantId", "Sku" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_InventoryItemId",
                table: "InventoryMovements",
                column: "InventoryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_MarketplaceOrderItemId",
                table: "InventoryMovements",
                column: "MarketplaceOrderItemId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_ReversesMovementId",
                table: "InventoryMovements",
                column: "ReversesMovementId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_SupplierInvoiceItemId",
                table: "InventoryMovements",
                column: "SupplierInvoiceItemId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_TenantId_EventKey",
                table: "InventoryMovements",
                columns: new[] { "TenantId", "EventKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_TenantId_InventoryItemId_OccurredAt",
                table: "InventoryMovements",
                columns: new[] { "TenantId", "InventoryItemId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReconciliationIssues_MarketplaceOrderId",
                table: "InventoryReconciliationIssues",
                column: "MarketplaceOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReconciliationIssues_MarketplaceOrderItemId",
                table: "InventoryReconciliationIssues",
                column: "MarketplaceOrderItemId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReconciliationIssues_TenantId_EventKey",
                table: "InventoryReconciliationIssues",
                columns: new[] { "TenantId", "EventKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReconciliationIssues_TenantId_ResolvedAt_CreatedAt",
                table: "InventoryReconciliationIssues",
                columns: new[] { "TenantId", "ResolvedAt", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceSkuMappings_InventoryItemId",
                table: "MarketplaceSkuMappings",
                column: "InventoryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceSkuMappings_TenantId_InventoryItemId",
                table: "MarketplaceSkuMappings",
                columns: new[] { "TenantId", "InventoryItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceSkuMappings_TenantId_Platform_ExternalSku",
                table: "MarketplaceSkuMappings",
                columns: new[] { "TenantId", "Platform", "ExternalSku" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierInvoiceItems_InventoryItemId",
                table: "SupplierInvoiceItems",
                column: "InventoryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierInvoiceItems_SupplierInvoiceId",
                table: "SupplierInvoiceItems",
                column: "SupplierInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierInvoiceItems_TenantId_InventoryItemId",
                table: "SupplierInvoiceItems",
                columns: new[] { "TenantId", "InventoryItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierInvoiceItems_TenantId_SupplierInvoiceId",
                table: "SupplierInvoiceItems",
                columns: new[] { "TenantId", "SupplierInvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierInvoices_TenantId_AccessKey",
                table: "SupplierInvoices",
                columns: new[] { "TenantId", "AccessKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierInvoices_TenantId_IssuedAt",
                table: "SupplierInvoices",
                columns: new[] { "TenantId", "IssuedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InventoryMovements");

            migrationBuilder.DropTable(
                name: "InventoryReconciliationIssues");

            migrationBuilder.DropTable(
                name: "MarketplaceSkuMappings");

            migrationBuilder.DropTable(
                name: "SupplierInvoiceItems");

            migrationBuilder.DropTable(
                name: "InventoryItems");

            migrationBuilder.DropTable(
                name: "SupplierInvoices");
        }
    }
}
