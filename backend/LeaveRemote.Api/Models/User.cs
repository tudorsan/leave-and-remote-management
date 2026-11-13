namespace LeaveRemote.Api.Models;

public class User
{
    public int Id { get; set; }

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public UserRole Role { get; set; } = UserRole.Employee;

    public int DefaultAnnualLeaveDays { get; set; }

    public bool IsActive { get; set; } = true;

    public int? TeamId { get; set; }

    public int? ManagerId { get; set; }

    /// <summary>
    /// Se schimbă la fiecare schimbare de parolă. Este inclus în token, iar tokenurile emise
    /// cu o valoare veche sunt respinse, deci resetarea parolei închide toate sesiunile existente.
    /// </summary>
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");
}

public enum UserRole
{
    Employee,
    Manager,
    Admin
}
