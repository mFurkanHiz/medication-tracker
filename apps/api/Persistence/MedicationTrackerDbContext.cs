using MedicationTracker.Api.Modules.Households;
using MedicationTracker.Api.Modules.Identity;
using MedicationTracker.Api.Modules.Subscriptions;
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MedicationTrackerDbContext).Assembly);
    }
}
