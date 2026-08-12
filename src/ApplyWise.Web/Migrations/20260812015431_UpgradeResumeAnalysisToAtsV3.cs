using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApplyWise.Web.Migrations
{
    /// <inheritdoc />
    public partial class UpgradeResumeAnalysisToAtsV3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FileDiagnosticsJson",
                table: "Resumes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PageCount",
                table: "Resumes",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FileDiagnosticsJson",
                table: "Resumes");

            migrationBuilder.DropColumn(
                name: "PageCount",
                table: "Resumes");
        }
    }
}
