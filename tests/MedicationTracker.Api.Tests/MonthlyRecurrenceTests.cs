using System.Text.Json;
using MedicationTracker.Api.Domain.Scheduling;

namespace MedicationTracker.Api.Tests;

/// <summary>
/// Monthly plans, in both forms the owner asked for: a day of every month, and every N
/// months anchored on the start date.
/// </summary>
/// <remarks>
/// Calendar months, never thirty-day spans. A day the month does not have falls on its
/// last day, so a plan written for the 31st is not silently skipped in February. All data
/// is synthetic.
/// </remarks>
public sealed class MonthlyRecurrenceTests
{
    [Fact]
    public void A_day_of_the_month_is_due_on_that_day_and_on_the_last_day_of_shorter_months()
    {
        var thirtyFirst = DayOfMonth(31, new DateOnly(2026, 1, 1));

        Assert.True(RecurrenceRule.IsDue(thirtyFirst, new DateOnly(2026, 1, 31)));
        Assert.True(RecurrenceRule.IsDue(thirtyFirst, new DateOnly(2026, 2, 28)));
        Assert.False(RecurrenceRule.IsDue(thirtyFirst, new DateOnly(2026, 2, 27)));
        Assert.True(RecurrenceRule.IsDue(thirtyFirst, new DateOnly(2026, 3, 31)));
        Assert.True(RecurrenceRule.IsDue(thirtyFirst, new DateOnly(2026, 4, 30)));
        Assert.False(RecurrenceRule.IsDue(thirtyFirst, new DateOnly(2026, 4, 29)));
    }

    [Fact]
    public void Every_n_months_is_anchored_on_the_day_of_the_start_date()
    {
        var monthlyFromTheThirtyFirst = EveryNMonths(1, new DateOnly(2026, 1, 31));
        Assert.True(RecurrenceRule.IsDue(monthlyFromTheThirtyFirst, new DateOnly(2026, 2, 28)));
        Assert.True(RecurrenceRule.IsDue(monthlyFromTheThirtyFirst, new DateOnly(2026, 3, 31)));
        Assert.False(RecurrenceRule.IsDue(monthlyFromTheThirtyFirst, new DateOnly(2026, 3, 30)));

        // Quarterly from the 15th of January: April and July, not February or March.
        var quarterly = EveryNMonths(3, new DateOnly(2026, 1, 15));
        Assert.True(RecurrenceRule.IsDue(quarterly, new DateOnly(2026, 1, 15)));
        Assert.False(RecurrenceRule.IsDue(quarterly, new DateOnly(2026, 2, 15)));
        Assert.False(RecurrenceRule.IsDue(quarterly, new DateOnly(2026, 3, 15)));
        Assert.True(RecurrenceRule.IsDue(quarterly, new DateOnly(2026, 4, 15)));
        Assert.True(RecurrenceRule.IsDue(quarterly, new DateOnly(2026, 7, 15)));

        // Nothing before the start, and a yearly plan is the same rule with twelve.
        Assert.False(RecurrenceRule.IsDue(quarterly, new DateOnly(2025, 10, 15)));
        Assert.True(RecurrenceRule.IsDue(EveryNMonths(12, new DateOnly(2026, 2, 28)), new DateOnly(2027, 2, 28)));
    }

    [Fact]
    public void Due_days_enumerate_one_occurrence_per_month()
    {
        var fifteenth = DayOfMonth(15, new DateOnly(2026, 1, 1));

        var days = RecurrenceRule.DueDays(fifteenth, new DateOnly(2026, 1, 1), 120).ToList();

        Assert.Equal(
            [new DateOnly(2026, 1, 15), new DateOnly(2026, 2, 15), new DateOnly(2026, 3, 15), new DateOnly(2026, 4, 15)],
            days);
    }

