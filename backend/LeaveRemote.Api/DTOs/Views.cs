namespace LeaveRemote.Api.DTOs;

// Răspunsurile API-ului (serializate camelCase).

public sealed record OwnLeaveRequestView(
    int Id, int UserId, DateOnly StartDate, DateOnly EndDate, string Type, string Status, int WorkingDays,
    string? Comment, string? RejectionReason, string? CancellationReason, DateTime CreatedAt, bool CanCancel);

public sealed record SavedLeaveRequestView(
    int Id, int UserId, DateOnly StartDate, DateOnly EndDate, string Type, string Status, int WorkingDays, string? Comment);

public sealed record PendingLeaveView(
    int Id, int UserId, string FirstName, string LastName, DateOnly StartDate, DateOnly EndDate,
    string Type, string Status, int WorkingDays, string? Comment, DateTime CreatedAt);

public sealed record OwnRemoteRequestView(
    int Id, int UserId, string Status, List<DateOnly> Days, string? Comment, string? RejectionReason,
    string? CancellationReason, DateTime CreatedAt, bool CanEdit, bool CanCancel);

public sealed record SavedRemoteRequestView(List<int> Ids, int UserId, string Status, List<DateOnly> Days, string? Comment);

public sealed record PendingRemoteView(
    int Id, int UserId, string FirstName, string LastName, string? Comment, DateTime CreatedAt, List<DateOnly> Days);

public sealed record RequestOutcomeView(int Id, string Status, string? RejectionReason, string Message);

public sealed record BalanceView(int UserId, int Year, int TotalDays, int UsedDays, int PendingDays, int AvailableDays);

public sealed record CalendarMemberView(int Id, string FirstName, string LastName, int? TeamId);

public sealed record CalendarEventView(int RequestId, int UserId, DateOnly Date, string Type, string Status);

public sealed record HolidayView(DateOnly Date, string Name);

public sealed record CalendarMonthView(
    int Year, int Month, int? TeamId, List<CalendarMemberView> Members, List<HolidayView> Holidays, List<CalendarEventView> Events);

public sealed record CalendarRangeView(
    DateOnly From, DateOnly To, int? TeamId, List<CalendarMemberView> Members, List<HolidayView> Holidays, List<CalendarEventView> Events);

public sealed record SettingsView(Dictionary<string, string> Settings, List<HolidayView> Holidays);

public sealed record UserView(
    int Id, string FirstName, string LastName, string Email, string Role, int? TeamId, int? ManagerId, bool IsActive);

public sealed record TeamView(int Id, string Name, bool IsActive);
