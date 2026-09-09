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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MedicationTrackerDbContext).Assembly);
    }
}