    [Fact]
    public void Monthly_patterns_own_their_fields_and_reject_the_rest()
    {
        Assert.True(RecurrenceRule.IsValid(DayOfMonth(1, null)));
        Assert.False(RecurrenceRule.IsValid(DayOfMonth(0, null)));
        Assert.False(RecurrenceRule.IsValid(DayOfMonth(32, null)));
        Assert.False(RecurrenceRule.IsValid(DayOfMonth(15, null) with { WeekdayMask = 1 }));
        Assert.False(RecurrenceRule.IsValid(DayOfMonth(15, null) with { IntervalMonths = 1 }));

        Assert.True(RecurrenceRule.IsValid(EveryNMonths(1, new DateOnly(2026, 1, 1))));
        Assert.False(RecurrenceRule.IsValid(EveryNMonths(1, null)));
        Assert.False(RecurrenceRule.IsValid(EveryNMonths(0, new DateOnly(2026, 1, 1))));
        Assert.False(RecurrenceRule.IsValid(EveryNMonths(121, new DateOnly(2026, 1, 1))));
        Assert.False(RecurrenceRule.IsValid(EveryNMonths(1, new DateOnly(2026, 1, 1)) with { IntervalDays = 2 }));

        // The older patterns reject the new fields in turn.
        Assert.False(RecurrenceRule.IsValid(RecurrenceSpecification.Daily() with { DayOfMonth = 1 }));
        Assert.False(RecurrenceRule.IsValid(RecurrenceSpecification.AsNeeded() with { IntervalMonths = 1 }));
    }

    [PostgreSqlFact]
    public async Task A_monthly_plan_round_trips_and_is_due_only_on_its_day()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var person = await client.PostId($"/api/households/{household}/people", new { name = "Synthetic person" });
        var definition = await client.PostId(
            $"/api/households/{household}/medication-definitions",
            new { name = "Synthetic tablet", form = "Tablet", unit = "Tablet" });

        await client.PostOk($"/api/households/{household}/plans", new
        {
            personId = person,
            medicationDefinitionId = definition,
            doseNumerator = 1,
            doseDenominator = 1,
            timeZoneId = "UTC",
            kind = "Scheduled",
            pattern = "DayOfMonth",
            dayOfMonth = 15,
            localTime = "08:00:00",
            effectiveFrom = "2026-10-01",
        });

        var workspace = await client.GetOk($"/api/households/{household}/workspace");
        var listed = Assert.Single(workspace.GetProperty("plans").EnumerateArray().ToList());
        Assert.Equal("DayOfMonth", listed.GetProperty("pattern").GetString());
        Assert.Equal(15, listed.GetProperty("dayOfMonth").GetInt32());
        Assert.Equal(JsonValueKind.Null, listed.GetProperty("intervalMonths").ValueKind);

        var onTheDay = await client.GetOk($"/api/households/{household}/today?date=2026-11-15");
        Assert.Single(onTheDay.GetProperty("due").EnumerateArray().ToList());

        var dayBefore = await client.GetOk($"/api/households/{household}/today?date=2026-11-14");
        Assert.Empty(dayBefore.GetProperty("due").EnumerateArray().ToList());

        // Every two months from the 5th of October: December, not November.
        await client.PostOk($"/api/households/{household}/plans", new
        {
            personId = person,
            medicationDefinitionId = definition,
            doseNumerator = 1,
            doseDenominator = 1,
            timeZoneId = "UTC",
            kind = "Scheduled",
            pattern = "EveryNMonths",
            intervalMonths = 2,
            localTime = "08:00:00",
            effectiveFrom = "2026-10-05",
        });

        var december = await client.GetOk($"/api/households/{household}/today?date=2026-12-05");
        Assert.Single(december.GetProperty("due").EnumerateArray().ToList());
        var november = await client.GetOk($"/api/households/{household}/today?date=2026-11-05");
        Assert.Empty(november.GetProperty("due").EnumerateArray().ToList());
    }

    private static RecurrenceSpecification DayOfMonth(int day, DateOnly? from) =>
        new(TreatmentKind.Scheduled, RecurrencePattern.DayOfMonth, null, null, from, null, DayOfMonth: day);

    private static RecurrenceSpecification EveryNMonths(int months, DateOnly? from) =>
        new(TreatmentKind.Scheduled, RecurrencePattern.EveryNMonths, null, null, from, null, IntervalMonths: months);
}
