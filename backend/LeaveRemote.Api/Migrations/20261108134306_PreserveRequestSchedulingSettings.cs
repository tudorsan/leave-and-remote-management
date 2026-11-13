using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeaveRemote.Api.Migrations
{
    /// <inheritdoc />
    public partial class PreserveRequestSchedulingSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HolidayDates",
                table: "RemoteRequests",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<int>(
                name: "MaxDaysAhead",
                table: "RemoteRequests",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaxDaysPerWeek",
                table: "RemoteRequests",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MinDaysAhead",
                table: "RemoteRequests",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "HolidayDates",
                table: "LeaveRequests",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<int>(
                name: "MaxDaysAhead",
                table: "LeaveRequests",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MinDaysAhead",
                table: "LeaveRequests",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("""
                DECLARE @holidays nvarchar(max) = (
                    SELECT COALESCE(N'[' + STRING_AGG(CONVERT(nvarchar(max), N'"' + CONVERT(nvarchar(10), [Date], 23) + N'"'), N',')
                        WITHIN GROUP (ORDER BY [Date]) + N']', N'[]')
                    FROM dbo.PublicHolidays
                );

                UPDATE dbo.LeaveRequests
                SET HolidayDates = @holidays,
                    MinDaysAhead = COALESCE((SELECT TRY_CONVERT(int, [Value]) FROM dbo.AppSettings WHERE [Key] = 'leave.min_days_ahead'), 3),
                    MaxDaysAhead = COALESCE((SELECT TRY_CONVERT(int, [Value]) FROM dbo.AppSettings WHERE [Key] = 'leave.max_days_ahead'), 365);

                UPDATE dbo.RemoteRequests
                SET HolidayDates = @holidays,
                    MinDaysAhead = COALESCE((SELECT TRY_CONVERT(int, [Value]) FROM dbo.AppSettings WHERE [Key] = 'remote.min_days_ahead'), 0),
                    MaxDaysAhead = COALESCE((SELECT TRY_CONVERT(int, [Value]) FROM dbo.AppSettings WHERE [Key] = 'remote.max_days_ahead'), 30),
                    MaxDaysPerWeek = COALESCE((SELECT TRY_CONVERT(int, [Value]) FROM dbo.AppSettings WHERE [Key] = 'remote.max_days_per_week'), 2);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HolidayDates",
                table: "RemoteRequests");

            migrationBuilder.DropColumn(
                name: "MaxDaysAhead",
                table: "RemoteRequests");

            migrationBuilder.DropColumn(
                name: "MaxDaysPerWeek",
                table: "RemoteRequests");

            migrationBuilder.DropColumn(
                name: "MinDaysAhead",
                table: "RemoteRequests");

            migrationBuilder.DropColumn(
                name: "HolidayDates",
                table: "LeaveRequests");

            migrationBuilder.DropColumn(
                name: "MaxDaysAhead",
                table: "LeaveRequests");

            migrationBuilder.DropColumn(
                name: "MinDaysAhead",
                table: "LeaveRequests");
        }
    }
}
