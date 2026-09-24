using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avallo.Web.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddConnectorLayer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MarketplaceConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectorName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ExternalAccountId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    AccountDisplayName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    EncryptedAccessToken = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    EncryptedRefreshToken = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    TokenExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    StatusMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    LastStatusCheckAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastSyncAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketplaceConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MarketplaceConnections_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MarketplaceOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Platform = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    SaleDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    GrossValue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PlatformFee = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    NetValue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PaymentMethod = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    PaymentDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReleaseDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    BuyerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    InvoiceNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SyncedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketplaceOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MarketplaceOrders_MarketplaceConnections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalTable: "MarketplaceConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MarketplaceOrderItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketplaceOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sku = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    UnitValue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketplaceOrderItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MarketplaceOrderItems_MarketplaceOrders_MarketplaceOrderId",
                        column: x => x.MarketplaceOrderId,
                        principalTable: "MarketplaceOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceConnections_TenantId_ConnectorName_ExternalAccou~",
                table: "MarketplaceConnections",
                columns: new[] { "TenantId", "ConnectorName", "ExternalAccountId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceConnections_TenantId_Status",
                table: "MarketplaceConnections",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceOrderItems_MarketplaceOrderId",
                table: "MarketplaceOrderItems",
                column: "MarketplaceOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceOrderItems_TenantId_MarketplaceOrderId",
                table: "MarketplaceOrderItems",
                columns: new[] { "TenantId", "MarketplaceOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceOrders_ConnectionId",
                table: "MarketplaceOrders",
                column: "ConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceOrders_TenantId_Platform_OrderId",
                table: "MarketplaceOrders",
                columns: new[] { "TenantId", "Platform", "OrderId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceOrders_TenantId_SaleDate",
                table: "MarketplaceOrders",
                columns: new[] { "TenantId", "SaleDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MarketplaceOrderItems");

            migrationBuilder.DropTable(
                name: "MarketplaceOrders");

            migrationBuilder.DropTable(
                name: "MarketplaceConnections");
        }
    }
}
