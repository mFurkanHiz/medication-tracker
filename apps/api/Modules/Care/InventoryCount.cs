namespace MedicationTracker.Api.Modules.Care;

public sealed class InventoryCountBatch
{
    public Guid Id { get; set; }
    public Guid HouseholdId { get; set; }
    public Guid AccountId { get; set; }
    public Guid? PreviousBatchId { get; set; }
    public int RevisionNumber { get; set; }
    public DateTimeOffset AcceptedAt { get; set; }
}

public sealed class InventoryCount
{
    public Guid Id { get; set; }
    public Guid HouseholdId { get; set; }
    public Guid? BatchId { get; set; }
    public Guid InventoryItemId { get; set; }
    public Guid AccountId { get; set; }
    public long BeforeNumerator { get; set; }
    public long BeforeDenominator { get; set; }
    public long ObservedNumerator { get; set; }
    public long ObservedDenominator { get; set; }
    public Guid LedgerEntryId { get; set; }
    public DateTimeOffset AcceptedAt { get; set; }
}
