using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avallo.Web.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketplaceSyncLeases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SyncLeaseId",
                table: "MarketplaceConnections",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SyncLeaseUntil",
                table: "MarketplaceConnections",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SyncLeaseId",
                table: "MarketplaceConnections");

            migrationBuilder.DropColumn(
                name: "SyncLeaseUntil",
                table: "MarketplaceConnections");
        }
    }
}
