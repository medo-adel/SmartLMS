using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartLMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CourseReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CourseReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CourseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Rating = table.Column<int>(type: "int", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourseReviews", x => x.Id);
                    table.UniqueConstraint("AK_CourseReviews_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_CourseReviews_Status", "[Status] IN ('Published','Hidden')");
                    table.ForeignKey(
                        name: "FK_CourseReviews_Courses_TenantId_CourseId",
                        columns: x => new { x.TenantId, x.CourseId },
                        principalTable: "Courses",
                        principalColumns: new[] { "TenantId", "Id" });
                    table.ForeignKey(
                        name: "FK_CourseReviews_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CourseReviews_UserTenantMemberships_TenantId_UserId",
                        columns: x => new { x.TenantId, x.UserId },
                        principalTable: "UserTenantMemberships",
                        principalColumns: new[] { "TenantId", "UserId" });
                });

            migrationBuilder.CreateTable(
                name: "CourseReviewReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReviewId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourseReviewReports", x => x.Id);
                    table.CheckConstraint("CK_CourseReviewReports_Status", "[Status] IN ('Open','Dismissed','Actioned')");
                    table.ForeignKey(
                        name: "FK_CourseReviewReports_CourseReviews_TenantId_ReviewId",
                        columns: x => new { x.TenantId, x.ReviewId },
                        principalTable: "CourseReviews",
                        principalColumns: new[] { "TenantId", "Id" });
                    table.ForeignKey(
                        name: "FK_CourseReviewReports_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CourseReviewReports_UserTenantMemberships_TenantId_UserId",
                        columns: x => new { x.TenantId, x.UserId },
                        principalTable: "UserTenantMemberships",
                        principalColumns: new[] { "TenantId", "UserId" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_CourseReviewReports_TenantId_UserId",
                table: "CourseReviewReports",
                columns: new[] { "TenantId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "UX_CourseReviewReports_TenantId_ReviewId_UserId",
                table: "CourseReviewReports",
                columns: new[] { "TenantId", "ReviewId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CourseReviews_TenantId_CourseId",
                table: "CourseReviews",
                columns: new[] { "TenantId", "CourseId" });

            migrationBuilder.CreateIndex(
                name: "UX_CourseReviews_TenantId_UserId_CourseId",
                table: "CourseReviews",
                columns: new[] { "TenantId", "UserId", "CourseId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CourseReviewReports");

            migrationBuilder.DropTable(
                name: "CourseReviews");
        }
    }
}
