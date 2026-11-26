using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartLMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WishlistItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WishlistItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CourseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WishlistItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WishlistItems_Courses_TenantId_CourseId",
                        columns: x => new { x.TenantId, x.CourseId },
                        principalTable: "Courses",
                        principalColumns: new[] { "TenantId", "Id" });
                    table.ForeignKey(
                        name: "FK_WishlistItems_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WishlistItems_UserTenantMemberships_TenantId_UserId",
                        columns: x => new { x.TenantId, x.UserId },
                        principalTable: "UserTenantMemberships",
                        principalColumns: new[] { "TenantId", "UserId" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_WishlistItems_TenantId_CourseId",
                table: "WishlistItems",
                columns: new[] { "TenantId", "CourseId" });

            migrationBuilder.CreateIndex(
                name: "UX_WishlistItems_TenantId_UserId_CourseId",
                table: "WishlistItems",
                columns: new[] { "TenantId", "UserId", "CourseId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WishlistItems");
        }
    }
}
