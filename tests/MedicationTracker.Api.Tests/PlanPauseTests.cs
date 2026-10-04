using System.Net.Http.Json;
using System.Text.Json;
using MedicationTracker.Api.Domain.Reports;
using MedicationTracker.Api.Domain.Scheduling;

namespace MedicationTracker.Api.Tests;

/// <summary>
/// Setting a plan aside for a while, and picking it up again.
/// </summary>
/// <remarks>
/// The owner asked for this in plain terms: "artık almadığımızı da belirtebilmeliyiz…
/// sonra da ilacın sayfasından artık almaya başladığımızı da". Until now the only way to
/// stop a plan was to end it, which is a soft delete with no way back — the wrong tool for
/// a break, and a trap for anyone who reaches for it expecting one.
///
/// The hard part is not the flag. It is that a deliberate break must not read as a run of
/// missed doses: punishing somebody for recording the truth is the surest way to teach them
/// to stop recording it. These tests pin that, and the three readers that would otherwise
/// keep acting on a plan nobody is taking.
///
/// All data is synthetic.
/// </remarks>
public sealed class PlanPauseTests
{
    private static readonly DateOnly September = new(2026, 9, 1);

    [Fact]
    public void A_paused_stretch_places_no_slots_so_it_can_never_be_missed()
    {
        // Version 1 ran daily through the first ten days; version 2 paused it from the
        // eleventh. The paused days must ask for nothing at all.
        var running = new PlanVersionSlice(
            1,
            RecurrenceSpecification.Daily(September, new DateOnly(2026, 9, 10)),
            new TimeOnly(9, 0),
            TimeZoneInfo.Utc);

        var paused = new PlanVersionSlice(
            2,
            RecurrenceSpecification.Daily(new DateOnly(2026, 9, 11), null),
            new TimeOnly(9, 0),
            TimeZoneInfo.Utc,
            IsPaused: true);

        var slots = Within([running, paused], September, new DateOnly(2026, 9, 30));

        // Ten days of real obligation, and not one slot after the pause.
        Assert.Equal(10, slots.Count);
        Assert.Equal(new DateTimeOffset(2026, 9, 10, 9, 0, 0, TimeSpan.Zero), slots[^1]);
    }

    [Fact]
    public void Pausing_does_not_rewrite_the_days_before_the_pause()
    {
        // The same ten days, replayed with only the running version present, must give the
        // same answer. If appending a pause changed the past, these two would disagree.
        var running = new PlanVersionSlice(
            1,
            RecurrenceSpecification.Daily(September, new DateOnly(2026, 9, 10)),
            new TimeOnly(9, 0),
            TimeZoneInfo.Utc);

        var paused = new PlanVersionSlice(
            2,
            RecurrenceSpecification.Daily(new DateOnly(2026, 9, 11), null),
            new TimeOnly(9, 0),
            TimeZoneInfo.Utc,
            IsPaused: true);

        var before = Within([running], September, new DateOnly(2026, 9, 30));
        var after = Within([running, paused], September, new DateOnly(2026, 9, 30));

        Assert.Equal(before, after);
    }

    [Fact]
    public void Resuming_starts_asking_again_without_back_filling_the_break()
    {
        var running = new PlanVersionSlice(
            1,
            RecurrenceSpecification.Daily(September, new DateOnly(2026, 9, 5)),
            new TimeOnly(9, 0),
            TimeZoneInfo.Utc);

        var paused = new PlanVersionSlice(
            2,
            RecurrenceSpecification.Daily(new DateOnly(2026, 9, 6), new DateOnly(2026, 9, 20)),
            new TimeOnly(9, 0),
            TimeZoneInfo.Utc,
            IsPaused: true);

        var resumed = new PlanVersionSlice(
            3,
            RecurrenceSpecification.Daily(new DateOnly(2026, 9, 21), null),
            new TimeOnly(9, 0),
            TimeZoneInfo.Utc);

        var slots = Within([running, paused, resumed], September, new DateOnly(2026, 9, 30));

        // Five days before the break, ten after it, and nothing in between. The fifteen
        // paused days are not retrospectively owed once the plan starts again.
        Assert.Equal(15, slots.Count);
        Assert.Equal(new DateTimeOffset(2026, 9, 5, 9, 0, 0, TimeSpan.Zero), slots[4]);
        Assert.Equal(new DateTimeOffset(2026, 9, 21, 9, 0, 0, TimeSpan.Zero), slots[5]);
    }

