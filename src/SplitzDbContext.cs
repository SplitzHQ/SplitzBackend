using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SplitzBackend.Models;

namespace SplitzBackend;

public class SplitzDbContext(DbContextOptions<SplitzDbContext> options) : IdentityDbContext<SplitzUser>(options)
{
    public DbSet<Group> Groups { get; set; } = null!;

    public DbSet<GroupJoinLink> GroupJoinLinks { get; set; } = null!;

    public DbSet<Transaction> Transactions { get; set; } = null!;

    public DbSet<TransactionDraft> TransactionDrafts { get; set; } = null!;

    public DbSet<Invoice> Invoices { get; set; } = null!;

    public DbSet<Notification> Notifications { get; set; } = null!;

    public DbSet<NotificationPreference> NotificationPreferences { get; set; } = null!;

    public DbSet<FriendRequest> FriendRequests { get; set; } = null!;

    public DbSet<GroupInvite> GroupInvites { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<SplitzUser>()
            .HasMany(e => e.Friends)
            .WithOne(e => e.User)
            .HasForeignKey(e => e.UserId);

        builder.Entity<SplitzUser>()
            .HasMany(e => e.Balances)
            .WithOne(e => e.User)
            .HasForeignKey(e => e.UserId);

        // Group-scoped preferences and invites go away with the group.
        builder.Entity<NotificationPreference>()
            .HasOne(p => p.Group)
            .WithMany()
            .HasForeignKey(p => p.GroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<GroupInvite>()
            .HasOne(i => i.Group)
            .WithMany()
            .HasForeignKey(i => i.GroupId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    /// <summary>
    ///     Treat every DateTime column as a UTC instant. Applies to DateTime and DateTime? alike,
    ///     because EF never passes null through a converter.
    /// </summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }
}

/// <summary>
///     SQLite stores DateTime as text with no offset and reads it back as Unspecified, so the "Z" is lost
///     when the value is serialized. This converter normalizes Local values to UTC on write and stamps
///     Kind = Utc on read, so every DateTime leaving the context is a real UTC instant.
///     See https://github.com/dotnet/efcore/issues/4711.
/// </summary>
public sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    v => v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : v,
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc));