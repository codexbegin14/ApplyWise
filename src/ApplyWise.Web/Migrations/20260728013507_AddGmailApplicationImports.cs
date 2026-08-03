using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApplyWise.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddGmailApplicationImports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GmailConnections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    EmailAddress = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    ProtectedRefreshToken = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ConnectedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastSyncStartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastSuccessfulSyncAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    NextSyncAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastErrorCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GmailConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GmailConnections_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ApplicationImports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    GmailConnectionId = table.Column<int>(type: "int", nullable: false),
                    ExternalMessageId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ExternalThreadId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Direction = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Confidence = table.Column<int>(type: "int", nullable: false),
                    EmailSubject = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    SenderDomain = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    CompanyName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    JobTitle = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    JobLocation = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Source = table.Column<int>(type: "int", nullable: false),
                    JobUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    AppliedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ResumeFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    CreatedApplicationId = table.Column<int>(type: "int", nullable: true),
                    DetectedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationImports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApplicationImports_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ApplicationImports_GmailConnections_GmailConnectionId",
                        column: x => x.GmailConnectionId,
                        principalTable: "GmailConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationImports_GmailConnectionId_ExternalMessageId",
                table: "ApplicationImports",
                columns: new[] { "GmailConnectionId", "ExternalMessageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationImports_UserId_Status_DetectedAt",
                table: "ApplicationImports",
                columns: new[] { "UserId", "Status", "DetectedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_GmailConnections_NextSyncAt",
                table: "GmailConnections",
                column: "NextSyncAt");

            migrationBuilder.CreateIndex(
                name: "IX_GmailConnections_UserId",
                table: "GmailConnections",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApplicationImports");

            migrationBuilder.DropTable(
                name: "GmailConnections");
        }
    }
}
