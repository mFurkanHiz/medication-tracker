using MedicationTracker.Api.Modules.Households;
using MedicationTracker.Api.Modules.Identity;
using MedicationTracker.Api.Modules.Subscriptions;
using MedicationTracker.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedicationTracker.Api.Tests;

public sealed class PersistenceModelTests
{
    private readonly MedicationTrackerDbContext _dbContext = new(
        new DbContextOptionsBuilder<MedicationTrackerDbContext>()
            .UseNpgsql("Host=localhost;Database=model_validation")
            .Options);

    [Fact]
    public void Identity_household_membership_and_subscription_are_separate_entities()
    {
        var model = _dbContext.Model;

        Assert.NotNull(model.FindEntityType(typeof(Account)));
        Assert.NotNull(model.FindEntityType(typeof(Household)));
        Assert.NotNull(model.FindEntityType(typeof(HouseholdMembership)));
        Assert.NotNull(model.FindEntityType(typeof(Subscription)));
        Assert.NotNull(model.FindEntityType(typeof(Entitlement)));
    }

    [Fact]
    public void Membership_history_key_includes_effective_start()
    {
        var membership = _dbContext.Model.FindEntityType(typeof(HouseholdMembership));
        var uniqueIndex = Assert.Single(
            membership!.GetIndexes(),
            index => index.IsUnique && index.Properties.Count == 3);

        Assert.Equal(
            [nameof(HouseholdMembership.HouseholdId), nameof(HouseholdMembership.AccountId), nameof(HouseholdMembership.ValidFrom)],
            uniqueIndex.Properties.Select(property => property.Name));
    }
}
