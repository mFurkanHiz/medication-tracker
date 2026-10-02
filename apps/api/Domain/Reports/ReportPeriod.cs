using MedicationTracker.Api.Domain.Scheduling;

namespace MedicationTracker.Api.Domain.Reports;

/// <summary>
/// The stretch of time a report covers, as the local days the user asked for plus the
/// instants those days actually span.
/// </summary>
/// <remarks>
/// Both forms are needed and neither can be derived at the point of use. The user thinks
/// in local days — "September" — while the rows being counted are instants, and the
/// conversion depends on a time zone and on whether a daylight-saving transition falls
/// on a boundary. Resolving it once, here, keeps every query in a report agreeing about
/// where the period starts.
/// </remarks>
public sealed record ReportPeriod(
    DateOnly From,
    DateOnly To,
    TimeZoneInfo Zone,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd)
{
    /// <summary>Whole local days in the period, both ends included.</summary>
    public int Days => To.DayNumber - From.DayNumber + 1;

    /// <summary>
    /// Resolves a period from whichever ends the caller named, defaulting to the last
    /// <see cref="AdherenceReport.DefaultPeriodDays"/> days.
    /// </summary>
    /// <remarks>
    /// Naming only a start means that many days forward from it, and naming only an end
    /// means that many days back from it, so a single date is never silently turned into
    /// a one-day report. A period that ends before it starts, or that is longer than the
    /// report is willing to walk, is refused rather than clamped: a clamped period would
    /// answer a question nobody asked.
    /// </remarks>
    public static bool TryResolve(
        DateOnly? from,
        DateOnly? to,
        TimeZoneInfo zone,
        out ReportPeriod period)
    {
        ArgumentNullException.ThrowIfNull(zone);

        period = null!;

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime);
        var last = to ?? from?.AddDays(AdherenceReport.DefaultPeriodDays - 1) ?? today;
        var first = from ?? last.AddDays(-(AdherenceReport.DefaultPeriodDays - 1));

        if (last < first || last.DayNumber - first.DayNumber >= AdherenceReport.MaximumPeriodDays)
        {
            return false;
        }

        period = new ReportPeriod(
            first,
            last,
            zone,

            // Local midnight on each boundary, resolved through the same rule the
            // scheduler uses, so a period that starts on a spring-forward day still
            // starts at an instant that exists.
            RecurrenceRule.ScheduledInstant(first, TimeOnly.MinValue, zone),
            RecurrenceRule.ScheduledInstant(last.AddDays(1), TimeOnly.MinValue, zone));

        return true;
    }

    /// <summary>Whether an instant falls inside the period.</summary>
    public bool Contains(DateTimeOffset instant) => instant >= WindowStart && instant < WindowEnd;
}
