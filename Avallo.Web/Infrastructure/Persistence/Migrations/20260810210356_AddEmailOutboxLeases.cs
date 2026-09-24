using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avallo.Web.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailOutboxLeases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LeaseId",
                table: "EmailOutbox",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LeaseUntil",
                table: "EmailOutbox",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LeaseId",
                table: "EmailOutbox");

            migrationBuilder.DropColumn(
                name: "LeaseUntil",
                table: "EmailOutbox");
        }
    }
}
