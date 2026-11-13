using LeaveRemote.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LeaveRemote.Api.Tests.Infrastructure;

/// <summary>
/// Pornește API-ul real în memorie, pe o bază SQL Server LocalDB nouă (creată prin migrări și populată
/// cu datele demo), cu ziua curentă fixată la <see cref="Today"/>. Baza este ștearsă la final.
/// Fiecare clasă de teste primește propria instanță, deci propria bază.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>Vineri, 13 noiembrie 2026, ora 10:00 în România.</summary>
    public static readonly DateOnly Today = new(2026, 11, 13);

    private static readonly DateTimeOffset TodayMorningUtc = new(2026, 11, 13, 8, 0, 0, TimeSpan.Zero);

    private readonly string _databaseName = $"LeaveRemoteTests_{Guid.NewGuid():N}";

    public TestTimeProvider Clock { get; } = new(TodayMorningUtc);

    public string ConnectionString =>
        $"Server=(localdb)\\MSSQLLocalDB;Database={_databaseName};Trusted_Connection=True;TrustServerCertificate=True;";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:DefaultConnection", ConnectionString);
        builder.UseSetting("Jwt:Key", "integration-tests-signing-key-0123456789abcdef0123456789");
        builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(Clock));
    }

    /// <summary>Mută „azi” la data dată (ora 10:00 în România).</summary>
    public void SetToday(DateOnly date) =>
        Clock.UtcNow = new DateTimeOffset(date.ToDateTime(new TimeOnly(8, 0)), TimeSpan.Zero);

    public void ResetToday() => Clock.UtcNow = TodayMorningUtc;

    public async Task InitializeAsync()
    {
        // Forțează pornirea aplicației: migrări + date demo.
        using var _ = CreateClient();
        await Task.CompletedTask;
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        using (var scope = Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureDeletedAsync();
        }
        await DisposeAsync();
    }
}
