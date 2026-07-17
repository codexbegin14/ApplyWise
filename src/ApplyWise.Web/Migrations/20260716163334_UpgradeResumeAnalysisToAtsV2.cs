using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApplyWise.Web.Migrations
{
    /// <inheritdoc />
    public partial class UpgradeResumeAnalysisToAtsV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AtsReadinessScore",
                table: "ResumeAnalyses",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ConfidenceScore",
                table: "ResumeAnalyses",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DetectedJobRequirementCount",
                table: "ResumeAnalyses",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EvidenceJson",
                table: "ResumeAnalyses",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "EvidenceQuality",
                table: "ResumeAnalyses",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InputHash",
                table: "ResumeAnalyses",
                type: "nchar(64)",
                fixedLength: true,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "JobMatchScore",
                table: "ResumeAnalyses",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "MustHaveCoverage",
                table: "ResumeAnalyses",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "RequiredCoverage",
                table: "ResumeAnalyses",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewJson",
                table: "ResumeAnalyses",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScoreBreakdownJson",
                table: "ResumeAnalyses",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScoreVersion",
                table: "ResumeAnalyses",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WarningsJson",
                table: "ResumeAnalyses",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_ResumeAnalyses_CurrentInput",
                table: "ResumeAnalyses",
                columns: new[] { "UserId", "ResumeId", "JobApplicationId", "AnalysisType", "InputHash", "ScoreVersion" },
                unique: true,
                filter: "[InputHash] IS NOT NULL AND [ScoreVersion] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_ResumeAnalyses_CurrentInput",
                table: "ResumeAnalyses");

            migrationBuilder.DropColumn(
                name: "AtsReadinessScore",
                table: "ResumeAnalyses");

            migrationBuilder.DropColumn(
                name: "ConfidenceScore",
                table: "ResumeAnalyses");

            migrationBuilder.DropColumn(
                name: "DetectedJobRequirementCount",
                table: "ResumeAnalyses");

            migrationBuilder.DropColumn(
                name: "EvidenceJson",
                table: "ResumeAnalyses");

            migrationBuilder.DropColumn(
                name: "EvidenceQuality",
                table: "ResumeAnalyses");

            migrationBuilder.DropColumn(
                name: "InputHash",
                table: "ResumeAnalyses");

            migrationBuilder.DropColumn(
                name: "JobMatchScore",
                table: "ResumeAnalyses");

            migrationBuilder.DropColumn(
                name: "MustHaveCoverage",
                table: "ResumeAnalyses");

            migrationBuilder.DropColumn(
                name: "RequiredCoverage",
                table: "ResumeAnalyses");

            migrationBuilder.DropColumn(
                name: "ReviewJson",
                table: "ResumeAnalyses");

            migrationBuilder.DropColumn(
                name: "ScoreBreakdownJson",
                table: "ResumeAnalyses");

            migrationBuilder.DropColumn(
                name: "ScoreVersion",
                table: "ResumeAnalyses");

            migrationBuilder.DropColumn(
                name: "WarningsJson",
                table: "ResumeAnalyses");
        }
    }
}
