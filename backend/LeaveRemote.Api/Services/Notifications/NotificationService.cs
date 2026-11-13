using LeaveRemote.Api.Data;
using LeaveRemote.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeaveRemote.Api.Services.Notifications;

/// <summary>
/// Cerința 16: notificări prin email la schimbarea stării unei cereri.
/// Decide pentru fiecare eveniment cine primește emailul și ce scrie în el, apoi îl pune în coadă:
/// <list type="bullet">
/// <item>solicitantul: cerere aprobată, respinsă (cu motiv) sau zi remote aprobată automat;</item>
/// <item>aprobatorii: cerere nouă de aprobat sau cerere anulată de solicitant.</item>
/// </list>
/// </summary>
public sealed class NotificationService(AppDbContext db, NotificationOutbox outbox)
{
    // ---------- Concedii ----------

    public async Task LeaveSubmittedAsync(LeaveRequest request, User requester)
    {
        foreach (var reviewer in await ReviewersOfAsync(requester))
        {
            outbox.Enqueue(new EmailMessage(reviewer.Email,
                $"Cerere nouă de aprobat: {FullName(requester)}",
                $"""
                Salut, {reviewer.FirstName}!

                {FullName(requester)} a trimis o cerere de {LeaveLabel(request.Type)} pentru {Period(request)} ({WorkingDays(request)}).
                {CommentLine(request.Comment)}Cererea așteaptă decizia ta în secțiunea „Cereri în așteptare”.
                """));
        }
    }

    public void LeaveApproved(LeaveRequest request, User requester, User reviewer) =>
        outbox.Enqueue(new EmailMessage(requester.Email,
            $"Cererea ta de {LeaveLabel(request.Type)} a fost aprobată",
            $"""
            Salut, {requester.FirstName}!

            Cererea ta de {LeaveLabel(request.Type)} pentru {Period(request)} ({WorkingDays(request)}) a fost aprobată de {FullName(reviewer)}.
            """));

    public void LeaveRejected(LeaveRequest request, User requester, User reviewer, string reason) =>
        outbox.Enqueue(new EmailMessage(requester.Email,
            $"Cererea ta de {LeaveLabel(request.Type)} a fost respinsă",
            $"""
            Salut, {requester.FirstName}!

            Cererea ta de {LeaveLabel(request.Type)} pentru {Period(request)} a fost respinsă de {FullName(reviewer)}.
            Motiv: {reason}
            """));

    public async Task LeaveCancelledAsync(LeaveRequest request, User requester, bool wasApproved)
    {
        foreach (var reviewer in await ReviewersOfAsync(requester))
        {
            outbox.Enqueue(new EmailMessage(reviewer.Email,
                $"Cerere anulată: {FullName(requester)}",
                $"""
                Salut, {reviewer.FirstName}!

                {FullName(requester)} și-a anulat cererea {(wasApproved ? "aprobată" : "în așteptare")} de {LeaveLabel(request.Type)} pentru {Period(request)}.
                """));
        }
    }

    // ---------- Zile remote ----------

    /// <summary>Un singur email pentru toate zilele trimise în același formular.</summary>
    public async Task RemoteSubmittedAsync(IReadOnlyList<DateOnly> days, User requester, bool autoApproved)
    {
        if (autoApproved)
        {
            outbox.Enqueue(new EmailMessage(requester.Email,
                "Zilele tale de lucru remote au fost aprobate",
                $"""
                Salut, {requester.FirstName}!

                Zilele de lucru remote {Days(days)} au fost înregistrate și aprobate automat.
                """));
            return;
        }

        foreach (var reviewer in await ReviewersOfAsync(requester))
        {
            outbox.Enqueue(new EmailMessage(reviewer.Email,
                $"Zile remote de aprobat: {FullName(requester)}",
                $"""
                Salut, {reviewer.FirstName}!

                {FullName(requester)} a cerut lucru remote în {Days(days)}.
                Cererile așteaptă decizia ta în secțiunea „Cereri în așteptare”.
                """));
        }
    }

