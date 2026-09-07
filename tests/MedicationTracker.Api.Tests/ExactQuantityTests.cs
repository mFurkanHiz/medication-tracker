using MedicationTracker.Api.Domain.Inventory;
using MedicationTracker.Api.Domain.Quantities;

namespace MedicationTracker.Api.Tests;

public sealed class ExactQuantityTests
{
    [Fact]
    public void Fractions_are_reduced_and_added_exactly()
    {
        var quantity = new ExactQuantity(1, 2) + new ExactQuantity(1, 4);

        Assert.Equal(new ExactQuantity(3, 4), quantity);
    }

    [Fact]
    public void Count_reconciliation_preserves_the_observed_difference()
    {
        var reconciliation = InventoryCountReconciliation.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            calculatedBeforeCount: new ExactQuantity(25),
            observed: new ExactQuantity(23));

        Assert.Equal(new ExactQuantity(-2), reconciliation.Adjustment);
        Assert.Equal(1, reconciliation.Revision);
    }
}
