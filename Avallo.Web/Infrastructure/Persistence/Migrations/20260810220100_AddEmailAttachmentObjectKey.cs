using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avallo.Web.Infrastructure.Persistence.Migrations;

public partial class AddEmailAttachmentObjectKey : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<string>(
            name: "AttachmentObjectKey",
            table: "EmailOutbox",
            type: "character varying(500)",
            maxLength: 500,
            nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "AttachmentObjectKey", table: "EmailOutbox");
}
