using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MedicationTracker.Api.Domain.Reports;
using MedicationTracker.Api.Domain.Scheduling;

namespace MedicationTracker.Api.Tests;

/// <summary>
/// Ending a plan, and starting it again later, without losing it or rewriting what it
/// asked for.
/// </summary>
/// <remarks>
/// <para>
/// The owner, testing the live site: "Plan sonlandırıldığında tamamen kayboldu.
/// Kurtarılabiliyor olması gerekli." Ending was a soft delete with no way back. It is now
/// a decision with a date, recorded as every other decision about a plan is: an appended
/// version, closed on the last day of doses. The plan stays listed, and restarting appends
/// a version that begins on the restart day, so the days in between are governed by the
/// ended version and ask for nothing rather than being counted as missed once the plan is
/// back.
/// </para>
/// <para>
/// That only works because of which version governs a day. Under "the highest-numbered
/// version covering the day", an older open-ended version keeps governing every day after
/// a newer version's end, which would hand the schedule straight back to the version the
/// household had just ended. The rule is therefore "the latest version that had started,
/// provided it has not ended", and these tests pin both halves. All data is synthetic.
/// </para>
/// </remarks>
public sealed class PlanEndTests
{
    private static readonly DateOnly September = new(2026, 9, 1);
    private static readonly DateOnly EndOfSeptember = new(2026, 9, 30);

    [Fact]
    public void Ending_keeps_the_days_up_to_the_end_and_asks_nothing_after()
    {
        var running = Daily(1, September, null);

        // The end copies the running version and closes it: same start, last day the 10th.
        var ended = Daily(2, September, new DateOnly(2026, 9, 10));

        var slots = Within([running, ended], September, EndOfSeptember);

        Assert.Equal(10, slots.Count);
        Assert.Equal(new DateTimeOffset(2026, 9, 10, 9, 0, 0, TimeSpan.Zero), slots[^1]);
    }

    [Fact]
    public void A_restart_continues_from_its_first_day_without_owing_the_gap()
    {
        var running = Daily(1, September, null);
        var ended = Daily(2, September, new DateOnly(2026, 9, 10));
        var restarted = Daily(3, new DateOnly(2026, 9, 21), null);

        var slots = Within([running, ended, restarted], September, EndOfSeptember);

        // Ten days before the end, ten after the restart, and nothing in between.
        Assert.Equal(20, slots.Count);
        Assert.Equal(new DateTimeOffset(2026, 9, 10, 9, 0, 0, TimeSpan.Zero), slots[9]);
        Assert.Equal(new DateTimeOffset(2026, 9, 21, 9, 0, 0, TimeSpan.Zero), slots[10]);
    }

    [Fact]
    public void A_version_that_has_not_started_yet_leaves_the_current_one_in_charge()
    {
        var current = Daily(1, September, null, new TimeOnly(9, 0));
        var later = Daily(2, new DateOnly(2026, 9, 20), null, new TimeOnly(10, 0));

        var slots = Within([current, later], September, EndOfSeptember);

        Assert.Equal(30, slots.Count);
        Assert.Equal(new DateTimeOffset(2026, 9, 19, 9, 0, 0, TimeSpan.Zero), slots[18]);
        Assert.Equal(new DateTimeOffset(2026, 9, 20, 10, 0, 0, TimeSpan.Zero), slots[19]);
    }

    [Fact]
    public void An_end_date_set_on_an_edit_really_ends_the_plan()
    {
        // The household edits the plan and sets "Bitiş" to the 10th without touching the
        // start. Under the covering rule the open-ended first version governed the 11th
        // onward, and the end date they had typed did nothing at all.
        var first = Daily(1, September, null);
        var edited = Daily(2, null, new DateOnly(2026, 9, 10));

        var slots = Within([first, edited], September, EndOfSeptember);

        Assert.Equal(10, slots.Count);
    }

    [PostgreSqlFact]
    public async Task Ending_a_plan_keeps_it_listed_and_stops_asking_from_the_next_day()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var planId = await CreateDailyPlanAsync(client, household, September);

        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        var ended = await client.PostOk($"/api/households/{household}/plans/{planId}/end", new { endsOn = yesterday });
        Assert.Equal(2, ended.GetProperty("versionNumber").GetInt32());

        var today = await client.GetOk($"/api/households/{household}/today");
        Assert.Empty(today.GetProperty("due").EnumerateArray().ToList());

