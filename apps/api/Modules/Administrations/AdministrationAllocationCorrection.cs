using MedicationTracker.Api.Domain.Quantities;

namespace MedicationTracker.Api.Modules.Administrations;

/// <summary>
/// The audit record of "the system charged the wrong box, and here is what we did
/// about it".
/// </summary>
/// <remarks>
/// Written alongside the matched reversal and re-charge ledger entries rather than
/// instead of them, so the activity history can show the human-readable fact — "stock
/// source corrected from Box 1 to Box 2" — while the ledger carries the arithmetic.
/// </remarks>
public sealed class AdministrationAllocationCorrection
{
    private AdministrationAllocationCorrection() { }

    public AdministrationAllocationCorrection(
        Guid id,
        Guid householdId,
        Guid administrationEventId,
        Guid supersededAllocationId,
        Guid replacementAllocationId,
        Guid? fromPackageId,
        Guid? toPackageId,
        ExactQuantity quantity,
        Guid correlationId,
        Guid reversalLedgerEntryId,
        Guid consumeLedgerEntryId,
        Guid actorAccountId,
        DateTimeOffset recordedAt,
        string? reason = null)
    {
        if (!quantity.IsPositive)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "A correction moves a positive amount.");
        }

        if (fromPackageId == toPackageId)
        {
            throw new ArgumentException("A correction must move the amount to a different source.", nameof(toPackageId));
        }

        Id = id;
        HouseholdId = householdId;
        AdministrationEventId = administrationEventId;
        SupersededAllocationId = supersededAllocationId;
        ReplacementAllocationId = replacementAllocationId;
        FromPackageId = fromPackageId;
        ToPackageId = toPackageId;
        QuantityNumerator = quantity.Numerator;
        QuantityDenominator = quantity.Denominator;
        CorrelationId = correlationId;
        ReversalLedgerEntryId = reversalLedgerEntryId;
        ConsumeLedgerEntryId = consumeLedgerEntryId;
        ActorAccountId = actorAccountId;
        RecordedAt = recordedAt;
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }

    public Guid Id { get; private set; }

    public Guid HouseholdId { get; private set; }

    public Guid AdministrationEventId { get; private set; }

    public Guid SupersededAllocationId { get; private set; }

    public Guid ReplacementAllocationId { get; private set; }

    /// <summary>The package wrongly charged, or null when loose stock was wrongly charged.</summary>
    public Guid? FromPackageId { get; private set; }

    /// <summary>The package really used, or null when loose stock was really used.</summary>
    public Guid? ToPackageId { get; private set; }

    public long QuantityNumerator { get; private set; }

    public long QuantityDenominator { get; private set; }

    public ExactQuantity Quantity => new(QuantityNumerator, QuantityDenominator);

    /// <summary>Ties this record to its reversal and re-charge ledger entries.</summary>
    public Guid CorrelationId { get; private set; }

    public Guid ReversalLedgerEntryId { get; private set; }

    public Guid ConsumeLedgerEntryId { get; private set; }

    public Guid ActorAccountId { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    public string? Reason { get; private set; }
}
