
using LeaveRemote.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeaveRemote.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();

    public DbSet<Team> Teams => Set<Team>();

    public DbSet<LeaveRequest> LeaveRequests => Set<LeaveRequest>();

    public DbSet<RemoteRequest> RemoteRequests => Set<RemoteRequest>();

    public DbSet<RemoteRequestDay> RemoteRequestDays => Set<RemoteRequestDay>();

    public DbSet<AnnualLeaveBalance> AnnualLeaveBalances => Set<AnnualLeaveBalance>();

    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    public DbSet<PublicHoliday> PublicHolidays => Set<PublicHoliday>();

    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();

protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);

    modelBuilder.Entity<User>()
        .HasOne<Team>()
        .WithMany()
        .HasForeignKey(user => user.TeamId)
        .OnDelete(DeleteBehavior.Restrict);

    
    modelBuilder.Entity<User>()
        .HasOne<User>()
        .WithMany()
        .HasForeignKey(user => user.ManagerId)
        .OnDelete(DeleteBehavior.Restrict);

    
    modelBuilder.Entity<LeaveRequest>()
        .HasOne<User>()
        .WithMany()
        .HasForeignKey(request => request.UserId)
        .OnDelete(DeleteBehavior.Restrict);

    
    modelBuilder.Entity<RemoteRequest>()
        .HasOne<User>()
        .WithMany()
        .HasForeignKey(request => request.UserId)
        .OnDelete(DeleteBehavior.Restrict);

    
    modelBuilder.Entity<RemoteRequestDay>()
        .HasOne<RemoteRequest>()
        .WithMany(request => request.Days)
        .HasForeignKey(day => day.RemoteRequestId)
        .OnDelete(DeleteBehavior.Cascade);

    
    modelBuilder.Entity<AnnualLeaveBalance>()
        .HasOne<User>()
        .WithMany()
        .HasForeignKey(balance => balance.UserId)
        .OnDelete(DeleteBehavior.Restrict);

    modelBuilder.Entity<AnnualLeaveBalance>()
        .HasIndex(balance => new { balance.UserId, balance.Year })
        .IsUnique();

    
    modelBuilder.Entity<AppSetting>()
        .HasIndex(setting => setting.Key)
        .IsUnique();

    modelBuilder.Entity<PublicHoliday>()
        .HasIndex(holiday => holiday.Date)
        .IsUnique();

    
    modelBuilder.Entity<User>()
        .HasIndex(user => user.Email)
        .IsUnique();

    // Utilizatorii existenți primesc o valoare aleatoare la migrare.
    modelBuilder.Entity<User>()
        .Property(user => user.SecurityStamp)
        .HasMaxLength(64)
        .HasDefaultValueSql("REPLACE(CONVERT(nvarchar(36), NEWID()), '-', '')");

    modelBuilder.Entity<PasswordResetToken>()
        .HasOne<User>()
        .WithMany()
        .HasForeignKey(token => token.UserId)
        .OnDelete(DeleteBehavior.Cascade);

    modelBuilder.Entity<PasswordResetToken>()
        .Property(token => token.TokenHash)
        .HasMaxLength(64);

    modelBuilder.Entity<PasswordResetToken>()
        .HasIndex(token => token.TokenHash)
        .IsUnique();


}

}
