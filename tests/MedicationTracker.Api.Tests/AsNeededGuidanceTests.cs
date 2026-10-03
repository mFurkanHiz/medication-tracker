using System.Text.Json;
using MedicationTracker.Api.Domain.Reports;
using MedicationTracker.Api.Domain.Scheduling;

namespace MedicationTracker.Api.Tests;

/// <summary>
/// An as-needed plan has no schedule. That has always been true, and it is why no dose
/// slot is ever placed for one. It does not follow that such a plan has nothing to say
/// about timing: "take it on a full stomach, preferably in the evening" is ordinary
/// advice, and the owner was right that refusing to record it was a defect in the form
/// rather than a rule of the domain.
/// </summary>
/// <remarks>
/// These tests pin the distinction that makes that safe. Guidance is recorded and shown;
/// it never becomes an obligation. If a later change made a named period imply a slot,
/// every as-needed medicine in every household would start accruing missed doses nobody
/// ever promised to take — so the separation is asserted here rather than left to
/// reviewers to notice.
///
/// All data is synthetic.
/// </remarks>
public sealed class AsNeededGuidanceTests
{
    [Fact]
    public void Food_timing_vocabulary_is_pinned_because_it_is_persisted_by_name()
    {
        // MealRelation is stored through HasConversion<string>, so these names are in the
        // database. Renaming or reordering a member silently orphans existing rows.
        Assert.Equal(
            ["Fasting", "BeforeFood", "WithFood", "AfterFood", "FullStomach"],
            Enum.GetNames<MealRelation>());

        // "Tok karnına" is what a Turkish prescription says far more often than it names a
        // particular meal, and it is broader than AfterFood: the stomach must not be empty.
        Assert.True(Enum.IsDefined(MealRelation.FullStomach));
    }

    [Fact]
    public void Named_periods_are_pinned_for_the_same_reason()
    {
        Assert.Equal(
            ["Morning", "Noon", "Afternoon", "Evening", "Night", "Bedtime"],
            Enum.GetNames<DayPeriod>());
    }

    [Fact]
    public void Guidance_cannot_reach_the_adherence_replay_at_all()
    {
        // The structural guarantee: the slice the report replays carries the recurrence,
        // the clock and the zone — and nothing else. There is no member a day period or a
        // food relation could travel in, so no change to guidance can manufacture a slot.
        var members = typeof(PlanVersionSlice)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain("DayPeriod", members);
        Assert.DoesNotContain("MealRelation", members);
    }

    [Fact]
    public void An_as_needed_plan_with_a_preferred_period_still_places_no_slots()
    {
        // The recurrence is what decides, and an as-needed recurrence places nothing over
        // a whole month however the household described its preference.
        var preferred = new PlanVersionSlice(
            1,
            RecurrenceSpecification.AsNeeded(new DateOnly(2026, 9, 1)),
            null,
            TimeZoneInfo.Utc);

        var from = new DateOnly(2026, 9, 1);
        var to = new DateOnly(2026, 9, 30);

        Assert.Empty(ScheduledSlots.Within(
            [preferred],
            from,
            to,
            new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            stoppedAt: null));
    }

    [PostgreSqlFact]
    public async Task An_as_needed_plan_records_a_preferred_period_and_food_timing()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();

        var person = await client.PostId(
            $"/api/households/{household}/people", new { name = "Synthetic person" });
        var definition = await client.PostId(
            $"/api/households/{household}/medication-definitions",
            new { name = "Synthetic analgesic", form = "Tablet", unit = "Tablet" });

        await client.PostOk($"/api/households/{household}/plans", new
        {
            personId = person,
            medicationDefinitionId = definition,
            doseNumerator = 1,
            doseDenominator = 1,
            timeZoneId = "UTC",
            kind = "AsNeeded",
            pattern = "Daily",

            // Neither of these is a schedule. Both are what the household was told.
            dayPeriod = "Evening",
            mealRelation = "FullStomach",
        });

        var workspace = await client.GetOk($"/api/households/{household}/workspace");
        var plan = Assert.Single(workspace.GetProperty("plans").EnumerateArray().ToList());

        Assert.Equal("AsNeeded", plan.GetProperty("kind").GetString());
        Assert.Equal("Evening", plan.GetProperty("dayPeriod").GetString());
        Assert.Equal("FullStomach", plan.GetProperty("mealRelation").GetString());

        // And the clock stays empty: a preference is not a time.
        Assert.Equal(JsonValueKind.Null, plan.GetProperty("localTime").ValueKind);
    }

    [PostgreSqlFact]
    public async Task A_preferred_period_does_not_turn_an_as_needed_dose_into_an_appointment()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();

        var person = await client.PostId(
            $"/api/households/{household}/people", new { name = "Synthetic person" });
        var definition = await client.PostId(
            $"/api/households/{household}/medication-definitions",
            new { name = "Synthetic analgesic", form = "Tablet", unit = "Tablet" });

        await client.PostOk($"/api/households/{household}/plans", new
        {
            personId = person,
            medicationDefinitionId = definition,
            doseNumerator = 1,
            doseDenominator = 1,
            timeZoneId = "UTC",
            kind = "AsNeeded",
            pattern = "Daily",
            dayPeriod = "Bedtime",
            mealRelation = "FullStomach",
        });

        var today = await client.GetOk($"/api/households/{household}/today");
        var due = Assert.Single(today.GetProperty("due").EnumerateArray().ToList());

        // The row still says as-needed, and carries no scheduled instant — so nothing can
        // later read it as a dose that was due at a particular moment and then missed.
        Assert.Equal("AsNeeded", due.GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, due.GetProperty("scheduledFor").ValueKind);
        Assert.Equal(JsonValueKind.Null, due.GetProperty("localTime").ValueKind);

        // The guidance rides along, which is the whole point: it is shown when it is useful.
        Assert.Equal("Bedtime", due.GetProperty("dayPeriod").GetString());
        Assert.Equal("FullStomach", due.GetProperty("mealRelation").GetString());
    }
}
