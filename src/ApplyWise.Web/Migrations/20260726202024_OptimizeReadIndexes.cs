using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApplyWise.Web.Migrations
{
    /// <inheritdoc />
    public partial class OptimizeReadIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_ResumeAnalyses_UserId_ScoreVersion_CreatedAt",
                table: "ResumeAnalyses",
                columns: new[] { "UserId", "ScoreVersion", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Reminders_UserId_IsCompleted_DueAt",
                table: "Reminders",
                columns: new[] { "UserId", "IsCompleted", "DueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_JobApplications_UserId_Deadline",
                table: "JobApplications",
                columns: new[] { "UserId", "Deadline" });

            migrationBuilder.CreateIndex(
                name: "IX_JobApplications_UserId_UpdatedAt",
                table: "JobApplications",
                columns: new[] { "UserId", "UpdatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ResumeAnalyses_UserId_ScoreVersion_CreatedAt",
                table: "ResumeAnalyses");

            migrationBuilder.DropIndex(
                name: "IX_Reminders_UserId_IsCompleted_DueAt",
                table: "Reminders");

            migrationBuilder.DropIndex(
                name: "IX_JobApplications_UserId_Deadline",
                table: "JobApplications");

            migrationBuilder.DropIndex(
                name: "IX_JobApplications_UserId_UpdatedAt",
                table: "JobApplications");
        }
    }
}
