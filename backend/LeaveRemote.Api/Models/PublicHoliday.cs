
namespace LeaveRemote.Api.Models;

public class PublicHoliday
{
    public int Id { get; set; }

    public DateOnly Date { get; set; }

    public string Name { get; set; } = string.Empty;
}
