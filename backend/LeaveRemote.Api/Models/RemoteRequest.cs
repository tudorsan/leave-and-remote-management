namespace LeaveRemote.Api.Models;

public class RemoteRequest
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public RequestStatus Status { get; set; } = RequestStatus.Pending;

    public bool WasAutoApproved { get; set; }

    public string? Comment { get; set; }

    public string? RejectionReason { get; set; }

    /// <summary>Motivul unei anulări făcute de aplicație (ex. cerere nedecisă până la finalul perioadei).</summary>
    public string? CancellationReason { get; set; }

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
