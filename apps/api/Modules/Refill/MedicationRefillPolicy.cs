using MedicationTracker.Api.Domain.Quantities;

namespace MedicationTracker.Api.Modules.Refill;

/// <summary>
/// The household's own refill settings for one medication: when to warn that stock is
/// running out, and the date the prescription may next be filled.
/// </summary>
/// <remarks>
/// <para>
/// Physical depletion and official refill eligibility are deliberately separate
/// fields. Having twelve tablets left says nothing about whether the pharmacy will
/// dispense more today, and the gap between the two is precisely what the product
/// needs to warn about.
/// </para>
/// <para>
/// Both values are entered by the user from their own prescription. The product never
/// infers an eligibility date and never contacts a pharmacy or health system.
/// </para>
/// </remarks>
public sealed class MedicationRefillPolicy
{
    private MedicationRefillPolicy() { }

    public MedicationRefillPolicy(
        Guid id,
        Guid householdId,
        Guid medicationDefinitionId,
        DateTimeOffset updatedAt,
        Guid updatedByAccountId)
    {
        Id = id;
        HouseholdId = householdId;
        MedicationDefinitionId = medicationDefinitionId;
        UpdatedAt = updatedAt;
        UpdatedByAccountId = updatedByAccountId;
    }

    public Guid Id { get; private set; }

    public Guid HouseholdId { get; private set; }

    public Guid MedicationDefinitionId { get; private set; }

    public long? LowStockThresholdNumerator { get; private set; }

    public long? LowStockThresholdDenominator { get; private set; }

    /// <summary>Warn when the remaining total falls to or below this amount.</summary>
    public ExactQuantity? LowStockThreshold =>
        LowStockThresholdNumerator is { } numerator && LowStockThresholdDenominator is { } denominator
            ? new ExactQuantity(numerator, denominator)
            : null;

    /// <summary>
    /// Warn when fewer than this many days of planned doses remain. Independent of, and
    /// combinable with, <see cref="LowStockThreshold"/>: whichever triggers first wins.
    /// </summary>
    public int? LowStockDays { get; private set; }

    /// <summary>
    /// The earliest date the prescription may be filled again, as the user read it off
    /// their own prescription. Never derived from stock.
    /// </summary>
    public DateOnly? NextEligibleRefillOn { get; private set; }

    public string? Note { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public Guid UpdatedByAccountId { get; private set; }

    public void Update(
        ExactQuantity? lowStockThreshold,
        int? lowStockDays,
        DateOnly? nextEligibleRefillOn,
        string? note,
        DateTimeOffset updatedAt,
        Guid updatedByAccountId)
    {
        if (lowStockThreshold is { } threshold && threshold.IsNegative)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lowStockThreshold),
                "A low-stock threshold cannot be negative.");
        }

        if (lowStockDays is < 0 or > 365)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lowStockDays),
                "A low-stock horizon must be between zero and 365 days.");
        }

        LowStockThresholdNumerator = lowStockThreshold?.Numerator;
        LowStockThresholdDenominator = lowStockThreshold?.Denominator;
        LowStockDays = lowStockDays;
        NextEligibleRefillOn = nextEligibleRefillOn;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        UpdatedAt = updatedAt;
        UpdatedByAccountId = updatedByAccountId;
    }
}
