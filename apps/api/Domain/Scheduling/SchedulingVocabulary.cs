namespace MedicationTracker.Api.Domain.Scheduling;

/// <summary>Whether a plan has due times at all.</summary>
public enum TreatmentKind
{
    /// <summary>Due on specific days, at an exact time or within a named period.</summary>
    Scheduled = 0,

    /// <summary>Taken when needed. Has no due dates and is excluded from forecasting.</summary>
    AsNeeded = 1,
}

/// <summary>Which local calendar days a scheduled plan is due on.</summary>
public enum RecurrencePattern
{
    /// <summary>Every day inside the effective period.</summary>
    Daily = 0,

    /// <summary>Only the selected weekdays, as a Monday-first seven-bit mask.</summary>
    SelectedWeekdays = 1,

    /// <summary>Every N local days, anchored on the effective start date.</summary>
    EveryNDays = 2,
}

/// <summary>
/// A named part of the day, used when the household does not want a clock time.
/// </summary>
/// <remarks>
/// Descriptive only. The product does not invent an hour for a named period, and the
/// internal midnight key it uses to identify an occurrence is never shown as a
/// reminder time.
/// </remarks>
public enum DayPeriod
{
    Morning = 0,
    Noon = 1,
    Afternoon = 2,
    Evening = 3,
    Night = 4,
    Bedtime = 5,
}

/// <summary>
/// The user's note about food timing, recorded as written. Not clinical guidance and
/// never used to shift a scheduled time.
/// </summary>
public enum MealRelation
{
    Fasting = 0,
    BeforeFood = 1,
    WithFood = 2,
    AfterFood = 3,
}

/// <summary>
/// The recurrence of one plan version, as a self-contained value so the due-date rule
/// can be tested without constructing a persistence entity.
/// </summary>
public sealed record RecurrenceSpecification(
    TreatmentKind Kind,
    RecurrencePattern Pattern,
    int? WeekdayMask,
    int? IntervalDays,
    DateOnly? EffectiveFrom,
    DateOnly? EffectiveTo)
{
    public const int MinimumWeekdayMask = 1;
    public const int MaximumWeekdayMask = 127;
    public const int MaximumIntervalDays = 3650;

    public static RecurrenceSpecification Daily(DateOnly? from = null, DateOnly? to = null) =>
        new(TreatmentKind.Scheduled, RecurrencePattern.Daily, null, null, from, to);

    public static RecurrenceSpecification AsNeeded(DateOnly? from = null, DateOnly? to = null) =>
        new(TreatmentKind.AsNeeded, RecurrencePattern.Daily, null, null, from, to);
}
