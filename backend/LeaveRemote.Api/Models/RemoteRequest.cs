namespace LeaveRemote.Api.Models;

public class RemoteRequest
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public RequestStatus Status { get; set; } = RequestStatus.Pending;

    public bool WasAutoApproved { get; set; }

    public string? Comment { get; set; }

    public string? RejectionReason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int MinDaysAhead { get; set; }

    public int MaxDaysAhead { get; set; }

    public int MaxDaysPerWeek { get; set; }

    public List<DateOnly> HolidayDates { get; set; } = new();

    public List<RemoteRequestDay> Days { get; set; } = new();
}

public class RemoteRequestDay
{
    public int Id { get; set; }

    public int RemoteRequestId { get; set; }

    public DateOnly Date { get; set; }
}