    [PostgreSqlFact]
    public async Task A_paused_plan_stops_appearing_on_today_and_comes_back_when_resumed()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var (planId, _) = await CreateDailyPlanAsync(client, household);

        var running = await client.GetOk($"/api/households/{household}/today");
        Assert.Single(running.GetProperty("due").EnumerateArray().ToList());

        var paused = await client.PostOk(
            $"/api/households/{household}/plans/{planId}/paused", new { isPaused = true });
        Assert.True(paused.GetProperty("isPaused").GetBoolean());
        Assert.Equal(2, paused.GetProperty("versionNumber").GetInt32());

        var quiet = await client.GetOk($"/api/households/{household}/today");
        Assert.Empty(quiet.GetProperty("due").EnumerateArray().ToList());

        // The plan is set aside, not gone: it must still be listed, or there is nothing
        // left to resume it from.
        var workspace = await client.GetOk($"/api/households/{household}/workspace");
        var listed = Assert.Single(workspace.GetProperty("plans").EnumerateArray().ToList());
        Assert.True(listed.GetProperty("isPaused").GetBoolean());

        var resumed = await client.PostOk(
            $"/api/households/{household}/plans/{planId}/paused", new { isPaused = false });
        Assert.False(resumed.GetProperty("isPaused").GetBoolean());
        Assert.Equal(3, resumed.GetProperty("versionNumber").GetInt32());

        var back = await client.GetOk($"/api/households/{household}/today");
        Assert.Single(back.GetProperty("due").EnumerateArray().ToList());
    }

    [PostgreSqlFact]
    public async Task Pausing_twice_records_one_decision_rather_than_two()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var (planId, _) = await CreateDailyPlanAsync(client, household);

        var first = await client.PostOk(
            $"/api/households/{household}/plans/{planId}/paused", new { isPaused = true });
        var second = await client.PostOk(
            $"/api/households/{household}/plans/{planId}/paused", new { isPaused = true });

        // The second call is not an error — a client retrying must not be punished — but it
        // must not append a version either, or the history reads as two separate pauses.
        Assert.Equal(first.GetProperty("versionNumber").GetInt32(), second.GetProperty("versionNumber").GetInt32());
        Assert.Equal(first.GetProperty("versionId").GetGuid(), second.GetProperty("versionId").GetGuid());
    }

    [PostgreSqlFact]
    public async Task Editing_a_paused_plan_does_not_quietly_resume_it()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var (planId, personId, definitionId) = await CreatePlanWithPartsAsync(client, household);

        await client.PostOk(
            $"/api/households/{household}/plans/{planId}/paused", new { isPaused = true });

        // Changing the dose of a plan you have set aside is not a statement that you have
        // started taking it again.
        await client.PutOk($"/api/households/{household}/plans/{planId}", new
        {
            personId,
            medicationDefinitionId = definitionId,
            doseNumerator = 2,
            doseDenominator = 1,
            timeZoneId = "UTC",
            kind = "Scheduled",
            pattern = "Daily",
            localTime = "08:00:00",
        });

        var workspace = await client.GetOk($"/api/households/{household}/workspace");
        var listed = Assert.Single(workspace.GetProperty("plans").EnumerateArray().ToList());

        Assert.True(listed.GetProperty("isPaused").GetBoolean());
        Assert.Equal("2", listed.GetProperty("dose").GetProperty("display").GetString());

        var today = await client.GetOk($"/api/households/{household}/today");
        Assert.Empty(today.GetProperty("due").EnumerateArray().ToList());
    }

    [PostgreSqlFact]
    public async Task A_household_cannot_pause_another_households_plan()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var (planId, _) = await CreateDailyPlanAsync(client, household);

        var (outsider, _) = await harness.NewHouseholdAsync();

        var refusal = await outsider.PostAsJsonAsync(
            $"/api/households/{household}/plans/{planId}/paused", new { isPaused = true });

        Assert.Equal(System.Net.HttpStatusCode.Forbidden, refusal.StatusCode);
    }

    private static async Task<(Guid PlanId, Guid VersionId)> CreateDailyPlanAsync(
        HttpClient client,
        Guid household)
    {
        var (planId, _, _) = await CreatePlanWithPartsAsync(client, household);
        return (planId, Guid.Empty);
    }

    private static async Task<(Guid PlanId, Guid PersonId, Guid DefinitionId)> CreatePlanWithPartsAsync(
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
