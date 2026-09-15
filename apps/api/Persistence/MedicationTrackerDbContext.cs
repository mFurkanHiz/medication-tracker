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
    public DbSet<InventoryPackage> InventoryPackages => Set<InventoryPackage>();
    public DbSet<InventoryPackageAssignmentEvent> InventoryPackageAssignmentEvents => Set<InventoryPackageAssignmentEvent>();
    public DbSet<InventoryCountBatch> InventoryCountBatches => Set<InventoryCountBatch>();
    public DbSet<InventoryCount> InventoryCounts => Set<InventoryCount>();
    public DbSet<MedicationChangeEvent> MedicationChangeEvents => Set<MedicationChangeEvent>();
    public DbSet<Regimen> Regimens => Set<Regimen>();
    public DbSet<RegimenVersion> RegimenVersions => Set<RegimenVersion>();
    public DbSet<RegimenChangeEvent> RegimenChangeEvents => Set<RegimenChangeEvent>();
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
            b.HasOne<InventoryCountBatch>().WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Household>().WithMany().HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<InventoryItem>().WithMany().HasForeignKey(x => x.InventoryItemId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<InventoryLedgerEntry>().WithMany().HasForeignKey(x => x.LedgerEntryId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.HouseholdId, x.BatchId });
            b.HasIndex(x => x.LedgerEntryId).IsUnique();
        });
        modelBuilder.Entity<InventoryCountBatch>(b =>
        {
            b.ToTable("count_batches", "inventory", table =>
                table.HasCheckConstraint("ck_count_batches_revision", "revision_number > 0"));
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.HouseholdId).HasColumnName("household_id");
            b.Property(x => x.AccountId).HasColumnName("account_id");
            b.Property(x => x.PreviousBatchId).HasColumnName("previous_batch_id");
            b.Property(x => x.RevisionNumber).HasColumnName("revision_number");
            b.Property(x => x.AcceptedAt).HasColumnName("accepted_at");
            b.HasOne<Household>().WithMany().HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<InventoryCountBatch>().WithMany().HasForeignKey(x => x.PreviousBatchId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.HouseholdId, x.AcceptedAt });
            b.HasIndex(x => x.PreviousBatchId).IsUnique();
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
