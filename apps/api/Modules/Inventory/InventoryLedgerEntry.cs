using MedicationTracker.Api.Domain.Inventory;
using MedicationTracker.Api.Domain.Quantities;

namespace MedicationTracker.Api.Modules.Inventory;

/// <summary>
/// One append-only signed change to stock. The only source of truth for how much of a
/// medication the household has.
/// </summary>
/// <remarks>
/// <para>
/// A package's balance is the sum of its entries; a medication's total is the sum of
/// all its packages plus its package-independent entries. Nothing caches a running
/// total, so nothing can drift from the ledger.
/// </para>
/// <para>
/// Entries are never updated or deleted. A mistake is corrected by appending a
/// reversal that points back at the entry it undoes, which is what preserves the
/// audit trail the product promises.
/// </para>
/// </remarks>
public sealed class InventoryLedgerEntry
{
    private InventoryLedgerEntry() { }

    private InventoryLedgerEntry(
        Guid id,
        Guid householdId,
        Guid medicationDefinitionId,
        Guid legacyInventoryItemId,
        Guid? packageId,
        ExactQuantity signedQuantity,
        LedgerEntryType entryType,
        Guid correlationId,
        Guid? administrationEventId,
        Guid? reversesEntryId,
        Guid? actorAccountId,
        string? reason,
        DateTimeOffset occurredAt,
        DateTimeOffset recordedAt)
    {
        // Two entry types can legitimately carry a zero delta: a count that found no
        // discrepancy, and an acquisition that added nothing — the superseded model let a
        // medication be created with zero stock, and production holds such rows. For a
        // consumption, loss or disposal a zero delta is a stock change that changed
        // nothing, which hides a bug.
        //
        // The factory methods below are stricter than this: no new code can write a zero
        // acquisition. This constructor describes what is representable, including
        // history.
        if (signedQuantity.IsZero
            && entryType is not (LedgerEntryType.CountAdjustment or LedgerEntryType.Acquire))
        {
            throw new ArgumentOutOfRangeException(
                nameof(signedQuantity),
                $"A {entryType} entry must change stock by a non-zero amount.");
        }

        if (LedgerEntryTypes.IsAlwaysNegative(entryType) && signedQuantity.IsPositive)
        {
            throw new ArgumentOutOfRangeException(
                nameof(signedQuantity),
                $"A {entryType} entry must reduce stock.");
        }

        if (LedgerEntryTypes.IsAlwaysPositive(entryType) && signedQuantity.IsNegative)
        {
            throw new ArgumentOutOfRangeException(
                nameof(signedQuantity),
                $"A {entryType} entry must increase stock.");
        }

        Id = id;
        HouseholdId = householdId;
        MedicationDefinitionId = medicationDefinitionId;
        LegacyInventoryItemId = legacyInventoryItemId;
        PackageId = packageId;
        QuantityNumerator = signedQuantity.Numerator;
        QuantityDenominator = signedQuantity.Denominator;
        EntryType = entryType;
        CorrelationId = correlationId;
        AdministrationEventId = administrationEventId;
        ReversesEntryId = reversesEntryId;
        ActorAccountId = actorAccountId;
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        OccurredAt = occurredAt;
        RecordedAt = recordedAt;
    }

    public Guid Id { get; private set; }

    public Guid HouseholdId { get; private set; }

    public Guid MedicationDefinitionId { get; private set; }

    /// <summary>Legacy inventory-item foreign key, retained so historical rows stay valid.</summary>
    public Guid LegacyInventoryItemId { get; private set; }

    /// <summary>The physical package affected, or null for package-independent stock.</summary>
    public Guid? PackageId { get; private set; }

    public long QuantityNumerator { get; private set; }

    public long QuantityDenominator { get; private set; }

    /// <summary>Signed: negative reduces stock, positive increases it.</summary>
    public ExactQuantity Quantity => new(QuantityNumerator, QuantityDenominator);

    public LedgerEntryType EntryType { get; private set; }

    /// <summary>
    /// Groups every entry written as one logical act — the several packages one dose
    /// spanned, or the reversal and re-charge of one correction.
    /// </summary>
    public Guid CorrelationId { get; private set; }

    public Guid? AdministrationEventId { get; private set; }

    /// <summary>The entry this one undoes, set only on a correction reversal.</summary>
    public Guid? ReversesEntryId { get; private set; }

    public Guid? ActorAccountId { get; private set; }

    /// <summary>Optional human note. Never parsed; <see cref="EntryType"/> carries the meaning.</summary>
    public string? Reason { get; private set; }

    /// <summary>When the stock change happened in the real world.</summary>
    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>When the system learned about it. Differs from <see cref="OccurredAt"/> after offline sync.</summary>
    public DateTimeOffset RecordedAt { get; private set; }

