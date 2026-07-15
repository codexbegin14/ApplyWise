using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApplyWise.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountSecurityCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccountSecurityCodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Salt = table.Column<byte[]>(type: "varbinary(16)", maxLength: 16, nullable: false),
                    CodeHash = table.Column<byte[]>(type: "varbinary(32)", maxLength: 32, nullable: false),
                    FailedAttemptCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountSecurityCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountSecurityCodes_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountSecurityCodes_UserId_Action_CreatedAt",
                table: "AccountSecurityCodes",
                columns: new[] { "UserId", "Action", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountSecurityCodes");
        }
    }
}
