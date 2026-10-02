using MedicationTracker.Api.Modules.Administrations;
using MedicationTracker.Api.Modules.Audit;
using MedicationTracker.Api.Modules.Catalog;
using MedicationTracker.Api.Modules.Households;
using MedicationTracker.Api.Modules.Identity;
using MedicationTracker.Api.Modules.Inventory;
using MedicationTracker.Api.Modules.People;
using MedicationTracker.Api.Modules.Refill;
using MedicationTracker.Api.Modules.Subscriptions;
using MedicationTracker.Api.Modules.Sync;
using MedicationTracker.Api.Modules.Treatments;
using Microsoft.EntityFrameworkCore;

namespace MedicationTracker.Api.Persistence;

/// <summary>
/// The single context for this modular monolith. Sets are grouped by module so the
/// boundaries are visible here even though they share one database and one transaction.
/// </summary>
public sealed class MedicationTrackerDbContext(DbContextOptions<MedicationTrackerDbContext> options)
    : DbContext(options)
{
    // Identity and access
    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<Household> Households => Set<Household>();

    public DbSet<HouseholdMembership> HouseholdMemberships => Set<HouseholdMembership>();

    public DbSet<Subscription> Subscriptions => Set<Subscription>();

    public DbSet<Entitlement> Entitlements => Set<Entitlement>();

    // People
    public DbSet<Person> People => Set<Person>();

    // Medication catalog
    public DbSet<MedicationDefinition> MedicationDefinitions => Set<MedicationDefinition>();

    // Inventory and physical packages
    public DbSet<LegacyInventoryItem> LegacyInventoryItems => Set<LegacyInventoryItem>();

    public DbSet<MedicationPackage> Packages => Set<MedicationPackage>();

    public DbSet<InventoryLedgerEntry> LedgerEntries => Set<InventoryLedgerEntry>();

    public DbSet<PackageAssignmentEvent> PackageAssignmentEvents => Set<PackageAssignmentEvent>();

    public DbSet<PackageLoan> PackageLoans => Set<PackageLoan>();

    public DbSet<InventoryCountBatch> InventoryCountBatches => Set<InventoryCountBatch>();

    public DbSet<InventoryCount> InventoryCounts => Set<InventoryCount>();

    // Treatments and scheduling
    public DbSet<TreatmentPlan> TreatmentPlans => Set<TreatmentPlan>();

    public DbSet<TreatmentPlanVersion> TreatmentPlanVersions => Set<TreatmentPlanVersion>();

    // Administrations and allocations
    public DbSet<AdministrationEvent> AdministrationEvents => Set<AdministrationEvent>();

    public DbSet<AdministrationAllocation> AdministrationAllocations => Set<AdministrationAllocation>();

    public DbSet<AdministrationAllocationCorrection> AllocationCorrections =>
        Set<AdministrationAllocationCorrection>();

    // Refill
    public DbSet<MedicationRefillPolicy> RefillPolicies => Set<MedicationRefillPolicy>();

    // Audit
    public DbSet<MedicationDefinitionChangeEvent> MedicationDefinitionChangeEvents =>
        Set<MedicationDefinitionChangeEvent>();

    public DbSet<TreatmentPlanChangeEvent> TreatmentPlanChangeEvents => Set<TreatmentPlanChangeEvent>();

    // Offline sync
    public DbSet<SyncCommandReceipt> SyncCommandReceipts => Set<SyncCommandReceipt>();

    public DbSet<ProcessedAdministrationCommand> ProcessedAdministrationCommands =>
        Set<ProcessedAdministrationCommand>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MedicationTrackerDbContext).Assembly);

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
