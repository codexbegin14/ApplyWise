using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApplyWise.Web.Migrations;

public partial class AddOpportunityCompatibility : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
IF COL_LENGTH(N'CareerProfiles', N'AcademicHighlights') IS NOT NULL AND COL_LENGTH(N'CareerProfiles', N'AcademicProjects') IS NULL
    EXEC sp_rename N'CareerProfiles.AcademicHighlights', N'AcademicProjects', N'COLUMN';
IF COL_LENGTH(N'Opportunities', N'StudentEligibility') IS NULL
    ALTER TABLE [Opportunities] ADD [StudentEligibility] nvarchar(1000) NULL;
");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Compatibility columns are retained during rollback to preserve user data.
    }
}
