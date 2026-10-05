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
/// Both values are entered by the user. The product never contacts a pharmacy or health
/// system; on request it suggests a date from the stock on hand and the planned use (see
/// <c>RefillForecast.SupplyRunsOutOn</c>), and the household confirms it by saving.
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

    /// <summary>
    /// When the household expects all of its stock to run out, as they recorded it. The
    /// forecast computes a projection; this is the date they chose to write down, which
    /// may be the projection or their own correction of it.
    /// </summary>
    public DateOnly? ExpectedDepletionOn { get; private set; }

    public string? Note { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public Guid UpdatedByAccountId { get; private set; }

    public void Update(
        ExactQuantity? lowStockThreshold,
        int? lowStockDays,
        DateOnly? nextEligibleRefillOn,
        DateOnly? expectedDepletionOn,
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
        ExpectedDepletionOn = expectedDepletionOn;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        UpdatedAt = updatedAt;
        UpdatedByAccountId = updatedByAccountId;
    }
}
