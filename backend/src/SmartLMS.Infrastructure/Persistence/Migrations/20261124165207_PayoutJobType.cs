using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartLMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PayoutJobType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_BackgroundJobs_Type",
                table: "BackgroundJobs");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BackgroundJobs_Type",
                table: "BackgroundJobs",
                sql: "[Type] IN ('AiIngest','AiTranscribe','PlagiarismBatch','VoucherGeneration','WebhookDelivery','CertificatePdf','AnalyticsRollup','Email','Export','PeerReviewAssign','PayoutCalculation')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_BackgroundJobs_Type",
                table: "BackgroundJobs");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BackgroundJobs_Type",
                table: "BackgroundJobs",
                sql: "[Type] IN ('AiIngest','AiTranscribe','PlagiarismBatch','VoucherGeneration','WebhookDelivery','CertificatePdf','AnalyticsRollup','Email','Export','PeerReviewAssign')");
        }
    }
}
