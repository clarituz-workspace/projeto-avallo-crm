using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avallo.Web.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryItemArchiving : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                table: "InventoryItems",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsArchived",
                table: "InventoryItems");
        }
    }
}
