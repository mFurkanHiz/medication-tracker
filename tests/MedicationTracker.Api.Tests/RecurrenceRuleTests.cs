using MedicationTracker.Api.Domain.Scheduling;

namespace MedicationTracker.Api.Tests;

/// <summary>
/// Due-date rules, carried forward from PR #9 and restated against the recurrence value
/// object so they no longer need a persistence entity to run.
/// </summary>
public sealed class RecurrenceRuleTests
{
    [Fact]
    public void Selected_weekdays_use_Monday_based_bits_without_locale_dependence()
    {
        // Monday and Thursday, including a daylight-saving transition week.
        var weekdays = Weekdays((1 << 0) | (1 << 3));

        Assert.True(RecurrenceRule.IsDue(weekdays, new DateOnly(2026, 3, 23)));
        Assert.False(RecurrenceRule.IsDue(weekdays, new DateOnly(2026, 3, 29)));
        Assert.True(RecurrenceRule.IsDue(weekdays, new DateOnly(2026, 4, 2)));
    }

    [Theory]
    [InlineData(2026, 3, 23, 0)] // Monday
    [InlineData(2026, 3, 26, 3)] // Thursday
    [InlineData(2026, 3, 29, 6)] // Sunday
    public void Monday_is_bit_zero_and_Sunday_is_bit_six(int year, int month, int day, int expected)
    {
        Assert.Equal(expected, RecurrenceRule.WeekdayIndex(new DateOnly(year, month, day)));
    }

    [Fact]
    public void Interval_is_anchored_to_the_effective_start_across_month_and_dst_boundaries()
    {
        var everyThirdDay = Interval(3, new DateOnly(2026, 3, 28));

        Assert.False(RecurrenceRule.IsDue(everyThirdDay, new DateOnly(2026, 3, 27)));
        Assert.True(RecurrenceRule.IsDue(everyThirdDay, new DateOnly(2026, 3, 28)));
        Assert.False(RecurrenceRule.IsDue(everyThirdDay, new DateOnly(2026, 3, 29)));
        Assert.True(RecurrenceRule.IsDue(everyThirdDay, new DateOnly(2026, 3, 31)));
        Assert.True(RecurrenceRule.IsDue(everyThirdDay, new DateOnly(2026, 4, 3)));
    }

    [Fact]
    public void An_effective_period_bounds_due_days_at_both_ends()
    {
        var bounded = RecurrenceSpecification.Daily(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3));

