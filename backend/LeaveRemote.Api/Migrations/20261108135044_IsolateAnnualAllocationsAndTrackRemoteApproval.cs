using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeaveRemote.Api.Migrations
{
    /// <inheritdoc />
    public partial class IsolateAnnualAllocationsAndTrackRemoteApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DefaultAnnualLeaveDays",
                table: "Users",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "WasAutoApproved",
                table: "RemoteRequests",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // Older users have no recorded initial allowance. Keep their yearly overrides intact.
            migrationBuilder.Sql("""
                DECLARE @days int = (
                    SELECT TRY_CONVERT(int, [Value])
                    FROM dbo.AppSettings WHERE [Key] = 'leave.default_annual_days'
                );
                UPDATE dbo.Users SET DefaultAnnualLeaveDays =
                    CASE WHEN @days >= 0 THEN @days ELSE 21 END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefaultAnnualLeaveDays",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "WasAutoApproved",
                table: "RemoteRequests");
        }
    }
}
