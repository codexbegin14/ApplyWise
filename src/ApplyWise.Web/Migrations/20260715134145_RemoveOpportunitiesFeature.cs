using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApplyWise.Web.Migrations
{
    /// <inheritdoc />
    public partial class RemoveOpportunitiesFeature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SavedOpportunities");

            migrationBuilder.DropTable(
                name: "Opportunities");

            migrationBuilder.DropColumn(
                name: "OpportunitiesViewedAt",
                table: "CareerProfiles");

            migrationBuilder.DropColumn(
                name: "OpportunityInterests",
                table: "CareerProfiles");

            migrationBuilder.DropColumn(
                name: "OpportunityNotificationsEnabled",
                table: "CareerProfiles");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OpportunitiesViewedAt",
                table: "CareerProfiles",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OpportunityInterests",
                table: "CareerProfiles",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "OpportunityNotificationsEnabled",
                table: "CareerProfiles",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "Opportunities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ApplicationRequirements = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ApplicationUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    OrganizationType = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    Compensation = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ApplicationDeadline = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EligibleDegrees = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    EligibleGraduationYears = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    EmploymentType = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    ExperienceLevel = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    IsPaid = table.Column<bool>(type: "bit", nullable: false),
                    IsVerified = table.Column<bool>(type: "bit", nullable: false),
                    Location = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: true),
                    NoExperienceRequired = table.Column<bool>(type: "bit", nullable: false),
                    NormalizedKey = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    OrganizationName = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Requirements = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Skills = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    SourceName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    SourceUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StudentEligibility = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Summary = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    WorkMode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Opportunities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SavedOpportunities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OpportunityId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    SavedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedOpportunities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SavedOpportunities_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SavedOpportunities_Opportunities_OpportunityId",
                        column: x => x.OpportunityId,
                        principalTable: "Opportunities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Opportunities_NormalizedKey",
                table: "Opportunities",
                column: "NormalizedKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Opportunities_SourceUrl",
                table: "Opportunities",
                column: "SourceUrl",
                unique: true,
                filter: "[SourceUrl] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Opportunities_Status_ApplicationDeadline",
                table: "Opportunities",
                columns: new[] { "Status", "ApplicationDeadline" });

            migrationBuilder.CreateIndex(
                name: "IX_Opportunities_Status_OrganizationType_PublishedAt",
                table: "Opportunities",
                columns: new[] { "Status", "OrganizationType", "PublishedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SavedOpportunities_OpportunityId",
                table: "SavedOpportunities",
                column: "OpportunityId");

            migrationBuilder.CreateIndex(
                name: "IX_SavedOpportunities_UserId_OpportunityId",
                table: "SavedOpportunities",
                columns: new[] { "UserId", "OpportunityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SavedOpportunities_UserId_SavedAt",
                table: "SavedOpportunities",
                columns: new[] { "UserId", "SavedAt" });
        }
    }
}
