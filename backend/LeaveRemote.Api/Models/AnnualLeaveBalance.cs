
namespace LeaveRemote.Api.Models;

public class AnnualLeaveBalance
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public int Year { get; set; }

    public int TotalDays { get; set; } = 21;
}
