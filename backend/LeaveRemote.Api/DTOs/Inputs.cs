namespace LeaveRemote.Api.DTOs;

// Corpurile cererilor primite de API.

public sealed class LeaveRequestInput
{
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? Comment { get; set; }
}

public sealed class RemoteRequestInput
{
    public List<DateOnly> Days { get; set; } = [];
    public string? Comment { get; set; }
}

public sealed class RejectRequestInput
{
    public string? Reason { get; set; }
}

public sealed class CreateUserInput
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Role { get; set; } = "Employee";
    public int? TeamId { get; set; }
    public int? ManagerId { get; set; }
}

public sealed class UpdateUserInput
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Role { get; set; }
    public int? TeamId { get; set; }
    public int? ManagerId { get; set; }
}

public sealed class TeamInput
{
    public string Name { get; set; } = string.Empty;
}

public sealed record SetAllocationInput(int? TotalDays);

public sealed record GlobalSettingsInput(
    int? RemoteMinDaysAhead,
    int? RemoteMaxDaysAhead,
    int? RemoteMaxDaysPerWeek,
    bool? RemoteRequiresApproval,
    int? LeaveMinDaysAhead,
    int? LeaveMaxDaysAhead,
    int? LeaveDefaultAnnualDays,
    int? LeaveMaxTeamAbsentPct);

public sealed record HolidayInput(DateOnly Date, string? Name);

public sealed record ForgotPasswordInput(string? Email);

public sealed record ResetPasswordInput(string? Token, string? NewPassword);
