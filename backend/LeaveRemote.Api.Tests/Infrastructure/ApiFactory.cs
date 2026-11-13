using LeaveRemote.Api.Data;
using LeaveRemote.Api.Services.Notifications;
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

    /// <summary>Emailurile „trimise” de aplicație în timpul testelor.</summary>
    public FakeEmailSender Emails { get; } = new();

    // Timp de conectare generos: la prima conectare LocalDB poate avea nevoie de câteva secunde ca să pornească.
    public string ConnectionString =>
        $"Server=(localdb)\\MSSQLLocalDB;Database={_databaseName};Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=60;";

    /// <summary>
    /// LocalDB se oprește singur după o perioadă de inactivitate. Îl pornim o singură dată, înainte ca toate
    /// clasele de teste să se conecteze în paralel; altfel conectările simultane pot depăși timpul de așteptare.
    /// </summary>
    private static readonly Lazy<Task> LocalDbStarted = new(StartLocalDbAsync);

    private static async Task StartLocalDbAsync()
    {
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "sqllocaldb",
                Arguments = "start MSSQLLocalDB",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });
            if (process != null) await process.WaitForExitAsync();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // sqllocaldb nu e în PATH: conexiunea va porni oricum instanța automat (doar mai lent).
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:DefaultConnection", ConnectionString);
        builder.UseSetting("Jwt:Key", "integration-tests-signing-key-0123456789abcdef0123456789");
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton<IEmailSender>(Emails);
        });
    }

    /// <summary>Mută „azi” la data dată (ora 10:00 în România).</summary>
    public void SetToday(DateOnly date) =>
        Clock.UtcNow = new DateTimeOffset(date.ToDateTime(new TimeOnly(8, 0)), TimeSpan.Zero);

    public void ResetToday() => Clock.UtcNow = TodayMorningUtc;

    public async Task InitializeAsync()
    {
        await LocalDbStarted.Value;
        // Forțează pornirea aplicației: migrări + date demo.
        using var _ = CreateClient();
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
