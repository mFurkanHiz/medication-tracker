using MedicationTracker.Api.Domain.Quantities;

namespace MedicationTracker.Api.Domain.Inventory;

public sealed record InventoryCountReconciliation(
    Guid CountSessionId,
    Guid InventoryItemId,
    ExactQuantity CalculatedBeforeCount,
    ExactQuantity Observed,
    ExactQuantity Adjustment,
    int Revision)
{
    public static InventoryCountReconciliation Create(
        Guid countSessionId,
        Guid inventoryItemId,
        ExactQuantity calculatedBeforeCount,
        ExactQuantity observed,
        int revision = 1)
    {
        if (revision < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(revision));
        }

        return new(
            countSessionId,
            inventoryItemId,
            calculatedBeforeCount,
            observed,
            observed - calculatedBeforeCount,
            revision);
    }
}
