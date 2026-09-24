using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avallo.Web.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBpoTenantAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BpoTenantAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperatorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetTenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BpoTenantAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BpoTenantAssignments_AspNetUsers_AssignedByUserId",
                        column: x => x.AssignedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BpoTenantAssignments_AspNetUsers_OperatorUserId",
                        column: x => x.OperatorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BpoTenantAssignments_Tenants_TargetTenantId",
                        column: x => x.TargetTenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BpoTenantAssignments_AssignedByUserId",
                table: "BpoTenantAssignments",
                column: "AssignedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BpoTenantAssignments_OperatorUserId",
                table: "BpoTenantAssignments",
                column: "OperatorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BpoTenantAssignments_TargetTenantId",
                table: "BpoTenantAssignments",
                column: "TargetTenantId");

            migrationBuilder.CreateIndex(
                name: "IX_BpoTenantAssignments_TenantId_OperatorUserId_RevokedAt",
                table: "BpoTenantAssignments",
                columns: new[] { "TenantId", "OperatorUserId", "RevokedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BpoTenantAssignments_TenantId_OperatorUserId_TargetTenantId",
                table: "BpoTenantAssignments",
                columns: new[] { "TenantId", "OperatorUserId", "TargetTenantId" },
                unique: true);

            migrationBuilder.Sql("""
                ALTER TABLE public."BpoTenantAssignments" ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON public."BpoTenantAssignments"
                    USING ("TenantId" = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK ("TenantId" = nullif(current_setting('app.tenant_id', true), '')::uuid);
                DO $grant$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'Avallo_app') THEN
                        GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE public."BpoTenantAssignments" TO Avallo_app;
                    END IF;
                END
                $grant$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BpoTenantAssignments");
        }
    }
}
