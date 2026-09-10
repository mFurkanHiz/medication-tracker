using MedicationTracker.Api.Modules.Households;
using MedicationTracker.Api.Modules.Identity;
using MedicationTracker.Api.Modules.Subscriptions;
using MedicationTracker.Api.Modules.Care;
using Microsoft.EntityFrameworkCore;

namespace MedicationTracker.Api.Persistence;

public sealed class MedicationTrackerDbContext(DbContextOptions<MedicationTrackerDbContext> options)
    : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<Household> Households => Set<Household>();

    public DbSet<HouseholdMembership> HouseholdMemberships => Set<HouseholdMembership>();

    public DbSet<Subscription> Subscriptions => Set<Subscription>();

    public DbSet<Entitlement> Entitlements => Set<Entitlement>();
    public DbSet<Person> People => Set<Person>();
    public DbSet<Medication> Medications => Set<Medication>();
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<InventoryLedgerEntry> InventoryLedgerEntries => Set<InventoryLedgerEntry>();
    public DbSet<Regimen> Regimens => Set<Regimen>();
    public DbSet<RegimenVersion> RegimenVersions => Set<RegimenVersion>();
    public DbSet<AdministrationEvent> AdministrationEvents => Set<AdministrationEvent>();
    public DbSet<ProcessedAdministrationCommand> ProcessedAdministrationCommands => Set<ProcessedAdministrationCommand>();
    public DbSet<SyncCommandReceipt> SyncCommandReceipts => Set<SyncCommandReceipt>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MedicationTrackerDbContext).Assembly);
        modelBuilder.Entity<InventoryCount>(b =>
        {
            b.ToTable("count_sessions", "inventory");
            b.HasKey(x => x.Id);
            b.HasOne<Household>().WithMany().HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<InventoryItem>().WithMany().HasForeignKey(x => x.InventoryItemId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<InventoryLedgerEntry>().WithMany().HasForeignKey(x => x.LedgerEntryId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<AccountSession>(b =>
        {
            b.ToTable("sessions", "identity");
            b.HasKey(x => x.TokenHash);
            b.Property(x => x.TokenHash).HasMaxLength(64);
            b.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => x.ExpiresAt);
        });
    }
}
