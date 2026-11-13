using System.Globalization;
using System.Text;
using LeaveRemote.Api.Data;
using LeaveRemote.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeaveRemote.Api.Services;

/// <summary>Un fișier generat pentru descărcare.</summary>
public sealed record ExportFile(byte[] Content, string ContentType, string FileName);

/// <summary>
/// Cerința 6 (opțională), a doua parte: exportul propriilor zile aprobate.
/// Conține doar cererile utilizatorului curent aflate în starea „Aprobată” (concedii de orice tip și zile remote),
/// în două formate: iCalendar (.ics), care se importă în Google Calendar / Outlook, și CSV pentru Excel.
/// </summary>
public sealed class ExportService(AppDbContext db, AppClock clock)
{
    private sealed record ApprovedEntry(string Uid, string Label, DateOnly Start, DateOnly End, int WorkingDays, string? Comment);

    public async Task<ExportFile> ToICalendarAsync(User user)
    {
        var entries = await GetApprovedAsync(user);
        var stamp = clock.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

        var ics = new StringBuilder();
        void Line(string text) => ics.Append(Fold(text)).Append("\r\n");

        Line("BEGIN:VCALENDAR");
        Line("VERSION:2.0");
        Line("PRODID:-//Ritm//Calendar concedii si remote//RO");
        Line("CALSCALE:GREGORIAN");
        Line("METHOD:PUBLISH");
        Line($"X-WR-CALNAME:{Escape($"Zile aprobate – {user.FirstName} {user.LastName}")}");
        foreach (var entry in entries)
        {
            var description = entry.WorkingDays == 1 ? "1 zi lucrătoare" : $"{entry.WorkingDays} zile lucrătoare";
            if (!string.IsNullOrWhiteSpace(entry.Comment)) description += $"\nComentariu: {entry.Comment}";

            Line("BEGIN:VEVENT");
            Line($"UID:{entry.Uid}");
            Line($"DTSTAMP:{stamp}");
            // Evenimente pe toată ziua: DTEND este exclusiv, deci ziua de după ultima zi.
            Line($"DTSTART;VALUE=DATE:{entry.Start:yyyyMMdd}");
            Line($"DTEND;VALUE=DATE:{entry.End.AddDays(1):yyyyMMdd}");
            Line($"SUMMARY:{Escape(entry.Label)}");
            Line($"DESCRIPTION:{Escape(description)}");
            Line("TRANSP:TRANSPARENT");
            Line("END:VEVENT");
        }
        Line("END:VCALENDAR");

        return new ExportFile(Encoding.UTF8.GetBytes(ics.ToString()), "text/calendar; charset=utf-8", "zile-aprobate.ics");
    }

    public async Task<ExportFile> ToCsvAsync(User user)
    {
        var entries = await GetApprovedAsync(user);

        // Separator „;” și BOM UTF-8: Excel cu setări românești deschide corect coloanele și diacriticele.
        var csv = new StringBuilder();
        csv.Append("Tip;Început;Sfârșit;Zile lucrătoare;Comentariu\r\n");
        foreach (var entry in entries)
        {
            csv.Append(string.Join(';',
                Field(entry.Label),
                entry.Start.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
                entry.End.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
                entry.WorkingDays.ToString(CultureInfo.InvariantCulture),
                Field(entry.Comment ?? string.Empty)));
            csv.Append("\r\n");
        }

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        return new ExportFile(bytes, "text/csv; charset=utf-8", "zile-aprobate.csv");
    }

    /// <summary>Concediile aprobate (o intrare per cerere) și zilele remote aprobate (o intrare per zi), în ordine cronologică.</summary>
    private async Task<List<ApprovedEntry>> GetApprovedAsync(User user)
    {
        var leave = await db.LeaveRequests.AsNoTracking()
            .Where(r => r.UserId == user.Id && r.Status == RequestStatus.Approved)
            .Select(r => new { r.Id, r.Type, r.StartDate, r.EndDate, r.WorkingDates, r.Comment })
            .ToListAsync();

        var remote = await (
            from day in db.RemoteRequestDays.AsNoTracking()
            join request in db.RemoteRequests.AsNoTracking() on day.RemoteRequestId equals request.Id
            where request.UserId == user.Id && request.Status == RequestStatus.Approved
            select new { request.Id, day.Date, request.Comment })
            .ToListAsync();

        return leave
            .Select(r => new ApprovedEntry($"leave-{r.Id}@ritm.local", LeaveLabel(r.Type), r.StartDate, r.EndDate, r.WorkingDates.Count, r.Comment))
            .Concat(remote.Select(d => new ApprovedEntry($"remote-{d.Id}-{d.Date:yyyyMMdd}@ritm.local", "Lucru remote", d.Date, d.Date, 1, d.Comment)))
            .OrderBy(e => e.Start).ThenBy(e => e.Label)
            .ToList();
    }

    private static string LeaveLabel(LeaveType type) => type switch
    {
        LeaveType.Vacation => "Concediu de odihnă",
        LeaveType.Medical => "Concediu medical",
        LeaveType.Unpaid => "Concediu fără plată",
        LeaveType.Special => "Eveniment special",
        _ => "Concediu"
    };

    /// <summary>Escape pentru textul iCalendar (RFC 5545): \ ; , și rândurile noi.</summary>
    private static string Escape(string text) => text
        .Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,")
        .Replace("\r\n", "\\n").Replace("\n", "\\n").Replace("\r", "\\n");

    /// <summary>Liniile iCalendar se împart la cel mult 75 de octeți; continuările încep cu un spațiu.</summary>
    private static string Fold(string line)
    {
        if (Encoding.UTF8.GetByteCount(line) <= 75) return line;
        var result = new StringBuilder();
        var current = new StringBuilder();
        var limit = 75;
        foreach (var element in EnumerateTextElements(line))
        {
            if (Encoding.UTF8.GetByteCount(current + element) > limit)
            {
                result.Append(current).Append("\r\n ");
                current.Clear();
                limit = 74; // spațiul de la început ocupă un octet
            }
            current.Append(element);
        }
        return result.Append(current).ToString();
    }

    private static IEnumerable<string> EnumerateTextElements(string text)
    {
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext()) yield return enumerator.GetTextElement();
    }

    /// <summary>Câmp CSV: între ghilimele dacă conține separatorul, ghilimele sau rânduri noi.</summary>
    private static string Field(string value) =>
        value.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
}
