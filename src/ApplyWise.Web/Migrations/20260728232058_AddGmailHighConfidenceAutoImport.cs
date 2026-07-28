using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApplyWise.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddGmailHighConfidenceAutoImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoAddHighConfidenceApplications",
                table: "GmailConnections",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "ApplicationImports",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.CreateIndex(
                name: "IX_JobApplications_UserId_AppliedDate",
                table: "JobApplications",
                columns: new[] { "UserId", "AppliedDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationImports_UserId_CreatedApplicationId",
                table: "ApplicationImports",
                columns: new[] { "UserId", "CreatedApplicationId" });

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationImports_UserId_Status_ReviewedAt",
                table: "ApplicationImports",
                columns: new[] { "UserId", "Status", "ReviewedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_JobApplications_UserId_AppliedDate",
                table: "JobApplications");

            migrationBuilder.DropIndex(
                name: "IX_ApplicationImports_UserId_CreatedApplicationId",
                table: "ApplicationImports");

            migrationBuilder.DropIndex(
                name: "IX_ApplicationImports_UserId_Status_ReviewedAt",
                table: "ApplicationImports");

            migrationBuilder.DropColumn(
                name: "AutoAddHighConfidenceApplications",
                table: "GmailConnections");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "ApplicationImports");
        }
    }
}
