using MedicationTracker.Api.Domain.Quantities;
using MedicationTracker.Api.Domain.Scheduling;

namespace MedicationTracker.Api.Domain.Refill;

/// <summary>
/// One plan's contribution to future consumption of a medication.
/// </summary>
public sealed record PlannedConsumption(RecurrenceSpecification Recurrence, ExactQuantity DosePerOccurrence);

/// <summary>
/// The household's warning settings, as plain values so the forecast stays pure.
/// </summary>
public sealed record RefillSettings(
    ExactQuantity? LowStockThreshold,
    int? LowStockDays,
    DateOnly? NextEligibleRefillOn)
{
    public static RefillSettings None => new(null, null, null);
}

/// <summary>Why a medication is flagged as low.</summary>
public enum LowStockTrigger
{
    None = 0,

    /// <summary>The remaining amount is at or below the configured threshold.</summary>
    BelowThreshold = 1,

    /// <summary>Fewer than the configured number of days of planned doses remain.</summary>
    WithinDayHorizon = 2,

    /// <summary>Stock has already run out.</summary>
    AlreadyDepleted = 3,
}

/// <summary>
/// An explainable projection of when a medication runs out, whether that is soon, and
/// whether it happens before the prescription may be refilled.
/// </summary>
/// <param name="ProjectedDepletionOn">
/// The first local day on which the planned doses cannot be covered in full, or null
/// when nothing consumes this medication on a schedule.
/// </param>
/// <param name="DaysOfStockRemaining">
/// Whole days from the forecast date until depletion. Zero means today's doses already
/// cannot be covered.
/// </param>
/// <param name="RefillGapDays">
/// How many days the household would be without medication: the distance from
/// projected depletion to refill eligibility. Null when there is no gap or either date
/// is unknown.
/// </param>
public sealed record DepletionForecast(
    ExactQuantity Balance,
    bool IsForecastable,
    DateOnly? ProjectedDepletionOn,
    int? DaysOfStockRemaining,
    LowStockTrigger LowStockTrigger,
    DateOnly? NextEligibleRefillOn,
    int? RefillGapDays)
{
    public bool IsLowStock => LowStockTrigger != LowStockTrigger.None;

    /// <summary>
    /// True when stock is projected to run out before the prescription may be refilled.
    /// This is the warning the owner asked for, and it only exists because physical
    /// depletion and official eligibility are modelled separately.
    /// </summary>
    public bool HasRefillGap => RefillGapDays is > 0;
}

/// <summary>
/// Projects remaining stock forward over the real due-date pattern of each plan.
/// </summary>
/// <remarks>
/// <para>
/// Consumption is walked day by day over actual due days rather than divided by an
/// average daily rate: a Monday/Thursday plan and an every-third-day plan do not
/// consume a constant amount per day, and treating them as if they did would put the
/// depletion date in the wrong place.
/// </para>
/// <para>
/// As-needed plans contribute nothing, because how often they will be used is unknown.
/// A medication with only as-needed plans is reported as not forecastable rather than
/// given a fabricated date.
/// </para>
/// <para>
/// This projects supply. It is not a clinical prediction and it never recommends taking
/// or withholding a dose.
/// </para>
/// </remarks>
public static class RefillForecast
{
    /// <summary>Default projection horizon. Beyond this the answer is "not soon".</summary>
    public const int DefaultHorizonDays = 3650;

    public static DepletionForecast Project(
        ExactQuantity balance,
        IReadOnlyCollection<PlannedConsumption> plans,
        DateOnly from,
        RefillSettings settings,
        int horizonDays = DefaultHorizonDays)
    {
        ArgumentNullException.ThrowIfNull(plans);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(horizonDays);

        var forecastable = plans
            .Where(plan => plan.Recurrence.Kind == TreatmentKind.Scheduled && plan.DosePerOccurrence.IsPositive)
            .ToList();

        if (forecastable.Count == 0)
        {
            return new DepletionForecast(
                balance,
                IsForecastable: false,
                ProjectedDepletionOn: null,
                DaysOfStockRemaining: null,
                LowStockTrigger: ThresholdOnlyTrigger(balance, settings),
                NextEligibleRefillOn: settings.NextEligibleRefillOn,
                RefillGapDays: null);
        }

        var depletionOn = FindFirstUncoveredDay(balance, forecastable, from, horizonDays);
        var daysRemaining = depletionOn is { } depletion ? depletion.DayNumber - from.DayNumber : (int?)null;

        return new DepletionForecast(
            balance,
            IsForecastable: true,
            ProjectedDepletionOn: depletionOn,
            DaysOfStockRemaining: daysRemaining,
            LowStockTrigger: DetermineTrigger(balance, daysRemaining, settings),
            NextEligibleRefillOn: settings.NextEligibleRefillOn,
            RefillGapDays: GapDays(depletionOn, settings.NextEligibleRefillOn));
    }

    /// <summary>
    /// Walks forward to the first day whose planned doses the remaining balance cannot
    /// cover in full. Returns null when the balance survives the whole horizon.
    /// </summary>
    private static DateOnly? FindFirstUncoveredDay(
        ExactQuantity balance,
        IReadOnlyCollection<PlannedConsumption> plans,
        DateOnly from,
        int horizonDays)
    {
        var remaining = balance.IsPositive ? balance : ExactQuantity.Zero;

        for (var offset = 0; offset < horizonDays; offset++)
        {
            var day = from.AddDays(offset);
            var required = ExactQuantity.Sum(plans
                .Where(plan => RecurrenceRule.IsDue(plan.Recurrence, day))
                .Select(plan => plan.DosePerOccurrence));

            if (required.IsZero)
            {
                continue;
            }

            if (remaining < required)
            {
                return day;
            }

            remaining -= required;
        }

        return null;
    }

    /// <summary>
    /// Low-stock evaluation when no schedule exists: only an explicit amount threshold
    /// can fire, because there is no day count to compare against.
    /// </summary>
    private static LowStockTrigger ThresholdOnlyTrigger(ExactQuantity balance, RefillSettings settings)
    {
        if (!balance.IsPositive)
        {
            return LowStockTrigger.AlreadyDepleted;
        }

        return settings.LowStockThreshold is { } threshold && balance <= threshold
            ? LowStockTrigger.BelowThreshold
            : LowStockTrigger.None;
    }

    private static LowStockTrigger DetermineTrigger(
        ExactQuantity balance,
        int? daysRemaining,
        RefillSettings settings)
    {
        if (!balance.IsPositive || daysRemaining is 0)
        {
            return LowStockTrigger.AlreadyDepleted;
        }

        if (settings.LowStockThreshold is { } threshold && balance <= threshold)
        {
            return LowStockTrigger.BelowThreshold;
        }

        return settings.LowStockDays is { } horizon && daysRemaining is { } days && days <= horizon
            ? LowStockTrigger.WithinDayHorizon
            : LowStockTrigger.None;
    }

    private static int? GapDays(DateOnly? depletionOn, DateOnly? eligibleOn)
    {
        if (depletionOn is not { } depletion || eligibleOn is not { } eligible)
        {
            return null;
        }

        var gap = eligible.DayNumber - depletion.DayNumber;
        return gap > 0 ? gap : null;
    }
}
