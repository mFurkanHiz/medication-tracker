using MedicationTracker.Api.Domain.Scheduling;

namespace MedicationTracker.Api.Domain.Reports;

/// <summary>
/// One effective-dated version of a plan, reduced to what placing its slots on a
/// timeline needs.
/// </summary>
public sealed record PlanVersionSlice(
    int VersionNumber,
    RecurrenceSpecification Recurrence,
    TimeOnly? LocalTime,
    TimeZoneInfo Zone);

/// <summary>
/// Works out which dose slots a plan actually placed inside a past period.
/// </summary>
/// <remarks>
/// <para>
/// This is the half of adherence that cannot be read from the administration table: a
/// dose nobody recorded leaves no row, so the only way to tell a missed dose from a day
/// the plan never asked for is to replay the schedule over the period.
/// </para>
/// <para>
/// Replaying it is not the same as reading today's plan. A plan is a chain of
/// effective-dated versions, so the version that governed a Tuesday three weeks ago may
/// have asked for a different pattern than the one in force now. For each day the
/// highest-numbered version covering that day wins, which is the same rule the daily
/// due list uses, so a dose the user was shown and a dose this report expects are
/// always the same dose.
/// </para>
/// <para>
/// Slots are produced as instants through the version's own time zone, so a period that
/// contains a daylight-saving transition keeps one slot per due day rather than
/// gaining or losing one.
/// </para>
/// </remarks>
public static class ScheduledSlots
{
    /// <summary>
    /// The slot instants one plan placed inside <paramref name="windowStart"/> to
    /// <paramref name="windowEnd"/>, exclusive of the end.
    /// </summary>
    /// <param name="firstDay">
    /// First local day to replay. The caller pads the period by a day on each side so a
    /// plan in a time zone east or west of the report's own still has its boundary slots
    /// considered; the window check then decides whether they really fall inside.
    /// </param>
    /// <param name="stoppedAt">
    /// When the plan was stopped, or null while it is still running. A stopped plan keeps
    /// the slots it placed before it stopped — deleting a plan must not rewrite what it
    /// asked for last week — and places none afterwards.
    /// </param>
    public static List<DateTimeOffset> Within(
        IReadOnlyCollection<PlanVersionSlice> versions,
        DateOnly firstDay,
        DateOnly lastDay,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        DateTimeOffset? stoppedAt)
    {
        ArgumentNullException.ThrowIfNull(versions);

        var slots = new List<DateTimeOffset>();

        if (versions.Count == 0 || lastDay < firstDay)
        {
            return slots;
        }

        for (var day = firstDay; day <= lastDay; day = day.AddDays(1))
        {
            if (Governing(versions, day) is not { } version)
            {
                continue;
            }

            // An as-needed plan places no slots at all. How often it will be used is
            // unknown, so counting a day as a missed dose would invent an obligation
            // the household never had.
            if (version.Recurrence.Kind != TreatmentKind.Scheduled
                || !RecurrenceRule.IsDue(version.Recurrence, day))
            {
                continue;
            }

            var slot = RecurrenceRule.ScheduledInstant(day, version.LocalTime, version.Zone);

            if (slot < windowStart || slot >= windowEnd)
            {
                continue;
            }

            if (stoppedAt is { } stopped && slot >= stopped)
            {
                continue;
            }

            slots.Add(slot);
        }

        return slots;
    }

    /// <summary>
    /// The version in force on a day: the highest-numbered one whose effective dates
    /// cover it. Null when the plan said nothing about that day.
    /// </summary>
    private static PlanVersionSlice? Governing(IEnumerable<PlanVersionSlice> versions, DateOnly day)
    {
        PlanVersionSlice? governing = null;

        foreach (var version in versions)
        {
            if (!Covers(version.Recurrence, day))
            {
                continue;
            }

            if (governing is null || version.VersionNumber > governing.VersionNumber)
            {
                governing = version;
            }
        }

        return governing;
    }

    private static bool Covers(RecurrenceSpecification recurrence, DateOnly day) =>
        (recurrence.EffectiveFrom is null || recurrence.EffectiveFrom.Value <= day)
        && (recurrence.EffectiveTo is null || recurrence.EffectiveTo.Value >= day);
}
