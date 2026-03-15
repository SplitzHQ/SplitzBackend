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

    public DbSet<Invoice> Invoices { get; set; } = null!;

    public DbSet<Notification> Notifications { get; set; } = null!;

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

        builder.Entity<Invoice>()
            .HasOne(e => e.Group)
            .WithMany()
            .HasForeignKey(e => e.GroupId);

        builder.Entity<Invoice>()
            .HasOne(e => e.CreatedBy)
            .WithMany()
            .HasForeignKey(e => e.CreatedByUserId);

        builder.Entity<Invoice>()
            .HasMany(e => e.Transactions)
            .WithOne(e => e.Invoice)
            .HasForeignKey(e => e.InvoiceId);

        builder.Entity<Invoice>()
            .HasMany(e => e.Debts)
            .WithOne(e => e.Invoice)
            .HasForeignKey(e => e.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Invoice>()
            .HasMany(e => e.Settlements)
            .WithOne(e => e.Invoice)
            .HasForeignKey(e => e.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Notification>()
            .HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId);
    }
}