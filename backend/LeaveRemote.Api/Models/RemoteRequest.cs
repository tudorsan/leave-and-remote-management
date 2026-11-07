namespace LeaveRemote.Api.Models;

public class RemoteRequest
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public RequestStatus Status { get; set; } = RequestStatus.Pending;

    public string? Comment { get; set; }

    public string? RejectionReason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<RemoteRequestDay> Days { get; set; } = new();
}

public class RemoteRequestDay
{
    public int Id { get; set; }

    public int RemoteRequestId { get; set; }

    public DateOnly Date { get; set; }
}