using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SplitzBackend.Models;

namespace SplitzBackend;

public class SplitzDbContext(DbContextOptions<SplitzDbContext> options) : IdentityDbContext<SplitzUser>(options)
{
    public DbSet<Group> Groups { get; set; } = null!;

    public DbSet<GroupJoinLink> GroupJoinLinks { get; set; } = null!;

    public DbSet<Transaction> Transactions { get; set; } = null!;

    public DbSet<TransactionDraft> TransactionDrafts { get; set; } = null!;

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
    }
}