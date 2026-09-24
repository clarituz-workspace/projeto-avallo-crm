using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avallo.Web.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketplaceOrderSyncIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceOrders_TenantId_ConnectionId_OrderId",
                table: "MarketplaceOrders",
                columns: new[] { "TenantId", "ConnectionId", "OrderId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MarketplaceOrders_TenantId_ConnectionId_OrderId",
                table: "MarketplaceOrders");
        }
    }
}
