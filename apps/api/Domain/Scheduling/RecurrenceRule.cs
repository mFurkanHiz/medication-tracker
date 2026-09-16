using MedicationTracker.Api.Modules.Care;

namespace MedicationTracker.Api.Domain.Scheduling;

public static class RecurrenceRule
{
    public static bool IsValid(string scheduleType, string recurrenceKind, int? weekdayMask, int? intervalDays, DateOnly? validFrom)
    {
        if (scheduleType == "as_needed") return recurrenceKind == "daily" && weekdayMask is null && intervalDays is null;
        if (scheduleType != "scheduled") return false;
        return recurrenceKind switch
        {
            "daily" => weekdayMask is null && intervalDays is null,
            "weekdays" => weekdayMask is >= 1 and <= 127 && intervalDays is null,
            "interval" => validFrom is not null && intervalDays is >= 1 and <= 3650 && weekdayMask is null,
            _ => false
        };
    }

    public static bool IsDue(RegimenVersion version, DateOnly day)
    {
        if (version.ValidFrom is not null && day < version.ValidFrom.Value) return false;
        if (version.ValidTo is not null && day > version.ValidTo.Value) return false;
        if (version.ScheduleType == "as_needed") return true;
        return version.RecurrenceKind switch
        {
            "daily" => true,
            "weekdays" => version.WeekdayMask is int mask && (mask & (1 << WeekdayIndex(day))) != 0,
            "interval" => version.ValidFrom is DateOnly start && version.IntervalDays is int interval && (day.DayNumber - start.DayNumber) % interval == 0,
            _ => false
        };
    }

    // Monday is bit 0, Sunday bit 6; independent of locale and time zone.
    private static int WeekdayIndex(DateOnly day) => ((int)day.DayOfWeek + 6) % 7;
}
