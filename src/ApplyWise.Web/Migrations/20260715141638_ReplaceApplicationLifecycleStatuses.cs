using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApplyWise.Web.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceApplicationLifecycleStatuses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE [JobApplications]
                SET [Status] = CASE [Status]
                    WHEN 0 THEN 1
                    WHEN 4 THEN 2
                    ELSE [Status]
                END
                WHERE [Status] IN (0, 4);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The source values for Saved and Technical Test are intentionally
            // consolidated into the new lifecycle and cannot be reconstructed.
        }
    }
}