        // The plan is history, not gone: it stays listed with its last day, or there is
        // nothing left to restart it from.
        var workspace = await client.GetOk($"/api/households/{household}/workspace");
        var listed = Assert.Single(workspace.GetProperty("plans").EnumerateArray().ToList());
        Assert.Equal(yesterday.ToString("yyyy-MM-dd"), listed.GetProperty("effectiveTo").GetString());

        // The last day itself still asked for its dose: the end is inclusive.
        var onTheDay = await client.GetOk($"/api/households/{household}/today?date={yesterday:yyyy-MM-dd}");
        Assert.Single(onTheDay.GetProperty("due").EnumerateArray().ToList());
    }

    [PostgreSqlFact]
    public async Task A_restarted_plan_asks_again_from_its_first_day_and_not_before()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var planId = await CreateDailyPlanAsync(client, household, September);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await client.PostOk($"/api/households/{household}/plans/{planId}/end", new { endsOn = today.AddDays(-3) });

        var restarted = await client.PostOk($"/api/households/{household}/plans/{planId}/restart", new { startsOn = today });
        Assert.Equal(3, restarted.GetProperty("versionNumber").GetInt32());

        var due = await client.GetOk($"/api/households/{household}/today");
        Assert.Single(due.GetProperty("due").EnumerateArray().ToList());

        // The gap was never owed.
        var gapDay = today.AddDays(-1);
        var gap = await client.GetOk($"/api/households/{household}/today?date={gapDay:yyyy-MM-dd}");
        Assert.Empty(gap.GetProperty("due").EnumerateArray().ToList());

        var workspace = await client.GetOk($"/api/households/{household}/workspace");
        var listed = Assert.Single(workspace.GetProperty("plans").EnumerateArray().ToList());
        Assert.Equal(today.ToString("yyyy-MM-dd"), listed.GetProperty("effectiveFrom").GetString());
        Assert.Equal(JsonValueKind.Null, listed.GetProperty("effectiveTo").ValueKind);
        Assert.False(listed.GetProperty("isPaused").GetBoolean());
    }

    [PostgreSqlFact]
    public async Task Restart_is_refused_while_the_plan_runs_and_before_its_end()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var planId = await CreateDailyPlanAsync(client, household, September);

        var running = await client.PostAsJsonAsync($"/api/households/{household}/plans/{planId}/restart", new { });
        Assert.Equal(HttpStatusCode.Conflict, running.StatusCode);
        Assert.Equal("plan_not_ended", await running.RefusalCode());

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await client.PostOk($"/api/households/{household}/plans/{planId}/end", new { endsOn = today });

        var tooEarly = await client.PostAsJsonAsync($"/api/households/{household}/plans/{planId}/restart", new { startsOn = today });
        Assert.Equal(HttpStatusCode.BadRequest, tooEarly.StatusCode);
    }

    [PostgreSqlFact]
    public async Task Ending_on_the_same_day_twice_records_one_decision()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var planId = await CreateDailyPlanAsync(client, household, September);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var first = await client.PostOk($"/api/households/{household}/plans/{planId}/end", new { endsOn = today });
        var second = await client.PostOk($"/api/households/{household}/plans/{planId}/end", new { endsOn = today });

        Assert.Equal(first.GetProperty("versionId").GetGuid(), second.GetProperty("versionId").GetGuid());
    }

    [PostgreSqlFact]
    public async Task Pausing_starts_the_paused_version_on_the_day_of_the_pause()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var planId = await CreateDailyPlanAsync(client, household, September);

        await client.PostOk($"/api/households/{household}/plans/{planId}/paused", new { isPaused = true });

        // The paused version begins today, not on the plan's original start: the days
        // before the pause stay governed by the running version and keep their doses in
        // the replay. Copying the start forward would have made a pause silently erase
        // every dose the household had recorded before it.
        var workspace = await client.GetOk($"/api/households/{household}/workspace");
        var listed = Assert.Single(workspace.GetProperty("plans").EnumerateArray().ToList());
        Assert.True(listed.GetProperty("isPaused").GetBoolean());
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"), listed.GetProperty("effectiveFrom").GetString());
    }

    private static PlanVersionSlice Daily(int number, DateOnly? from, DateOnly? to, TimeOnly? at = null) =>
        new(number, RecurrenceSpecification.Daily(from, to), at ?? new TimeOnly(9, 0), TimeZoneInfo.Utc);

    private static async Task<Guid> CreateDailyPlanAsync(HttpClient client, Guid household, DateOnly effectiveFrom)
    {
        var person = await client.PostId($"/api/households/{household}/people", new { name = "Synthetic person" });
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
            effectiveFrom,
        });

        return plan.GetProperty("id").GetGuid();
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
