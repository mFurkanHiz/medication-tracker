using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace MedicationTracker.Api.Tests;

/// <summary>
/// Putting a medication or a person away, and bringing them back.
/// </summary>
/// <remarks>
/// <para>
/// Both of these were one-way doors, and neither said so. Archiving a medication
/// soft-deleted every plan for it and no route could revive a deleted plan, so "archive"
/// quietly meant "retype your doses, times, weekdays and instruction notes from memory if
/// you ever come back". Archiving a person was worse: no restore route existed at all, and
/// because nothing filtered on it, an archived person's plans kept producing doses on the
/// Today screen for ever, with their name on the row and no marker.
/// </para>
/// <para>
/// Both now set the plans aside instead of destroying them, which is what the word
/// "archive" was always promising. These tests pin the promise.
/// </para>
/// <para>All data is synthetic.</para>
/// </remarks>
public sealed class ArchiveIsNotAOneWayDoorTests
{
    [PostgreSqlFact]
    public async Task Archiving_a_medication_sets_its_plans_aside_and_restoring_leaves_them_resumable()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var (planId, _, definition) = await CreatePlanAsync(client, household);

        await client.DeleteOk($"/api/households/{household}/medication-definitions/{definition}");

        // The plan is still there — this is the whole point. It used to be gone.
        var afterArchive = await PlanAsync(client, household, planId);
        Assert.True(afterArchive.GetProperty("isPaused").GetBoolean());

        var today = await client.GetOk($"/api/households/{household}/today");
        Assert.Empty(today.GetProperty("due").EnumerateArray().ToList());

        await client.PostOk($"/api/households/{household}/medication-definitions/{definition}/restore");

        // Restoring the medication does not restart the course. Bringing a medicine back
        // out of the cupboard is not a statement that it is being taken again.
        var afterRestore = await PlanAsync(client, household, planId);
        Assert.True(afterRestore.GetProperty("isPaused").GetBoolean());

        var stillQuiet = await client.GetOk($"/api/households/{household}/today");
        Assert.Empty(stillQuiet.GetProperty("due").EnumerateArray().ToList());

        // But it can be resumed, which is exactly what was impossible before.
        await client.PostOk(
            $"/api/households/{household}/plans/{planId}/paused", new { isPaused = false });

