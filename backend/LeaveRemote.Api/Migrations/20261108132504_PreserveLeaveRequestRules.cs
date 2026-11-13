using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeaveRemote.Api.Migrations
{
    /// <inheritdoc />
    public partial class PreserveLeaveRequestRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "Users",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<int>(
                name: "TeamAbsenceLimitPercent",
                table: "LeaveRequests",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "WorkingDates",
                table: "LeaveRequests",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");

            // Previous requests have no historical settings. Capture the values available at upgrade.
            migrationBuilder.Sql("""
                DECLARE @absenceLimit int = (
                    SELECT TRY_CONVERT(int, [Value])
                    FROM dbo.AppSettings WHERE [Key] = 'leave.max_team_absent_pct'
                );

                ;WITH RequestDates AS (
                    SELECT Id, StartDate AS [Date], EndDate
                    FROM dbo.LeaveRequests WHERE StartDate <= EndDate
                    UNION ALL
                    SELECT Id, DATEADD(day, 1, [Date]), EndDate
                    FROM RequestDates WHERE [Date] < EndDate
                ),
                WorkingDates AS (
                    SELECT d.Id,
                        N'[' + STRING_AGG(CONVERT(nvarchar(max), N'"' + CONVERT(nvarchar(10), d.[Date], 23) + N'"'), N',')
                            WITHIN GROUP (ORDER BY d.[Date]) + N']' AS DatesJson
                    FROM RequestDates AS d
                    WHERE ((DATEDIFF(day, CONVERT(date, '19000101'), d.[Date]) % 7 + 7) % 7) < 5
                        AND NOT EXISTS (SELECT 1 FROM dbo.PublicHolidays AS h WHERE h.[Date] = d.[Date])
                    GROUP BY d.Id
                )
                UPDATE r
                SET WorkingDates = COALESCE(d.DatesJson, N'[]'),
                    TeamAbsenceLimitPercent = CASE WHEN @absenceLimit BETWEEN 0 AND 100 THEN @absenceLimit ELSE 50 END
                FROM dbo.LeaveRequests AS r
                LEFT JOIN WorkingDates AS d ON d.Id = r.Id
                OPTION (MAXRECURSION 0);
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_Email",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TeamAbsenceLimitPercent",
                table: "LeaveRequests");

            migrationBuilder.DropColumn(
                name: "WorkingDates",
                table: "LeaveRequests");

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "Users",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");
        }
    }
}
