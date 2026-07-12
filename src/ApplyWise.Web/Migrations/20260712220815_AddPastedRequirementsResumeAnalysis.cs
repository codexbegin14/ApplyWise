using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApplyWise.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddPastedRequirementsResumeAnalysis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "JobApplicationId",
                table: "ResumeAnalyses",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<string>(
                name: "AnalysisType",
                table: "ResumeAnalyses",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "SavedApplication");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AnalysisType",
                table: "ResumeAnalyses");

            migrationBuilder.Sql(
                "DELETE FROM [ResumeAnalyses] WHERE [JobApplicationId] IS NULL;");

            migrationBuilder.AlterColumn<int>(
                name: "JobApplicationId",
                table: "ResumeAnalyses",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
        }
    }
}
