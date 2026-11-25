using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartLMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RefundAccounting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ReleasedLate",
                table: "OrderItemShares",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "SettledPayoutId",
                table: "OrderItemShares",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_InstructorPayouts_TenantId_Id",
                table: "InstructorPayouts",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateTable(
                name: "InstructorPayoutLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InstructorPayoutId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CourseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GrossEarned = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PaidEarned = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstructorPayoutLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InstructorPayoutLines_Courses_TenantId_CourseId",
                        columns: x => new { x.TenantId, x.CourseId },
                        principalTable: "Courses",
                        principalColumns: new[] { "TenantId", "Id" });
                    table.ForeignKey(
                        name: "FK_InstructorPayoutLines_InstructorPayouts_TenantId_InstructorPayoutId",
                        columns: x => new { x.TenantId, x.InstructorPayoutId },
                        principalTable: "InstructorPayouts",
                        principalColumns: new[] { "TenantId", "Id" });
                    table.ForeignKey(
                        name: "FK_InstructorPayoutLines_OrderItems_TenantId_OrderItemId",
                        columns: x => new { x.TenantId, x.OrderItemId },
                        principalTable: "OrderItems",
                        principalColumns: new[] { "TenantId", "Id" });
                    table.ForeignKey(
                        name: "FK_InstructorPayoutLines_Orders_TenantId_OrderId",
                        columns: x => new { x.TenantId, x.OrderId },
                        principalTable: "Orders",
                        principalColumns: new[] { "TenantId", "Id" });
                    table.ForeignKey(
                        name: "FK_InstructorPayoutLines_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OrderRefunds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RecordedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderRefunds", x => x.Id);
                    table.UniqueConstraint("AK_OrderRefunds_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_OrderRefunds_Orders_TenantId_OrderId",
                        columns: x => new { x.TenantId, x.OrderId },
                        principalTable: "Orders",
                        principalColumns: new[] { "TenantId", "Id" });
                    table.ForeignKey(
                        name: "FK_OrderRefunds_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrderRefunds_UserTenantMemberships_TenantId_RecordedBy",
                        columns: x => new { x.TenantId, x.RecordedBy },
                        principalTable: "UserTenantMemberships",
                        principalColumns: new[] { "TenantId", "UserId" });
                });

            migrationBuilder.CreateTable(
                name: "InstructorDebits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InstructorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderRefundId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourcePayoutId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    RemainingAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstructorDebits", x => x.Id);
                    table.UniqueConstraint("AK_InstructorDebits_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_InstructorDebits_InstructorPayouts_TenantId_SourcePayoutId",
                        columns: x => new { x.TenantId, x.SourcePayoutId },
                        principalTable: "InstructorPayouts",
                        principalColumns: new[] { "TenantId", "Id" });
                    table.ForeignKey(
                        name: "FK_InstructorDebits_OrderRefunds_TenantId_OrderRefundId",
                        columns: x => new { x.TenantId, x.OrderRefundId },
                        principalTable: "OrderRefunds",
                        principalColumns: new[] { "TenantId", "Id" });
                    table.ForeignKey(
                        name: "FK_InstructorDebits_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_InstructorDebits_UserTenantMemberships_TenantId_InstructorId",
                        columns: x => new { x.TenantId, x.InstructorId },
                        principalTable: "UserTenantMemberships",
                        principalColumns: new[] { "TenantId", "UserId" });
                });

            migrationBuilder.CreateTable(
                name: "InstructorDebitApplications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InstructorDebitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InstructorPayoutId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstructorDebitApplications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InstructorDebitApplications_InstructorDebits_TenantId_InstructorDebitId",
                        columns: x => new { x.TenantId, x.InstructorDebitId },
                        principalTable: "InstructorDebits",
                        principalColumns: new[] { "TenantId", "Id" });
                    table.ForeignKey(
                        name: "FK_InstructorDebitApplications_InstructorPayouts_TenantId_InstructorPayoutId",
                        columns: x => new { x.TenantId, x.InstructorPayoutId },
                        principalTable: "InstructorPayouts",
                        principalColumns: new[] { "TenantId", "Id" });
                    table.ForeignKey(
                        name: "FK_InstructorDebitApplications_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderItemShares_TenantId_SettledPayoutId",
                table: "OrderItemShares",
                columns: new[] { "TenantId", "SettledPayoutId" });

            migrationBuilder.CreateIndex(
                name: "IX_InstructorDebitApplications_InstructorPayoutId",
                table: "InstructorDebitApplications",
                column: "InstructorPayoutId");

            migrationBuilder.CreateIndex(
                name: "IX_InstructorDebitApplications_TenantId_InstructorDebitId",
                table: "InstructorDebitApplications",
                columns: new[] { "TenantId", "InstructorDebitId" });

            migrationBuilder.CreateIndex(
                name: "IX_InstructorDebitApplications_TenantId_InstructorPayoutId",
                table: "InstructorDebitApplications",
                columns: new[] { "TenantId", "InstructorPayoutId" });

            migrationBuilder.CreateIndex(
                name: "IX_InstructorDebits_InstructorId_RemainingAmount",
                table: "InstructorDebits",
                columns: new[] { "InstructorId", "RemainingAmount" });

            migrationBuilder.CreateIndex(
                name: "IX_InstructorDebits_TenantId_InstructorId",
                table: "InstructorDebits",
                columns: new[] { "TenantId", "InstructorId" });

            migrationBuilder.CreateIndex(
                name: "IX_InstructorDebits_TenantId_OrderRefundId",
                table: "InstructorDebits",
                columns: new[] { "TenantId", "OrderRefundId" });

            migrationBuilder.CreateIndex(
                name: "IX_InstructorDebits_TenantId_SourcePayoutId",
                table: "InstructorDebits",
                columns: new[] { "TenantId", "SourcePayoutId" });

            migrationBuilder.CreateIndex(
                name: "IX_InstructorPayoutLines_InstructorPayoutId",
                table: "InstructorPayoutLines",
                column: "InstructorPayoutId");

            migrationBuilder.CreateIndex(
                name: "IX_InstructorPayoutLines_OrderId",
                table: "InstructorPayoutLines",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_InstructorPayoutLines_TenantId_CourseId",
                table: "InstructorPayoutLines",
                columns: new[] { "TenantId", "CourseId" });

            migrationBuilder.CreateIndex(
                name: "IX_InstructorPayoutLines_TenantId_InstructorPayoutId",
                table: "InstructorPayoutLines",
                columns: new[] { "TenantId", "InstructorPayoutId" });

            migrationBuilder.CreateIndex(
                name: "IX_InstructorPayoutLines_TenantId_OrderId",
                table: "InstructorPayoutLines",
                columns: new[] { "TenantId", "OrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_InstructorPayoutLines_TenantId_OrderItemId",
                table: "InstructorPayoutLines",
                columns: new[] { "TenantId", "OrderItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderRefunds_TenantId_OrderId",
                table: "OrderRefunds",
                columns: new[] { "TenantId", "OrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderRefunds_TenantId_RecordedBy",
                table: "OrderRefunds",
                columns: new[] { "TenantId", "RecordedBy" });

            migrationBuilder.CreateIndex(
                name: "UX_OrderRefunds_OrderId_Reference",
                table: "OrderRefunds",
                columns: new[] { "OrderId", "Reference" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderItemShares_InstructorPayouts_TenantId_SettledPayoutId",
                table: "OrderItemShares",
                columns: new[] { "TenantId", "SettledPayoutId" },
                principalTable: "InstructorPayouts",
                principalColumns: new[] { "TenantId", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrderItemShares_InstructorPayouts_TenantId_SettledPayoutId",
                table: "OrderItemShares");

            migrationBuilder.DropTable(
                name: "InstructorDebitApplications");

            migrationBuilder.DropTable(
                name: "InstructorPayoutLines");

            migrationBuilder.DropTable(
                name: "InstructorDebits");

            migrationBuilder.DropTable(
                name: "OrderRefunds");

            migrationBuilder.DropIndex(
                name: "IX_OrderItemShares_TenantId_SettledPayoutId",
                table: "OrderItemShares");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_InstructorPayouts_TenantId_Id",
                table: "InstructorPayouts");

            migrationBuilder.DropColumn(
                name: "ReleasedLate",
                table: "OrderItemShares");

            migrationBuilder.DropColumn(
                name: "SettledPayoutId",
                table: "OrderItemShares");
        }
    }
}
