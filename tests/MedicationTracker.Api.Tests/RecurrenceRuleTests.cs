using MedicationTracker.Api.Domain.Scheduling;
using MedicationTracker.Api.Modules.Care;

namespace MedicationTracker.Api.Tests;

public sealed class RecurrenceRuleTests
{
    [Fact]
    public void Selected_weekdays_use_Monday_based_bits_without_locale_dependence()
    {
        // Monday and Thursday, including a daylight-saving transition week.
        var version = Version("weekdays", weekdayMask: (1 << 0) | (1 << 3));
        Assert.True(RecurrenceRule.IsDue(version, new DateOnly(2026, 3, 23)));
        Assert.False(RecurrenceRule.IsDue(version, new DateOnly(2026, 3, 29)));
        Assert.True(RecurrenceRule.IsDue(version, new DateOnly(2026, 4, 2)));
    }

    [Fact]
    public void Interval_is_anchored_to_effective_start_across_month_and_dst_boundaries()
    {
        var version = Version("interval", validFrom: new DateOnly(2026, 3, 28), intervalDays: 3);
        Assert.False(RecurrenceRule.IsDue(version, new DateOnly(2026, 3, 27)));
        Assert.True(RecurrenceRule.IsDue(version, new DateOnly(2026, 3, 28)));
        Assert.False(RecurrenceRule.IsDue(version, new DateOnly(2026, 3, 29)));
        Assert.True(RecurrenceRule.IsDue(version, new DateOnly(2026, 3, 31)));
        Assert.True(RecurrenceRule.IsDue(version, new DateOnly(2026, 4, 3)));
    }

    [Theory]
    [InlineData("scheduled", "weekdays", 0, null, false)]
    [InlineData("scheduled", "weekdays", 128, null, false)]
    [InlineData("scheduled", "interval", null, 2, false)]
    [InlineData("as_needed", "weekdays", 1, null, false)]
    [InlineData("scheduled", "daily", null, null, true)]
    public void Invalid_and_legacy_rules_are_distinguished(string scheduleType, string recurrenceKind, int? weekdayMask, int? intervalDays, bool expected)
    {
        Assert.Equal(expected, RecurrenceRule.IsValid(scheduleType, recurrenceKind, weekdayMask, intervalDays, null));
    }

    private static RegimenVersion Version(string kind, DateOnly? validFrom = null, int? weekdayMask = null, int? intervalDays = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), validFrom, null, 1, 1, new TimeOnly(8, 0), "Europe/Berlin", DateTimeOffset.UtcNow,
            recurrenceKind: kind, weekdayMask: weekdayMask, intervalDays: intervalDays);
}
