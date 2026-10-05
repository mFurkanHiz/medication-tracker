using MedicationTracker.Api.Domain.Quantities;
using MedicationTracker.Api.Domain.Scheduling;

namespace MedicationTracker.Api.Modules.Treatments;

/// <summary>
/// The standing instruction that a person takes a medication, independent of any
/// physical package.
/// </summary>
/// <remarks>
/// A plan points at a <see cref="Catalog.MedicationDefinition"/>, never at a box. Which
/// box a dose comes from is decided at the moment of use by the consumption policy, so
/// a plan survives every package being replaced.
/// </remarks>
public sealed class TreatmentPlan
{
    private TreatmentPlan() { }

    public TreatmentPlan(
        Guid id,
        Guid householdId,
        Guid personId,
        Guid medicationDefinitionId,
        DateTimeOffset createdAt)
    {
        Id = id;
        HouseholdId = householdId;
        PersonId = personId;
        MedicationDefinitionId = medicationDefinitionId;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid HouseholdId { get; private set; }

    public Guid PersonId { get; private set; }

    public Guid MedicationDefinitionId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public bool IsActive => DeletedAt is null;

    public void Reassign(Guid personId, Guid medicationDefinitionId)
    {
        PersonId = personId;
        MedicationDefinitionId = medicationDefinitionId;
    }

    /// <summary>
    /// Stops the plan without touching its versions or the administrations recorded
    /// against them.
    /// </summary>
    public void Delete(DateTimeOffset deletedAt) => DeletedAt = deletedAt;
}

/// <summary>
/// An immutable, effective-dated snapshot of what a plan said during one period.
/// </summary>
/// <remarks>
/// <para>
/// Editing a plan appends a new version; it never rewrites an old one. Every
/// administration stays linked to the version that was in force when it happened, so
/// changing today's dose cannot retroactively make last month's adherence look wrong.
/// </para>
/// <para>
/// The dose recorded here is what the household's own prescriber told them. The product
/// stores it and never calculates or suggests one.
/// </para>
/// </remarks>
public sealed class TreatmentPlanVersion
{
    private TreatmentPlanVersion() { }

    public TreatmentPlanVersion(
        Guid id,
        Guid treatmentPlanId,
        int versionNumber,
        ExactQuantity dose,
        RecurrenceSpecification recurrence,
        TimeOnly? localTime,
        string timeZoneId,
        DateTimeOffset createdAt,
        Guid createdByAccountId,
        DayPeriod? dayPeriod = null,
        MealRelation? mealRelation = null,
        int? minimumIntervalMinutes = null,
        string? instructions = null,
        bool isPaused = false)
    {
        ArgumentNullException.ThrowIfNull(recurrence);
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);

        if (!dose.IsPositive)
        {
            throw new ArgumentOutOfRangeException(nameof(dose), "A plan dose must be a positive amount.");
        }

        if (versionNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(versionNumber), "Version numbers start at one.");
        }

        if (!RecurrenceRule.IsValid(recurrence))
        {
            throw new ArgumentException("The recurrence is not internally consistent.", nameof(recurrence));
        }

        if (recurrence.Kind == TreatmentKind.Scheduled && localTime is null && dayPeriod is null)
        {
            throw new ArgumentException(
                "A scheduled version needs either an exact local time or a named day period.",
                nameof(localTime));
        }

        if (minimumIntervalMinutes is < 0 or > 60 * 24 * 30)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumIntervalMinutes));
        }

        Id = id;
        TreatmentPlanId = treatmentPlanId;
        VersionNumber = versionNumber;
        DoseNumerator = dose.Numerator;
        DoseDenominator = dose.Denominator;
        Kind = recurrence.Kind;
        Pattern = recurrence.Pattern;
        WeekdayMask = recurrence.WeekdayMask;
        IntervalDays = recurrence.IntervalDays;
        DayOfMonth = recurrence.DayOfMonth;
        IntervalMonths = recurrence.IntervalMonths;
        EffectiveFrom = recurrence.EffectiveFrom;
        EffectiveTo = recurrence.EffectiveTo;
        LocalTime = localTime;
        TimeZoneId = timeZoneId;
        DayPeriod = dayPeriod;
        MealRelation = mealRelation;
        MinimumIntervalMinutes = minimumIntervalMinutes;
        Instructions = string.IsNullOrWhiteSpace(instructions) ? null : instructions.Trim();
        IsPaused = isPaused;
        CreatedAt = createdAt;
        CreatedByAccountId = createdByAccountId;
    }

    public Guid Id { get; private set; }

    public Guid TreatmentPlanId { get; private set; }

    /// <summary>Monotonic within a plan, so version order does not depend on timestamps.</summary>
    public int VersionNumber { get; private set; }

    public long DoseNumerator { get; private set; }

    public long DoseDenominator { get; private set; }

    public ExactQuantity Dose => new(DoseNumerator, DoseDenominator);

    public TreatmentKind Kind { get; private set; }

    public RecurrencePattern Pattern { get; private set; }

    /// <summary>Monday-first seven-bit mask; only set for selected weekdays.</summary>
    public int? WeekdayMask { get; private set; }

    public int? IntervalDays { get; private set; }

    /// <summary>For <see cref="RecurrencePattern.DayOfMonth"/>: 1–31, clamped to the month.</summary>
    public int? DayOfMonth { get; private set; }

    /// <summary>For <see cref="RecurrencePattern.EveryNMonths"/>: the month interval, anchored on the start date.</summary>
    public int? IntervalMonths { get; private set; }

    public DateOnly? EffectiveFrom { get; private set; }

    public DateOnly? EffectiveTo { get; private set; }

    public TimeOnly? LocalTime { get; private set; }

    /// <summary>IANA time zone the plan's local days and times are expressed in.</summary>
    public string TimeZoneId { get; private set; } = string.Empty;

    public DayPeriod? DayPeriod { get; private set; }

    public MealRelation? MealRelation { get; private set; }

    public int? MinimumIntervalMinutes { get; private set; }

    public string? Instructions { get; private set; }

    /// <summary>
    /// Whether the household has set this plan aside for now.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Pausing is not ending. Ending a plan is a soft delete with no way back, and that is
    /// the wrong tool for "I have stopped taking this for a while" — which is the ordinary
    /// case, not the exception.
    /// </para>
    /// <para>
    /// It lives on the VERSION rather than on the plan, and pausing appends a version like
    /// any other edit, so the periods before and after a pause stay exactly as they were.
    /// A flag on the aggregate would have rewritten history every time somebody paused.
    /// </para>
    /// <para>
    /// A paused version still covers its days. That is deliberate: the adherence replay
    /// skips it by this flag, so the paused stretch produces no slots and therefore no
    /// missed doses, while the days themselves remain governed and auditable.
    /// </para>
    /// </remarks>
    public bool IsPaused { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Guid CreatedByAccountId { get; private set; }

    public RecurrenceSpecification Recurrence =>
        new(Kind, Pattern, WeekdayMask, IntervalDays, EffectiveFrom, EffectiveTo, DayOfMonth, IntervalMonths);

    public bool IsDueOn(DateOnly localDay) => RecurrenceRule.IsDue(Recurrence, localDay);

    /// <summary>Whether this version governs the given local day at all.</summary>
    public bool CoversDay(DateOnly localDay) =>
        (EffectiveFrom is null || EffectiveFrom.Value <= localDay)
        && (EffectiveTo is null || EffectiveTo.Value >= localDay);
}
