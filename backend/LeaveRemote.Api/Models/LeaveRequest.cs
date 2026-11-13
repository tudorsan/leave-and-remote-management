namespace LeaveRemote.Api.Models;

public class LeaveRequest
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    public List<DateOnly> WorkingDates { get; set; } = new();

    public int TeamAbsenceLimitPercent { get; set; }

    public int MinDaysAhead { get; set; }

    public int MaxDaysAhead { get; set; }

    public List<DateOnly> HolidayDates { get; set; } = new();

    public LeaveType Type { get; set; }

    public RequestStatus Status { get; set; } = RequestStatus.Pending;

    public string? Comment { get; set; }

    public string? RejectionReason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum LeaveType
{
    Vacation,
    Medical,
    Unpaid,
    Special
}

public enum RequestStatus
{
    Pending,
    Approved,
    Rejected,
    Canceled
}
