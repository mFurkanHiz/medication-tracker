using MedicationTracker.Api.Domain.Administrations;
using MedicationTracker.Api.Domain.Reports;
using MedicationTracker.Api.Domain.Scheduling;

namespace MedicationTracker.Api.Tests;

/// <summary>
/// The adherence arithmetic and the slot replay behind it, with no database or clock.
/// </summary>
/// <remarks>
/// All data is synthetic. These cover the cases where a naive implementation reports a
/// household as having missed doses it did not miss, which is the way this report would
/// do real harm: a carer who stops trusting the number stops using it.
/// </remarks>
public sealed class AdherenceReportTests
{
    private static readonly DateTimeOffset Slot = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);

    // -----------------------------------------------------------------------------
    // Counting
    // -----------------------------------------------------------------------------

    [Fact]
    public void Each_outcome_is_counted_separately_and_an_extra_dose_is_not_a_scheduled_one()
    {
        var tally = AdherenceReport.Tally(3, [
            Dose(AdministrationOutcome.Taken, Slot),
            Dose(AdministrationOutcome.PartialDose, Slot.AddDays(1)),
            Dose(AdministrationOutcome.Skipped, Slot.AddDays(2)),

            // An unplanned dose: real, recorded, and attached to no slot.
            Dose(AdministrationOutcome.ExtraDose, null),
        ]);

        Assert.Equal(1, tally.Taken);
        Assert.Equal(1, tally.PartialDoses);
        Assert.Equal(1, tally.Skipped);
        Assert.Equal(1, tally.ExtraDoses);
        Assert.Equal(4, tally.RecordedDoses);

        // Three slots, all three answered, so nothing is missed even though one was a
        // deliberate skip and a fourth dose happened outside the schedule.
        Assert.Equal(3, tally.ScheduledDoses);
        Assert.Equal(3, tally.RecordedSlots);
        Assert.Equal(0, tally.MissedDoses);

        // Only the full and partial doses answered a slot with medication.
        Assert.Equal(2, tally.OnScheduleDoses);
        Assert.Equal((2, 3), tally.OnScheduleRatio);
    }

    [Fact]
    public void A_skipped_dose_is_answered_rather_than_missed()
    {
        var tally = AdherenceReport.Tally(2, [Dose(AdministrationOutcome.Skipped, Slot)]);

        Assert.Equal(1, tally.Skipped);

        // One slot was answered with a decision, the other was not answered at all.
        Assert.Equal(1, tally.MissedDoses);
        Assert.Equal(0, tally.OnScheduleDoses);
    }

    [Fact]
    public void An_extra_dose_against_a_slot_answers_it_without_counting_as_taken_on_schedule()
    {
        var tally = AdherenceReport.Tally(1, [Dose(AdministrationOutcome.ExtraDose, Slot)]);

        Assert.Equal(1, tally.ExtraDoses);
        Assert.Equal(1, tally.RecordedSlots);
        Assert.Equal(0, tally.OnScheduleDoses);
        Assert.Equal((0, 1), tally.OnScheduleRatio);
    }

    [Fact]
    public void Two_records_of_one_slot_count_that_slot_once()
    {
        // Two phones recording the same evening dose, or a command replayed by sync.
        var tally = AdherenceReport.Tally(2, [
            Dose(AdministrationOutcome.Taken, Slot),
            Dose(AdministrationOutcome.Taken, Slot),
        ]);

        Assert.Equal(2, tally.Taken);

        // The household did not become twice as adherent by syncing twice.
        Assert.Equal(1, tally.RecordedSlots);
        Assert.Equal(1, tally.OnScheduleDoses);
        Assert.Equal(1, tally.MissedDoses);
    }

    [Fact]
    public void A_dose_taken_after_midnight_still_answers_the_slot_it_was_for()
    {
        // The dose itself fell outside the period; the slot it answers did not.
        var tally = AdherenceReport.Tally(1, [
            new AdherenceRecord(AdministrationOutcome.Taken, Slot, OccurredInPeriod: false),
        ]);

        // Not counted in the period's outcome totals...
        Assert.Equal(0, tally.Taken);
        Assert.Equal(0, tally.RecordedDoses);

        // ...but the slot was answered, so the period does not report a missed dose.
        Assert.Equal(1, tally.OnScheduleDoses);
        Assert.Equal(0, tally.MissedDoses);
    }

    [Fact]
    public void Nothing_scheduled_reports_no_ratio_rather_than_zero()
    {
        // An as-needed medication: used twice, never due.
        var tally = AdherenceReport.Tally(0, [
            Dose(AdministrationOutcome.Taken, null),
            Dose(AdministrationOutcome.Taken, null),
        ]);

        Assert.Equal(2, tally.Taken);
        Assert.Equal(0, tally.ScheduledDoses);
        Assert.Equal(0, tally.MissedDoses);

        // A ratio with no denominator is not a zero, and must not render as 0%.
        Assert.Null(tally.OnScheduleRatio);
    }

    [Fact]
    public void A_slot_recorded_but_no_longer_scheduled_never_produces_a_negative_miss()
    {
        // A plan edited after the fact can leave a record whose slot the current
        // schedule no longer places.
        var tally = AdherenceReport.Tally(1, [
            Dose(AdministrationOutcome.Taken, Slot),
            Dose(AdministrationOutcome.Taken, Slot.AddDays(1)),
        ]);

        Assert.Equal(1, tally.RecordedSlots);
        Assert.Equal(1, tally.OnScheduleDoses);
        Assert.Equal(0, tally.MissedDoses);
    }

    [Fact]
    public void Tallies_roll_up_without_losing_a_count()
    {
        var first = AdherenceReport.Tally(3, [Dose(AdministrationOutcome.Taken, Slot)]);
        var second = AdherenceReport.Tally(2, [Dose(AdministrationOutcome.Skipped, Slot.AddDays(5))]);

        var total = AdherenceReport.Total([first, second]);

        Assert.Equal(5, total.ScheduledDoses);
        Assert.Equal(1, total.Taken);
        Assert.Equal(1, total.Skipped);
        Assert.Equal(2, total.RecordedSlots);
        Assert.Equal(3, total.MissedDoses);
        Assert.Equal((1, 5), total.OnScheduleRatio);
    }

    [Fact]
    public void An_empty_pair_is_recognisable_so_the_report_can_drop_it()
    {
        Assert.True(AdherenceReport.Tally(0, []).IsEmpty);
        Assert.False(AdherenceReport.Tally(1, []).IsEmpty);
        Assert.False(AdherenceReport.Tally(0, [Dose(AdministrationOutcome.Taken, null)]).IsEmpty);
    }

    // -----------------------------------------------------------------------------
    // Replaying the schedule over a past period
    // -----------------------------------------------------------------------------

    [Fact]
    public void The_version_in_force_on_each_day_decides_that_day_rather_than_the_newest_one()
    {
        // Daily until the 10th, then Mondays only from the 11th.
        var daily = new PlanVersionSlice(
            1,
            RecurrenceSpecification.Daily(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 10)),
            new TimeOnly(9, 0),
            TimeZoneInfo.Utc);

        var mondays = new PlanVersionSlice(
            2,
            new RecurrenceSpecification(
                TreatmentKind.Scheduled, RecurrencePattern.SelectedWeekdays, 1 << 0, null,
                new DateOnly(2026, 9, 11), null),
            new TimeOnly(9, 0),
            TimeZoneInfo.Utc);

        var slots = Within([daily, mondays], new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 20));

        // Ten daily slots to the 10th, then Mondays only. The 14th is the one Monday
        // inside the period; the next falls on the 21st, past the end.
        Assert.Equal(11, slots.Count);
        Assert.Contains(new DateTimeOffset(2026, 9, 5, 9, 0, 0, TimeSpan.Zero), slots);
        Assert.Contains(new DateTimeOffset(2026, 9, 14, 9, 0, 0, TimeSpan.Zero), slots);

        // The 15th is a Tuesday, which the newer version does not ask for.
        Assert.DoesNotContain(new DateTimeOffset(2026, 9, 15, 9, 0, 0, TimeSpan.Zero), slots);
    }

    [Fact]
    public void A_stopped_plan_keeps_the_slots_it_placed_before_it_stopped()
    {
        var daily = new PlanVersionSlice(
            1, RecurrenceSpecification.Daily(new DateOnly(2026, 9, 1)), new TimeOnly(9, 0), TimeZoneInfo.Utc);

        var slots = ScheduledSlots.Within(
            [daily],
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 10),
            new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 11, 0, 0, 0, TimeSpan.Zero),

            // Archiving the medication stopped the plan on the evening of the 4th.
            stoppedAt: new DateTimeOffset(2026, 9, 4, 20, 0, 0, TimeSpan.Zero));

        // The 1st to the 4th were really due. Deleting the plan must not rewrite that.
        Assert.Equal(4, slots.Count);
        Assert.Equal(new DateTimeOffset(2026, 9, 4, 9, 0, 0, TimeSpan.Zero), slots[^1]);
    }

    [Fact]
    public void An_as_needed_plan_places_no_slots_so_it_can_never_be_missed()
    {
        var asNeeded = new PlanVersionSlice(
            1, RecurrenceSpecification.AsNeeded(new DateOnly(2026, 9, 1)), null, TimeZoneInfo.Utc);

        Assert.Empty(Within([asNeeded], new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)));
    }

    [Fact]
    public void A_day_no_version_covers_places_no_slot()
    {
        var september = new PlanVersionSlice(
            1,
            RecurrenceSpecification.Daily(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 3)),
            new TimeOnly(9, 0),
            TimeZoneInfo.Utc);

        var slots = Within([september], new DateOnly(2026, 8, 28), new DateOnly(2026, 9, 6));

        Assert.Equal(3, slots.Count);
    }

    [Fact]
    public void A_period_containing_a_daylight_saving_transition_keeps_one_slot_per_due_day()
    {
        // Istanbul stays on UTC+3 all year, so a zone that does shift is needed to
        // prove the slot is placed through local time rather than by adding 24 hours.
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

        var daily = new PlanVersionSlice(
            1, RecurrenceSpecification.Daily(new DateOnly(2026, 3, 27)), new TimeOnly(9, 0), zone);

        // 29 March 2026 is the European spring-forward day.
        var slots = ScheduledSlots.Within(
            [daily],
            new DateOnly(2026, 3, 27),
            new DateOnly(2026, 3, 31),
            new DateTimeOffset(2026, 3, 26, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 4, 2, 0, 0, 0, TimeSpan.Zero),
            stoppedAt: null);

        Assert.Equal(5, slots.Count);

        // 09:00 local is 08:00 UTC before the transition and 07:00 UTC after it. A
        // fixed 24-hour step would have drifted the later slots off 09:00 local.
        Assert.Equal(new DateTimeOffset(2026, 3, 27, 8, 0, 0, TimeSpan.Zero), slots[0]);
        Assert.Equal(new DateTimeOffset(2026, 3, 30, 7, 0, 0, TimeSpan.Zero), slots[3]);
    }

    [Fact]
    public void A_slot_outside_the_window_is_excluded_even_when_its_day_is_replayed()
    {
        var daily = new PlanVersionSlice(
            1, RecurrenceSpecification.Daily(new DateOnly(2026, 9, 1)), new TimeOnly(9, 0), TimeZoneInfo.Utc);

        // The replay covers the 1st to the 3rd; the window opens midway through the 1st.
        var slots = ScheduledSlots.Within(
            [daily],
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 3),
            new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 4, 0, 0, 0, TimeSpan.Zero),
            stoppedAt: null);

        Assert.Equal(2, slots.Count);
        Assert.Equal(new DateTimeOffset(2026, 9, 2, 9, 0, 0, TimeSpan.Zero), slots[0]);
    }

    [Fact]
    public void No_versions_means_no_slots()
    {
        Assert.Empty(Within([], new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)));
    }

    // -----------------------------------------------------------------------------
    // Period resolution
    // -----------------------------------------------------------------------------

    [Fact]
    public void A_period_longer_than_the_report_will_walk_is_refused()
    {
        var from = new DateOnly(2026, 1, 1);

        Assert.False(ReportPeriod.TryResolve(
            from, from.AddDays(AdherenceReport.MaximumPeriodDays), TimeZoneInfo.Utc, out _));

        Assert.True(ReportPeriod.TryResolve(
            from, from.AddDays(AdherenceReport.MaximumPeriodDays - 1), TimeZoneInfo.Utc, out _));
    }

    [Fact]
    public void A_period_that_ends_before_it_starts_is_refused()
    {
        Assert.False(ReportPeriod.TryResolve(
            new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 9), TimeZoneInfo.Utc, out _));
    }

    [Fact]
    public void Naming_one_end_of_the_period_derives_the_other()
    {
        Assert.True(ReportPeriod.TryResolve(
            null, new DateOnly(2026, 9, 30), TimeZoneInfo.Utc, out var backwards));
        Assert.Equal(new DateOnly(2026, 9, 1), backwards.From);
        Assert.Equal(new DateOnly(2026, 9, 30), backwards.To);
        Assert.Equal(AdherenceReport.DefaultPeriodDays, backwards.Days);

        Assert.True(ReportPeriod.TryResolve(
            new DateOnly(2026, 9, 1), null, TimeZoneInfo.Utc, out var forwards));
        Assert.Equal(new DateOnly(2026, 9, 1), forwards.From);
        Assert.Equal(new DateOnly(2026, 9, 30), forwards.To);
    }

    [Fact]
    public void The_window_spans_local_midnight_to_local_midnight()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

        Assert.True(ReportPeriod.TryResolve(
            new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), zone, out var period));

        // Istanbul is UTC+3, so a local September starts at 21:00 on 31 August UTC.
        Assert.Equal(new DateTimeOffset(2026, 8, 31, 21, 0, 0, TimeSpan.Zero), period.WindowStart);
        Assert.Equal(new DateTimeOffset(2026, 9, 30, 21, 0, 0, TimeSpan.Zero), period.WindowEnd);

        // A dose recorded at 22:00 UTC on 30 September is already October in Istanbul.
        Assert.True(period.Contains(new DateTimeOffset(2026, 9, 30, 20, 59, 0, TimeSpan.Zero)));
        Assert.False(period.Contains(new DateTimeOffset(2026, 9, 30, 22, 0, 0, TimeSpan.Zero)));
    }

    private static AdherenceRecord Dose(AdministrationOutcome outcome, DateTimeOffset? slot) =>
        new(outcome, slot, OccurredInPeriod: true);

    /// <summary>Replays a whole period, with the window set to exactly that period.</summary>
    private static List<DateTimeOffset> Within(
        IReadOnlyCollection<PlanVersionSlice> versions,
        DateOnly from,
        DateOnly to) =>
        ScheduledSlots.Within(
            versions,
            from,
            to,
            new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            stoppedAt: null);
}
