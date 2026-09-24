using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avallo.Web.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationsAndEmailOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmailOutbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Recipient = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    HtmlBody = table.Column<string>(type: "text", nullable: false),
                    AttachmentName = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    AttachmentContentType = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    AttachmentContent = table.Column<byte[]>(type: "bytea", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailOutbox", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailOutbox_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NotificationPreferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    MonthlyCloseEmail = table.Column<bool>(type: "boolean", nullable: false),
                    MercadoLivreReleaseAlert = table.Column<bool>(type: "boolean", nullable: false),
                    NewSaleNotification = table.Column<bool>(type: "boolean", nullable: false),
                    WeeklyAccountantReport = table.Column<bool>(type: "boolean", nullable: false),
                    MercadoLivreAlertDays = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationPreferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationPreferences_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    EventKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Title = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    Message = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    Link = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    IsRead = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReadAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notifications_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmailOutbox_TenantId_SentAt_NextAttemptAt",
                table: "EmailOutbox",
                columns: new[] { "TenantId", "SentAt", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_EmailOutbox_TenantId_UserId_EventKey",
                table: "EmailOutbox",
                columns: new[] { "TenantId", "UserId", "EventKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailOutbox_UserId",
                table: "EmailOutbox",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationPreferences_TenantId_UserId",
                table: "NotificationPreferences",
                columns: new[] { "TenantId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationPreferences_UserId",
                table: "NotificationPreferences",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_TenantId_UserId_EventKey",
                table: "Notifications",
                columns: new[] { "TenantId", "UserId", "EventKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_TenantId_UserId_IsRead_CreatedAt",
                table: "Notifications",
                columns: new[] { "TenantId", "UserId", "IsRead", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId",
                table: "Notifications",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmailOutbox");

            migrationBuilder.DropTable(
                name: "NotificationPreferences");

            migrationBuilder.DropTable(
                name: "Notifications");
        }
    }
}