        var back = await client.GetOk($"/api/households/{household}/today");
        Assert.Single(back.GetProperty("due").EnumerateArray().ToList());
    }

    [PostgreSqlFact]
    public async Task Archiving_a_medication_keeps_the_whole_shape_of_the_plan()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();

        var person = await client.PostId(
            $"/api/households/{household}/people", new { name = "Synthetic person" });
        var definition = await client.PostId(
            $"/api/households/{household}/medication-definitions",
            new { name = "Synthetic tablet", form = "Tablet", unit = "Tablet" });

        // Weekdays, a named period, food timing and an instruction note: the parts a
        // household would have to remember and retype if archiving destroyed the plan.
        var created = await client.PostOk($"/api/households/{household}/plans", new
        {
            personId = person,
            medicationDefinitionId = definition,
            doseNumerator = 3,
            doseDenominator = 2,
            timeZoneId = "Europe/Istanbul",
            kind = "Scheduled",
            pattern = "SelectedWeekdays",
            weekdayMask = 0b0010101,
            dayPeriod = "Bedtime",
            mealRelation = "FullStomach",
            minimumIntervalMinutes = 360,
            instructions = "Synthetic instruction note",
        });

        var planId = created.GetProperty("id").GetGuid();

        await client.DeleteOk($"/api/households/{household}/medication-definitions/{definition}");
        await client.PostOk($"/api/households/{household}/medication-definitions/{definition}/restore");
        await client.PostOk(
            $"/api/households/{household}/plans/{planId}/paused", new { isPaused = false });

        var plan = await PlanAsync(client, household, planId);

        // Improper fractions are this product's display convention, not a slip: a dose is
        // stored as an exact numerator over denominator and shown the same way.
        Assert.Equal("3/2", plan.GetProperty("dose").GetProperty("display").GetString());
        Assert.Equal("SelectedWeekdays", plan.GetProperty("pattern").GetString());
        Assert.Equal(0b0010101, plan.GetProperty("weekdayMask").GetInt32());
        Assert.Equal("Bedtime", plan.GetProperty("dayPeriod").GetString());
        Assert.Equal("FullStomach", plan.GetProperty("mealRelation").GetString());
        Assert.Equal(360, plan.GetProperty("minimumIntervalMinutes").GetInt32());
        Assert.Equal("Synthetic instruction note", plan.GetProperty("instructions").GetString());
        Assert.Equal("Europe/Istanbul", plan.GetProperty("timeZoneId").GetString());
    }

    [PostgreSqlFact]
    public async Task Archiving_a_medication_does_not_re_pause_a_plan_the_household_had_already_paused()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var (planId, _, definition) = await CreatePlanAsync(client, household);

        var paused = await client.PostOk(
            $"/api/households/{household}/plans/{planId}/paused", new { isPaused = true });
        var pausedVersion = paused.GetProperty("versionNumber").GetInt32();

        await client.DeleteOk($"/api/households/{household}/medication-definitions/{definition}");

        // Appending another paused version would record a decision nobody made, and leave
        // the household's own pause looking like the archive's doing.
        var plan = await PlanAsync(client, household, planId);
        Assert.Equal(pausedVersion, plan.GetProperty("versionNumber").GetInt32());
    }

    [PostgreSqlFact]
    public async Task An_archived_person_stops_producing_doses_and_can_be_brought_back()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var (planId, person, _) = await CreatePlanAsync(client, household);

        var running = await client.GetOk($"/api/households/{household}/today");
        Assert.Single(running.GetProperty("due").EnumerateArray().ToList());

        await client.DeleteOk($"/api/households/{household}/people/{person}");

        // This is the defect: their plans used to keep generating doses for ever, with
        // their name on the row and nothing saying they had been archived.
        var today = await client.GetOk($"/api/households/{household}/today");
        Assert.Empty(today.GetProperty("due").EnumerateArray().ToList());

        var plan = await PlanAsync(client, household, planId);
        Assert.True(plan.GetProperty("isPaused").GetBoolean());

        // And there was no way back at all: Person.Restore() existed with no route.
        await client.PostOk($"/api/households/{household}/people/{person}/restore");

        var people = await client.GetOk($"/api/households/{household}/workspace");
        var restored = Assert.Single(people.GetProperty("people").EnumerateArray().ToList());
        Assert.False(restored.GetProperty("isArchived").GetBoolean());

        await client.PostOk(
            $"/api/households/{household}/plans/{planId}/paused", new { isPaused = false });

        var back = await client.GetOk($"/api/households/{household}/today");
        Assert.Single(back.GetProperty("due").EnumerateArray().ToList());
    }

    [PostgreSqlFact]
    public async Task An_archived_persons_recorded_history_is_not_rewritten()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var (_, person, definition) = await CreatePlanAsync(client, household);

        await client.PostOk($"/api/households/{household}/inventory/{definition}/stock", new
        {
            fullPackages = 1,
            capacityNumerator = 20,
            capacityDenominator = 1,
            openedPackages = Array.Empty<object>(),
        });

        var dose = await client.PostOk($"/api/households/{household}/administrations", new
        {
            personId = person,
            medicationDefinitionId = definition,
            outcome = "ExtraDose",
            actualQuantityNumerator = 1,
        });

        var administrationId = dose.GetProperty("administrationEventId").GetGuid();

        await client.DeleteOk($"/api/households/{household}/people/{person}");

        await using var db = harness.NewDbContext();

        // Archiving is not deletion. The dose they took stays exactly where it was.
        Assert.True(await db.AdministrationEvents.AsNoTracking()
            .AnyAsync(e => e.Id == administrationId));
        Assert.NotNull((await db.People.AsNoTracking().SingleAsync(p => p.Id == person)).ArchivedAt);
    }

    [PostgreSqlFact]
    public async Task A_household_cannot_restore_another_households_person()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var (_, person, _) = await CreatePlanAsync(client, household);
        await client.DeleteOk($"/api/households/{household}/people/{person}");

        var (outsider, _) = await harness.NewHouseholdAsync();

        var refusal = await outsider.PostAsJsonAsync(
            $"/api/households/{household}/people/{person}/restore", new { });

        Assert.Equal(HttpStatusCode.Forbidden, refusal.StatusCode);
    }

    private static async Task<JsonElement> PlanAsync(HttpClient client, Guid household, Guid planId)
    {
        var workspace = await client.GetOk($"/api/households/{household}/workspace");

        return workspace.GetProperty("plans").EnumerateArray()
            .Single(plan => plan.GetProperty("id").GetGuid() == planId);
    }

    private static async Task<(Guid PlanId, Guid PersonId, Guid DefinitionId)> CreatePlanAsync(
        HttpClient client,
        Guid household)
    {
        var person = await client.PostId(
            $"/api/households/{household}/people", new { name = "Synthetic person" });
        var definition = await client.PostId(
            $"/api/households/{household}/medication-definitions",
            new { name = "Synthetic tablet", form = "Tablet", unit = "Tablet" });

        var plan = await client.PostOk($"/api/households/{household}/plans", new
        {
            personId = person,
            medicationDefinitionId = definition,
            doseNumerator = 1,
            doseDenominator = 1,
            timeZoneId = "UTC",
            kind = "Scheduled",
            pattern = "Daily",
            localTime = "08:00:00",
        });

        return (plan.GetProperty("id").GetGuid(), person, definition);
    }
}
