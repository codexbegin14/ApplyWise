using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApplyWise.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminMonitoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Succeeded = table.Column<bool>(type: "bit", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductEvents_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "UserAccountActivities",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    RegisteredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastLoginAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastActivityAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastLoginProvider = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    TotalSuccessfulLogins = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAccountActivities", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_UserAccountActivities_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductEvents_Name_OccurredAt",
                table: "ProductEvents",
                columns: new[] { "Name", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductEvents_OccurredAt",
                table: "ProductEvents",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_ProductEvents_UserId_OccurredAt",
                table: "ProductEvents",
                columns: new[] { "UserId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_UserAccountActivities_LastActivityAt",
                table: "UserAccountActivities",
                column: "LastActivityAt");

            migrationBuilder.CreateIndex(
                name: "IX_UserAccountActivities_RegisteredAt",
                table: "UserAccountActivities",
                column: "RegisteredAt");

            migrationBuilder.Sql("""
                INSERT INTO [UserAccountActivities]
                    ([UserId], [RegisteredAt], [LastLoginAt], [LastActivityAt], [LastLoginProvider], [TotalSuccessfulLogins])
                SELECT
                    [u].[Id],
                    COALESCE([p].[CreatedAt], SYSUTCDATETIME()),
                    NULL,
                    NULL,
                    NULL,
                    0
                FROM [AspNetUsers] AS [u]
                LEFT JOIN [CareerProfiles] AS [p] ON [p].[UserId] = [u].[Id]
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM [UserAccountActivities] AS [a]
                    WHERE [a].[UserId] = [u].[Id]);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductEvents");

            migrationBuilder.DropTable(
                name: "UserAccountActivities");
        }
    }
}
