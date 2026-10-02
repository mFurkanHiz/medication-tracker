namespace MedicationTracker.Api.Domain.Scheduling;

/// <summary>
/// Decides which local calendar days a treatment plan version is due on.
/// </summary>
/// <remarks>
/// <para>
/// All arithmetic is <see cref="DateOnly"/> arithmetic. Adding 24-hour spans instead
/// would drift by an hour across a daylight-saving transition and eventually move a
/// plan onto the wrong local day, so elapsed time is never used to advance a
/// recurrence.
/// </para>
/// <para>
/// Carried forward from PR #9, whose weekday-mask and interval rules were correct;
/// restated against a value object so it no longer depends on a persistence entity.
/// </para>
/// </remarks>
public static class RecurrenceRule
{
    /// <summary>
    /// Whether a recurrence is internally consistent. Rejects a weekday mask with no
    /// days selected, an interval with no anchor date, and any combination that mixes
    /// fields belonging to different patterns.
    /// </summary>
    public static bool IsValid(RecurrenceSpecification specification)
    {
        ArgumentNullException.ThrowIfNull(specification);

        if (specification.EffectiveFrom is { } from
            && specification.EffectiveTo is { } to
            && to < from)
        {
            return false;
        }

        if (specification.Kind == TreatmentKind.AsNeeded)
        {
            return specification.Pattern == RecurrencePattern.Daily
                   && specification.WeekdayMask is null
                   && specification.IntervalDays is null;
        }

        return specification.Pattern switch
        {
            RecurrencePattern.Daily =>
                specification.WeekdayMask is null && specification.IntervalDays is null,

            RecurrencePattern.SelectedWeekdays =>
                specification.WeekdayMask is >= RecurrenceSpecification.MinimumWeekdayMask
                    and <= RecurrenceSpecification.MaximumWeekdayMask
                && specification.IntervalDays is null,

            RecurrencePattern.EveryNDays =>
                specification.EffectiveFrom is not null
                && specification.IntervalDays is >= 1 and <= RecurrenceSpecification.MaximumIntervalDays
                && specification.WeekdayMask is null,

            _ => false,
        };
    }

    /// <summary>
    /// Whether the plan is due on <paramref name="day"/>, expressed in the plan's own
    /// local time zone.
    /// </summary>
    public static bool IsDue(RecurrenceSpecification specification, DateOnly day)
    {
        ArgumentNullException.ThrowIfNull(specification);

        if (specification.EffectiveFrom is { } from && day < from)
        {
            return false;
        }

        if (specification.EffectiveTo is { } to && day > to)
        {
            return false;
        }

        // An as-needed plan has no due days; it is available every day it is in force.
        if (specification.Kind == TreatmentKind.AsNeeded)
        {
            return true;
        }

        return specification.Pattern switch
        {
            RecurrencePattern.Daily => true,

            RecurrencePattern.SelectedWeekdays =>
                specification.WeekdayMask is { } mask && (mask & (1 << WeekdayIndex(day))) != 0,

            RecurrencePattern.EveryNDays =>
                specification.EffectiveFrom is { } anchor
                && specification.IntervalDays is { } interval
                && interval > 0
                && (day.DayNumber - anchor.DayNumber) % interval == 0,

            _ => false,
        };
    }

    /// <summary>
    /// Enumerates due days from <paramref name="from"/> inclusive, up to
    /// <paramref name="maximumDays"/> calendar days ahead. Yields nothing for an
    /// as-needed plan, whose future consumption is unknown and must not be forecast.
    /// </summary>
    public static IEnumerable<DateOnly> DueDays(
        RecurrenceSpecification specification,
        DateOnly from,
        int maximumDays)
    {
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumDays);

        if (specification.Kind == TreatmentKind.AsNeeded)
        {
            yield break;
        }

        for (var offset = 0; offset < maximumDays; offset++)
        {
            var day = from.AddDays(offset);

            if (specification.EffectiveTo is { } to && day > to)
            {
                yield break;
            }

            if (IsDue(specification, day))
            {
                yield return day;
            }
        }
    }

    /// <summary>
    /// Monday is bit 0 and Sunday is bit 6, independent of the server's culture and of
    /// any client's first-day-of-week setting.
    /// </summary>
    public static int WeekdayIndex(DateOnly day) => ((int)day.DayOfWeek + 6) % 7;

    /// <summary>
    /// Converts a local date and optional time into an instant, resolving the gap
    /// created by a spring-forward transition by moving forward past it.
    /// </summary>
    /// <remarks>
    /// When a plan has no clock time, local midnight is used purely as a stable
    /// occurrence key. It is never presented as a reminder time and carries no dosing
    /// meaning.
    /// </remarks>
    public static DateTimeOffset ScheduledInstant(DateOnly date, TimeOnly? localTime, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        var local = date.ToDateTime(localTime ?? TimeOnly.MinValue, DateTimeKind.Unspecified);

        if (zone.IsInvalidTime(local))
        {
            local = local.AddHours(1);
        }

        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }
}
