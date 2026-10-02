using MedicationTracker.Api.Domain.Quantities;

namespace MedicationTracker.Api.Modules.Inventory;

/// <summary>
/// One accepted counting session across any number of medications.
/// </summary>
/// <remarks>
/// A correction to an accepted count never edits it. It creates a new batch that points
/// back at the one it revises, forming an immutable chain, so what the household
/// believed at each point stays recoverable.
/// </remarks>
public sealed class InventoryCountBatch
{
    private InventoryCountBatch() { }

    public InventoryCountBatch(
        Guid id,
        Guid householdId,
        Guid accountId,
        Guid? previousBatchId,
        int revisionNumber,
        DateTimeOffset acceptedAt)
    {
        if (revisionNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(revisionNumber), "Revisions start at one.");
        }

        Id = id;
        HouseholdId = householdId;
        AccountId = accountId;
        PreviousBatchId = previousBatchId;
        RevisionNumber = revisionNumber;
        AcceptedAt = acceptedAt;
    }

    public Guid Id { get; private set; }

    public Guid HouseholdId { get; private set; }

    public Guid AccountId { get; private set; }

    /// <summary>The batch this one corrects. Null for an original count.</summary>
    public Guid? PreviousBatchId { get; private set; }

    public int RevisionNumber { get; private set; }

    public DateTimeOffset AcceptedAt { get; private set; }
}

/// <summary>
/// One medication's line within a counting session: what the ledger said, what the
/// household actually counted, and the adjusting entry that reconciled the two.
/// </summary>
public sealed class InventoryCount
{
    private InventoryCount() { }

    public InventoryCount(
        Guid id,
        Guid householdId,
        Guid? batchId,
        Guid legacyInventoryItemId,
        Guid accountId,
        ExactQuantity before,
        ExactQuantity observed,
        Guid ledgerEntryId,
        DateTimeOffset acceptedAt,
        Guid? packageId = null)
    {
        if (observed.IsNegative)
        {
            throw new ArgumentOutOfRangeException(nameof(observed), "A counted amount cannot be negative.");
        }

        Id = id;
        HouseholdId = householdId;
        BatchId = batchId;
        LegacyInventoryItemId = legacyInventoryItemId;
        AccountId = accountId;
        BeforeNumerator = before.Numerator;
        BeforeDenominator = before.Denominator;
        ObservedNumerator = observed.Numerator;
        ObservedDenominator = observed.Denominator;
        LedgerEntryId = ledgerEntryId;
        AcceptedAt = acceptedAt;
        PackageId = packageId;
    }

    public Guid Id { get; private set; }

    public Guid HouseholdId { get; private set; }

    public Guid? BatchId { get; private set; }

    public Guid LegacyInventoryItemId { get; private set; }

    /// <summary>
    /// Set when an advanced user counted one specific package rather than the
    /// medication as a whole. Null for the default medication-level count.
    /// </summary>
    public Guid? PackageId { get; private set; }

    public Guid AccountId { get; private set; }

    public long BeforeNumerator { get; private set; }

    public long BeforeDenominator { get; private set; }

    /// <summary>What the ledger projected immediately before the count was accepted.</summary>
    public ExactQuantity Before => new(BeforeNumerator, BeforeDenominator);

    public long ObservedNumerator { get; private set; }

    public long ObservedDenominator { get; private set; }

    /// <summary>What the household physically counted.</summary>
    public ExactQuantity Observed => new(ObservedNumerator, ObservedDenominator);

    /// <summary>The signed difference the adjusting ledger entry carries.</summary>
    public ExactQuantity Adjustment => Observed - Before;

    public Guid LedgerEntryId { get; private set; }

    public DateTimeOffset AcceptedAt { get; private set; }
}
