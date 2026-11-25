using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartLMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SubscriptionAllocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "SubscriptionJumpAlertPercent",
                table: "Tenants",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SubscriptionMinAvgLessonSeconds",
                table: "Tenants",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "SubscriptionMinWatchPercent",
                table: "Tenants",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MeasuredDurationSeconds",
                table: "FileAssets",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CourseLearningWeights",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CourseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NonVideoCourseSeconds = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourseLearningWeights", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourseLearningWeights_Courses_TenantId_CourseId",
                        columns: x => new { x.TenantId, x.CourseId },
                        principalTable: "Courses",
                        principalColumns: new[] { "TenantId", "Id" });
                    table.ForeignKey(
                        name: "FK_CourseLearningWeights_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OrderItemAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CourseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ListPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    NonVideoSeconds = table.Column<int>(type: "int", nullable: true),
                    NonVideoLessonCount = table.Column<int>(type: "int", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderItemAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderItemAllocations_Courses_TenantId_CourseId",
                        columns: x => new { x.TenantId, x.CourseId },
                        principalTable: "Courses",
                        principalColumns: new[] { "TenantId", "Id" });
                    table.ForeignKey(
                        name: "FK_OrderItemAllocations_OrderItems_TenantId_OrderItemId",
                        columns: x => new { x.TenantId, x.OrderItemId },
                        principalTable: "OrderItems",
                        principalColumns: new[] { "TenantId", "Id" });
                    table.ForeignKey(
                        name: "FK_OrderItemAllocations_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OrderItemShares",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CourseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InstructorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SharePercent = table.Column<double>(type: "float", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderItemShares", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderItemShares_Courses_TenantId_CourseId",
                        columns: x => new { x.TenantId, x.CourseId },
                        principalTable: "Courses",
                        principalColumns: new[] { "TenantId", "Id" });
                    table.ForeignKey(
                        name: "FK_OrderItemShares_OrderItems_TenantId_OrderItemId",
                        columns: x => new { x.TenantId, x.OrderItemId },
                        principalTable: "OrderItems",
                        principalColumns: new[] { "TenantId", "Id" });
                    table.ForeignKey(
                        name: "FK_OrderItemShares_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrderItemShares_UserTenantMemberships_TenantId_InstructorId",
                        columns: x => new { x.TenantId, x.InstructorId },
                        principalTable: "UserTenantMemberships",
                        principalColumns: new[] { "TenantId", "UserId" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_CourseLearningWeights_TenantId_CourseId",
                table: "CourseLearningWeights",
                columns: new[] { "TenantId", "CourseId" });

            migrationBuilder.CreateIndex(
                name: "UX_CourseLearningWeights_CourseId",
                table: "CourseLearningWeights",
                column: "CourseId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderItemAllocations_TenantId_CourseId",
                table: "OrderItemAllocations",
                columns: new[] { "TenantId", "CourseId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderItemAllocations_TenantId_OrderItemId",
                table: "OrderItemAllocations",
                columns: new[] { "TenantId", "OrderItemId" });

            migrationBuilder.CreateIndex(
                name: "UX_OrderItemAllocations_OrderItemId_CourseId",
                table: "OrderItemAllocations",
                columns: new[] { "OrderItemId", "CourseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderItemShares_TenantId_CourseId",
                table: "OrderItemShares",
                columns: new[] { "TenantId", "CourseId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderItemShares_TenantId_InstructorId",
                table: "OrderItemShares",
                columns: new[] { "TenantId", "InstructorId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderItemShares_TenantId_OrderItemId",
                table: "OrderItemShares",
                columns: new[] { "TenantId", "OrderItemId" });

            migrationBuilder.CreateIndex(
                name: "UX_OrderItemShares_OrderItemId_CourseId_InstructorId",
                table: "OrderItemShares",
                columns: new[] { "OrderItemId", "CourseId", "InstructorId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CourseLearningWeights");

            migrationBuilder.DropTable(
                name: "OrderItemAllocations");

            migrationBuilder.DropTable(
                name: "OrderItemShares");

            migrationBuilder.DropColumn(
                name: "SubscriptionJumpAlertPercent",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "SubscriptionMinAvgLessonSeconds",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "SubscriptionMinWatchPercent",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "MeasuredDurationSeconds",
                table: "FileAssets");
        }
    }
}