    public static InventoryLedgerEntry Acquire(
        Guid householdId,
        Guid medicationDefinitionId,
        Guid legacyInventoryItemId,
        Guid? packageId,
        ExactQuantity amount,
        Guid correlationId,
        Guid actorAccountId,
        DateTimeOffset occurredAt,
        DateTimeOffset recordedAt,
        string? reason = null) =>
        new(Guid.CreateVersion7(), householdId, medicationDefinitionId, legacyInventoryItemId, packageId,
            Require(amount, positive: true), LedgerEntryType.Acquire, correlationId, null, null,
            actorAccountId, reason, occurredAt, recordedAt);

    public static InventoryLedgerEntry Consume(
        Guid householdId,
        Guid medicationDefinitionId,
        Guid legacyInventoryItemId,
        Guid? packageId,
        ExactQuantity amount,
        Guid correlationId,
        Guid administrationEventId,
        Guid actorAccountId,
        DateTimeOffset occurredAt,
        DateTimeOffset recordedAt) =>
        new(Guid.CreateVersion7(), householdId, medicationDefinitionId, legacyInventoryItemId, packageId,
            -Require(amount, positive: true), LedgerEntryType.Consume, correlationId, administrationEventId, null,
            actorAccountId, null, occurredAt, recordedAt);

    /// <summary>Credits back a package that was charged in error, naming the entry it undoes.</summary>
    public static InventoryLedgerEntry CorrectionReversal(
        Guid householdId,
        Guid medicationDefinitionId,
        Guid legacyInventoryItemId,
        Guid? packageId,
        ExactQuantity amount,
        Guid correlationId,
        Guid administrationEventId,
        Guid? reversesEntryId,
        Guid actorAccountId,
        DateTimeOffset occurredAt,
        DateTimeOffset recordedAt,
        string? reason = null) =>
        new(Guid.CreateVersion7(), householdId, medicationDefinitionId, legacyInventoryItemId, packageId,
            Require(amount, positive: true), LedgerEntryType.CorrectionReversal, correlationId,
            administrationEventId, reversesEntryId, actorAccountId, reason, occurredAt, recordedAt);

    /// <summary>Charges the package the dose really came from, paired with a reversal.</summary>
    public static InventoryLedgerEntry CorrectionConsume(
        Guid householdId,
        Guid medicationDefinitionId,
        Guid legacyInventoryItemId,
        Guid? packageId,
        ExactQuantity amount,
        Guid correlationId,
        Guid administrationEventId,
        Guid actorAccountId,
        DateTimeOffset occurredAt,
        DateTimeOffset recordedAt,
        string? reason = null) =>
        new(Guid.CreateVersion7(), householdId, medicationDefinitionId, legacyInventoryItemId, packageId,
            -Require(amount, positive: true), LedgerEntryType.CorrectionConsume, correlationId,
            administrationEventId, null, actorAccountId, reason, occurredAt, recordedAt);

    /// <summary>
    /// A stock change the user recorded directly: found, lost, discarded, counted, or
    /// moved between packages. The sign is supplied by the caller and validated
    /// against the entry type.
    /// </summary>
    public static InventoryLedgerEntry Adjustment(
        Guid householdId,
        Guid medicationDefinitionId,
        Guid legacyInventoryItemId,
        Guid? packageId,
        ExactQuantity signedAmount,
        LedgerEntryType entryType,
        Guid correlationId,
        Guid actorAccountId,
        DateTimeOffset occurredAt,
        DateTimeOffset recordedAt,
        string? reason = null) =>
        new(Guid.CreateVersion7(), householdId, medicationDefinitionId, legacyInventoryItemId, packageId,
            signedAmount, entryType, correlationId, null, null, actorAccountId, reason, occurredAt, recordedAt);

    /// <summary>
    /// Puts back the stock a retirement removed, naming the entry it undoes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Retiring a package writes a negative entry for whatever was left in it. Finding
    /// that box again has to write the symmetric positive one, or the household's total
    /// stays short by an amount that was never actually gone — and the ledger is the only
    /// thing that knows how much stock there is.
    /// </para>
    /// <para>
    /// The link back to the retirement entry is what makes the pair readable later.
    /// Without it the history shows a loss and then an unexplained windfall, which is the
    /// shape of a mistake rather than of a correction. Nothing is deleted or rewritten:
    /// the loss really was recorded, and this says it was undone.
    /// </para>
    /// </remarks>
    public static InventoryLedgerEntry Reinstatement(
        Guid householdId,
        Guid medicationDefinitionId,
        Guid legacyInventoryItemId,
        Guid? packageId,
        ExactQuantity amount,
        Guid correlationId,
        Guid reversesEntryId,
        Guid actorAccountId,
        DateTimeOffset occurredAt,
        DateTimeOffset recordedAt,
        string? reason = null) =>
        new(Guid.CreateVersion7(), householdId, medicationDefinitionId, legacyInventoryItemId, packageId,
            Require(amount, positive: true), LedgerEntryType.Found, correlationId,
            null, reversesEntryId, actorAccountId, reason, occurredAt, recordedAt);

    private static ExactQuantity Require(ExactQuantity amount, bool positive)
    {
        if (positive && !amount.IsPositive)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "The amount must be positive.");
        }

        return amount;
    }
}