        Assert.False(RecurrenceRule.IsDue(bounded, new DateOnly(2026, 4, 30)));
        Assert.True(RecurrenceRule.IsDue(bounded, new DateOnly(2026, 5, 1)));
        Assert.True(RecurrenceRule.IsDue(bounded, new DateOnly(2026, 5, 3)));
        Assert.False(RecurrenceRule.IsDue(bounded, new DateOnly(2026, 5, 4)));
    }

    [Fact]
    public void Due_days_enumerate_only_the_selected_weekdays_and_stop_at_the_period_end()
    {
        var weekdays = Weekdays(1 << 0) with { EffectiveFrom = new DateOnly(2026, 3, 1), EffectiveTo = new DateOnly(2026, 3, 20) };

        var due = RecurrenceRule.DueDays(weekdays, new DateOnly(2026, 3, 1), 60).ToList();

        Assert.Equal(
            [
                new DateOnly(2026, 3, 2),
                new DateOnly(2026, 3, 9),
                new DateOnly(2026, 3, 16),
            ],
            due);
    }

    [Fact]
    public void An_as_needed_plan_has_no_due_days_so_it_is_never_forecast()
    {
        var asNeeded = RecurrenceSpecification.AsNeeded();

        // Available every day it is in force...
        Assert.True(RecurrenceRule.IsDue(asNeeded, new DateOnly(2026, 5, 1)));

        // ...but it generates no scheduled occurrences, because how often it will be
        // used is unknown and must not be invented.
        Assert.Empty(RecurrenceRule.DueDays(asNeeded, new DateOnly(2026, 5, 1), 30));
    }

    [Theory]
    // A weekday pattern needs at least one day selected and at most all seven.
    [InlineData(TreatmentKind.Scheduled, RecurrencePattern.SelectedWeekdays, 0, null, false)]
    [InlineData(TreatmentKind.Scheduled, RecurrencePattern.SelectedWeekdays, 128, null, false)]
    [InlineData(TreatmentKind.Scheduled, RecurrencePattern.SelectedWeekdays, 1, null, true)]
    [InlineData(TreatmentKind.Scheduled, RecurrencePattern.SelectedWeekdays, 127, null, true)]
    // Fields belonging to another pattern are rejected rather than ignored.
    [InlineData(TreatmentKind.Scheduled, RecurrencePattern.SelectedWeekdays, 1, 3, false)]
    [InlineData(TreatmentKind.Scheduled, RecurrencePattern.Daily, 1, null, false)]
    [InlineData(TreatmentKind.Scheduled, RecurrencePattern.Daily, null, null, true)]
    // An as-needed plan carries no recurrence fields at all.
    [InlineData(TreatmentKind.AsNeeded, RecurrencePattern.SelectedWeekdays, 1, null, false)]
    [InlineData(TreatmentKind.AsNeeded, RecurrencePattern.Daily, null, null, true)]
    public void Inconsistent_recurrences_are_rejected(
        TreatmentKind kind,
        RecurrencePattern pattern,
        int? weekdayMask,
        int? intervalDays,
        bool expected)
    {
        var specification = new RecurrenceSpecification(
            kind, pattern, weekdayMask, intervalDays, new DateOnly(2026, 1, 1), null);

        Assert.Equal(expected, RecurrenceRule.IsValid(specification));
    }

    [Fact]
    public void An_interval_pattern_requires_an_anchor_date_and_a_sane_length()
    {
        Assert.False(RecurrenceRule.IsValid(
            new RecurrenceSpecification(
                TreatmentKind.Scheduled, RecurrencePattern.EveryNDays, null, 3, null, null)));

        Assert.False(RecurrenceRule.IsValid(Interval(0, new DateOnly(2026, 1, 1))));
        Assert.False(RecurrenceRule.IsValid(Interval(3651, new DateOnly(2026, 1, 1))));
        Assert.True(RecurrenceRule.IsValid(Interval(1, new DateOnly(2026, 1, 1))));
    }

    [Fact]
    public void An_effective_period_that_ends_before_it_starts_is_rejected()
    {
        Assert.False(RecurrenceRule.IsValid(
            RecurrenceSpecification.Daily(new DateOnly(2026, 5, 10), new DateOnly(2026, 5, 1))));
    }

    [Fact]
    public void A_local_time_inside_a_spring_forward_gap_moves_forward_past_it()
    {
        var berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

        // 02:30 on 2026-03-29 does not exist in Berlin; the clock jumps 02:00 to 03:00.
        var resolved = RecurrenceRule.ScheduledInstant(
            new DateOnly(2026, 3, 29), new TimeOnly(2, 30), berlin);

        Assert.Equal(new DateTimeOffset(2026, 3, 29, 1, 30, 0, TimeSpan.Zero), resolved);
    }

    [Fact]
    public void A_plan_without_a_clock_time_uses_local_midnight_only_as_an_occurrence_key()
    {
        var istanbul = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

        var resolved = RecurrenceRule.ScheduledInstant(new DateOnly(2026, 5, 1), null, istanbul);

        // Istanbul is UTC+3 year round, so local midnight is 21:00 the previous day.
        Assert.Equal(new DateTimeOffset(2026, 4, 30, 21, 0, 0, TimeSpan.Zero), resolved);
    }

    private static RecurrenceSpecification Weekdays(int mask) => new(
        TreatmentKind.Scheduled, RecurrencePattern.SelectedWeekdays, mask, null, null, null);

    private static RecurrenceSpecification Interval(int days, DateOnly from) => new(
        TreatmentKind.Scheduled, RecurrencePattern.EveryNDays, null, days, from, null);
}
