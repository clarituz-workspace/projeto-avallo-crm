using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avallo.Web.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountingLedgerV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "MarketplaceOrders",
                type: "character(3)",
                fixedLength: true,
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeliveredAt",
                table: "MarketplaceOrders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FulfillmentStatus",
                table: "MarketplaceOrders",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "AccountingEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventKey = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    Type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SourceType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SourceId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReversesEntryId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountingEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountingEntries_AccountingEntries_ReversesEntryId",
                        column: x => x.ReversesEntryId,
                        principalTable: "AccountingEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MarketplaceFees",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketplaceOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    SyncedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketplaceFees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MarketplaceFees_MarketplaceOrders_MarketplaceOrderId",
                        column: x => x.MarketplaceOrderId,
                        principalTable: "MarketplaceOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MarketplacePayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketplaceOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    GrossValue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    NetValue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PaymentFee = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Method = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    PaidAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReleaseAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SyncedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketplacePayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MarketplacePayments_MarketplaceOrders_MarketplaceOrderId",
                        column: x => x.MarketplaceOrderId,
                        principalTable: "MarketplaceOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AccountingPostings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountingEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountCode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    AccountName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Marketplace = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    Debit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Credit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountingPostings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountingPostings_AccountingEntries_AccountingEntryId",
                        column: x => x.AccountingEntryId,
                        principalTable: "AccountingEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountingEntries_ReversesEntryId",
                table: "AccountingEntries",
                column: "ReversesEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountingEntries_TenantId_EventKey",
                table: "AccountingEntries",
                columns: new[] { "TenantId", "EventKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountingEntries_TenantId_OccurredAt",
                table: "AccountingEntries",
                columns: new[] { "TenantId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountingPostings_AccountingEntryId",
                table: "AccountingPostings",
                column: "AccountingEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountingPostings_TenantId_AccountCode_AccountingEntryId",
                table: "AccountingPostings",
                columns: new[] { "TenantId", "AccountCode", "AccountingEntryId" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceFees_MarketplaceOrderId",
                table: "MarketplaceFees",
                column: "MarketplaceOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceFees_TenantId_MarketplaceOrderId_ExternalKey",
                table: "MarketplaceFees",
                columns: new[] { "TenantId", "MarketplaceOrderId", "ExternalKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MarketplacePayments_MarketplaceOrderId",
                table: "MarketplacePayments",
                column: "MarketplaceOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketplacePayments_TenantId_MarketplaceOrderId_PaymentId",
                table: "MarketplacePayments",
                columns: new[] { "TenantId", "MarketplaceOrderId", "PaymentId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountingPostings");

            migrationBuilder.DropTable(
                name: "MarketplaceFees");

            migrationBuilder.DropTable(
                name: "MarketplacePayments");

            migrationBuilder.DropTable(
                name: "AccountingEntries");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "MarketplaceOrders");

            migrationBuilder.DropColumn(
                name: "DeliveredAt",
                table: "MarketplaceOrders");

            migrationBuilder.DropColumn(
                name: "FulfillmentStatus",
                table: "MarketplaceOrders");
        }
    }
}