    public void RemoteApproved(IEnumerable<DateOnly> days, User requester, User reviewer) =>
        outbox.Enqueue(new EmailMessage(requester.Email,
            "Cererea ta de lucru remote a fost aprobată",
            $"""
            Salut, {requester.FirstName}!

            Lucrul remote din {Days(days)} a fost aprobat de {FullName(reviewer)}.
            """));

    public void RemoteRejected(IEnumerable<DateOnly> days, User requester, User reviewer, string reason) =>
        outbox.Enqueue(new EmailMessage(requester.Email,
            "Cererea ta de lucru remote a fost respinsă",
            $"""
            Salut, {requester.FirstName}!

            Lucrul remote din {Days(days)} a fost respins de {FullName(reviewer)}.
            Motiv: {reason}
            """));

    public async Task RemoteCancelledAsync(IEnumerable<DateOnly> days, User requester, bool wasApproved)
    {
        foreach (var reviewer in await ReviewersOfAsync(requester))
        {
            outbox.Enqueue(new EmailMessage(reviewer.Email,
                $"Zi remote anulată: {FullName(requester)}",
                $"""
                Salut, {reviewer.FirstName}!

                {FullName(requester)} și-a anulat lucrul remote {(wasApproved ? "aprobat" : "în așteptare")} din {Days(days)}.
                """));
        }
    }

    // ---------- Cine aprobă cererile cuiva ----------

    /// <summary>
    /// Destinatarii emailurilor pentru aprobatori:
    /// angajat → managerii activi ai echipei sale; Manager/Admin → superiorul direct activ;
    /// dacă nu există niciunul → Adminii activi. Solicitantul nu se notifică pe sine.
    /// </summary>
    public async Task<List<User>> ReviewersOfAsync(User requester)
    {
        var reviewers = requester.Role == UserRole.Employee
            ? await db.Users.AsNoTracking()
                .Where(u => u.IsActive && u.Role == UserRole.Manager && requester.TeamId != null && u.TeamId == requester.TeamId)
                .ToListAsync()
            : await db.Users.AsNoTracking()
                .Where(u => u.IsActive && u.Id == requester.ManagerId && (u.Role == UserRole.Manager || u.Role == UserRole.Admin))
                .ToListAsync();

        if (reviewers.Count == 0)
        {
            reviewers = await db.Users.AsNoTracking().Where(u => u.IsActive && u.Role == UserRole.Admin).ToListAsync();
        }
        return reviewers.Where(u => u.Id != requester.Id).OrderBy(u => u.Id).ToList();
    }

    // ---------- Formatare ----------

    private static string FullName(User user) => $"{user.FirstName} {user.LastName}";

    private static string Format(DateOnly date) => date.ToString("dd.MM.yyyy");

    private static string Period(LeaveRequest request) => request.StartDate == request.EndDate
        ? Format(request.StartDate)
        : $"{Format(request.StartDate)} – {Format(request.EndDate)}";

    private static string WorkingDays(LeaveRequest request) =>
        request.WorkingDates.Count == 1 ? "1 zi lucrătoare" : $"{request.WorkingDates.Count} zile lucrătoare";

    private static string Days(IEnumerable<DateOnly> days) => string.Join(", ", days.Order().Select(Format));

    private static string CommentLine(string? comment) =>
        string.IsNullOrWhiteSpace(comment) ? string.Empty : $"Comentariu: {comment}\n";

    private static string LeaveLabel(LeaveType type) => type switch
    {
        LeaveType.Vacation => "concediu de odihnă",
        LeaveType.Medical => "concediu medical",
        LeaveType.Unpaid => "concediu fără plată",
        LeaveType.Special => "concediu pentru eveniment special",
        _ => "concediu"
    };
}
