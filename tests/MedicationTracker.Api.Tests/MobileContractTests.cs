using System.Net.Http.Json;
using System.Net;
using System.Text.Json;

namespace MedicationTracker.Api.Tests;

/// <summary>
/// The server contract an offline mobile client reads.
/// </summary>
/// <remarks>
/// <para>
/// The owner deferred mobile feature code to a version after V1 and kept the mobile
/// infrastructure, with server work to continue "mobile-compatible". This file is what
/// makes that phrase mean something. Compatibility that nobody checks is not a plan, it
/// is a hope, and the thing hoping is a client that is not being written and therefore
/// cannot complain.
/// </para>
/// <para>
/// A deferred client is the worst kind of consumer to break. While it is being written
/// a dropped field is a failing build on somebody's screen. While it is deferred the same
/// drop is silent for months, and the cost lands on whoever finally picks the work up —
/// as server work they did not budget for, discovered after the mobile estimate was given.
/// </para>
/// <para>
/// So these tests pin the field set the phone consumes: the two reads the Expo client
/// already makes (<c>/workspace</c> and <c>/today</c>), plus the fields the deferred
/// slices will need, which the server already sends and the web already uses.
/// </para>
/// <para>
/// They assert <b>presence and type, not exhaustiveness</b>. Adding a field must stay
/// free — an additive change breaks no client. Removing or renaming one is what fails
/// here, and failing here is the point: the fix is either to keep the field or to change
/// this list deliberately, having decided what it costs the phone.
/// </para>
/// <para>All data is synthetic.</para>
/// </remarks>
public sealed class MobileContractTests
{
    /// <summary>
    /// Every field the phone's Today screen needs, including the deferred slices'.
    /// </summary>
    /// <remarks>
    /// The first block is already stored in the device's <c>due_doses</c> table. The
    /// second is what the deferred caution-notes and minimum-gap slices will read — kept
    /// here rather than added later, because the reason the server grew them (Sprint 3)
    /// applies to whoever is holding the box, not to whichever screen they are holding.
    /// </remarks>
    private static readonly string[] TodayRowFields =
    [
        "planId", "planVersionId", "personId", "medicationDefinitionId",
        "dose", "kind", "localTime", "dayPeriod", "mealRelation",
        "scheduledFor", "hasEnoughStock", "recordedOutcome", "recordedAdministrationId",

        "cautions", "minimumIntervalMinutes", "lastTakenAt", "nextDoseAllowedFrom",

        // Read since mobile schema v3: the "do not take with" warnings (ADR 0016).
        "conflicts",
    ];

    /// <summary>Every field the phone's cached plan row needs.</summary>
    private static readonly string[] WorkspacePlanFields =
    [
        "id", "versionId", "personId", "medicationDefinitionId", "dose", "kind",
        "pattern", "weekdayMask", "intervalDays", "effectiveFrom", "effectiveTo",
        "localTime", "timeZoneId", "dayPeriod", "mealRelation",
        "minimumIntervalMinutes", "instructions", "isPaused",

        // Read since mobile schema v3: the monthly patterns of server Sprint 7.
        "dayOfMonth", "intervalMonths",
    ];

    /// <summary>Every field the phone's cached medication row needs.</summary>
    private static readonly string[] WorkspaceMedicationFields =
    [
        "id", "name", "strength", "unit", "isArchived",
        "total", "packageCount", "packages", "cautions", "notes",

        // Read since mobile schema v4, for the Stock screen.
        "coverage",

        // Read since mobile schema v6, to offer the box size when stock is added.
        "defaultPackageCapacity",
    ];

    /// <summary>Every field the phone's cached box row needs (mobile schema v4).</summary>
    private static readonly string[] WorkspacePackageFields =
    [
        "id", "ordinal", "label", "state", "isEmpty", "nominalCapacity", "remaining", "unit",
        "expiresOn", "ownerPersonId", "holderPersonId", "isPinned", "coverage",
    ];

    [PostgreSqlFact]
    public async Task The_today_row_still_carries_every_field_the_phone_reads()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var (person, definition) = await SyntheticHouseholdAsync(client, household);

        await PlanAsync(client, household, person, definition);

        var today = await client.GetOk($"/api/households/{household}/today");
        var row = today.GetProperty("due").EnumerateArray().Single();

        AssertEveryFieldPresent(row, TodayRowFields);

        // The dose is a numerator/denominator pair all the way to the client, never a
        // decimal. Half a tablet is 1/2, and no device may be handed 0.5 to round.
        var dose = row.GetProperty("dose");
        Assert.Equal(1, dose.GetProperty("numerator").GetInt32());
        Assert.Equal(2, dose.GetProperty("denominator").GetInt32());
    }

    [PostgreSqlFact]
    public async Task The_workspace_still_carries_every_field_the_phone_caches()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var (person, definition) = await SyntheticHouseholdAsync(client, household);

        await PlanAsync(client, household, person, definition);

        var workspace = await client.GetOk($"/api/households/{household}/workspace");

        AssertEveryFieldPresent(
            workspace.GetProperty("plans").EnumerateArray().Single(), WorkspacePlanFields);
        var medication = workspace.GetProperty("medications").EnumerateArray().Single();
        AssertEveryFieldPresent(medication, WorkspaceMedicationFields);
        AssertEveryFieldPresent(
            medication.GetProperty("packages").EnumerateArray().Single().GetProperty("view"),
            WorkspacePackageFields);

        // The phone filters archived people out of its reminder query, so it has to be
        // told which they are rather than having them silently withheld.
        var personRow = workspace.GetProperty("people").EnumerateArray().Single();
        Assert.True(personRow.TryGetProperty("id", out _));
        Assert.True(personRow.TryGetProperty("name", out _));
        Assert.Equal(JsonValueKind.False, personRow.GetProperty("isArchived").ValueKind);
    }

    [PostgreSqlFact]
    public async Task A_paused_plan_is_still_sent_to_the_phone_and_still_says_so()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var (person, definition) = await SyntheticHouseholdAsync(client, household);

        var planId = await PlanAsync(client, household, person, definition);

        await client.PostOk($"/api/households/{household}/plans/{planId}/paused", new
        {
            isPaused = true,
        });

        var workspace = await client.GetOk($"/api/households/{household}/workspace");
        var plan = workspace.GetProperty("plans").EnumerateArray().Single();

        // Withholding a paused plan would be the more obvious design and it would be
        // wrong twice: the phone would have nothing to resume from, and a device that
        // synced during the pause could not tell "set aside" from "deleted" — so it
        // would keep its old reminders for a plan it believed had merely vanished.
        Assert.Equal(JsonValueKind.True, plan.GetProperty("isPaused").ValueKind);
    }

    [PostgreSqlFact]
    public async Task The_caution_notes_reach_both_reads_in_the_same_shape()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var (person, definition) = await SyntheticHouseholdAsync(client, household);

        await PlanAsync(client, household, person, definition);

        var workspace = await client.GetOk($"/api/households/{household}/workspace");
        var today = await client.GetOk($"/api/households/{household}/today");

        var fromWorkspace = workspace.GetProperty("medications").EnumerateArray().Single()
            .GetProperty("cautions");
        var fromToday = today.GetProperty("due").EnumerateArray().Single()
            .GetProperty("cautions");

        // One wire shape for every reader. A phone that had to parse the notes differently
        // depending on which endpoint it asked would eventually render them differently,
        // and the household would have to work out which screen to believe.
        Assert.Equal(fromWorkspace.GetRawText(), fromToday.GetRawText());
        Assert.Equal("Synthetic pharmacist warning", fromToday.GetProperty("warning").GetString());
    }

    [PostgreSqlFact]
    public async Task A_monthly_plan_reaches_the_phone_with_its_pattern_fields_and_an_empty_conflict_list()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var (person, definition) = await SyntheticHouseholdAsync(client, household);

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
        });

        var workspace = await client.GetOk($"/api/households/{household}/workspace");
        var plan = workspace.GetProperty("plans").EnumerateArray().Single();

        // The phone's due-day rule mirrors the server's clamp-to-month arithmetic, so it
        // needs the day exactly as written, and the other pattern's field honestly null.
        Assert.Equal("DayOfMonth", plan.GetProperty("pattern").GetString());
        Assert.Equal(15, plan.GetProperty("dayOfMonth").GetInt32());
        Assert.Equal(JsonValueKind.Null, plan.GetProperty("intervalMonths").ValueKind);

        // A day the plan is due on, so there is a Today row to look at.
        var today = await client.GetOk($"/api/households/{household}/today?date=2026-11-15");
        var row = today.GetProperty("due").EnumerateArray().Single();

        // An empty array, never absent and never null: the phone stores it as JSON and
        // renders each entry, so "nothing matched" must look like a list with nothing in it.
        Assert.Equal(JsonValueKind.Array, row.GetProperty("conflicts").ValueKind);
        Assert.Empty(row.GetProperty("conflicts").EnumerateArray());
    }

    /// <summary>Every field the phone's History screen caches from the activity feed (mobile schema v5).</summary>
    private static readonly string[] ActivityInventoryFields =
    [
        "id", "medicationDefinitionId", "entryType", "packageLabel", "quantity",
        "occurredAt", "recordedAt", "reason",
    ];

    private static readonly string[] ActivityAdministrationFields =
    [
        "id", "personId", "medicationDefinitionId", "outcome", "stockSource", "actualQuantity",
        "scheduledFor", "occurredAt", "recordedAt", "latenessMinutes",
    ];

    private static readonly string[] ActivityCorrectionFields =
    [
        "id", "administrationEventId", "fromPackageLabel", "toPackageLabel", "quantity",
        "reason", "recordedAt",
    ];

    [PostgreSqlFact]
    public async Task The_activity_feed_still_carries_every_field_the_phone_caches()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var (person, definition) = await SyntheticHouseholdAsync(client, household);

        await PlanAsync(client, household, person, definition);

        var activity = await client.GetOk($"/api/households/{household}/activity");

        // Three streams, each always present as an array: the phone replaces its cache
        // wholesale from them and an absent stream would read as "everything vanished".
        foreach (var stream in new[] { "inventory", "administrations", "allocationCorrections" })
        {
            Assert.Equal(JsonValueKind.Array, activity.GetProperty(stream).ValueKind);
        }

        // Adding the synthetic stock wrote a ledger entry, so the inventory stream has a
        // row to pin field by field.
        var entry = activity.GetProperty("inventory").EnumerateArray().First();
        AssertEveryFieldPresent(entry, ActivityInventoryFields);
        Assert.Equal("Acquire", entry.GetProperty("entryType").GetString());

        foreach (var row in activity.GetProperty("administrations").EnumerateArray())
        {
            AssertEveryFieldPresent(row, ActivityAdministrationFields);
        }

        foreach (var row in activity.GetProperty("allocationCorrections").EnumerateArray())
        {
            AssertEveryFieldPresent(row, ActivityCorrectionFields);
        }
    }

    /// <summary>
    /// The phone queues "add stock" offline and retries it on reconnect with the same key.
    /// A retry after a lost answer must bring back the boxes already created, never a
    /// second set.
    /// </summary>
    [PostgreSqlFact]
    public async Task Adding_stock_with_the_same_key_twice_creates_the_boxes_once()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var (_, definition) = await SyntheticHouseholdAsync(client, household);

        var body = new
        {
            fullPackages = 2,
            capacityNumerator = 20,
            capacityDenominator = 1,
            openedPackages = new[] { new { remainingNumerator = 8, remainingDenominator = 1 } },
            idempotencyKey = Guid.NewGuid().ToString(),
        };

        var first = await client.PostOk($"/api/households/{household}/inventory/{definition}/stock", body);
        var second = await client.PostOk($"/api/households/{household}/inventory/{definition}/stock", body);

        Assert.False(first.GetProperty("replayed").GetBoolean());
        Assert.True(second.GetProperty("replayed").GetBoolean());

        static HashSet<Guid> Ids(JsonElement response) =>
            response.GetProperty("packages").EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).ToHashSet();

        Assert.Equal(3, Ids(first).Count);
        Assert.Equal(Ids(first), Ids(second));

        // One synthetic box of 20 from the household, plus 2 × 20 and 8: nothing doubled.
        var inventory = await client.GetOk($"/api/households/{household}/inventory/{definition}");
        Assert.Equal(4, inventory.GetProperty("packageCount").GetInt32());
        Assert.Equal("68", inventory.GetProperty("total").Quantity());
    }

    /// <summary>
    /// The other commands the phone queues answer the same way on a replay: making a box
    /// active twice is fine, marking it lost twice writes one loss, and pinning a box that
    /// is no longer in use is refused with the code the phone translates.
    /// </summary>
    [PostgreSqlFact]
    public async Task The_commands_the_phone_queues_can_be_replayed()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var (_, definition) = await SyntheticHouseholdAsync(client, household);

        var inventory = await client.GetOk($"/api/households/{household}/inventory/{definition}");
        var box = inventory.GetProperty("packages").EnumerateArray().Single().GetProperty("id").GetGuid();
        var pinPath = $"/api/households/{household}/inventory/packages/{box}/pin";
        var retirePath = $"/api/households/{household}/inventory/packages/{box}/retire";

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync(pinPath, null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync(pinPath, null)).StatusCode);

        var retire = new { state = "Lost", reason = "synthetic reason" };
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync(retirePath, retire)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync(retirePath, retire)).StatusCode);

        var activity = await client.GetOk($"/api/households/{household}/activity");
        var losses = activity.GetProperty("inventory").EnumerateArray()
            .Count(entry => entry.GetProperty("entryType").GetString() == "Loss");
        Assert.Equal(1, losses);

        var after = await client.GetOk($"/api/households/{household}/inventory/{definition}");
        Assert.Equal("0", after.GetProperty("total").Quantity());

        var pinRetired = await client.PostAsync(pinPath, null);
        Assert.Equal(HttpStatusCode.Conflict, pinRetired.StatusCode);
        Assert.Equal("package_not_available", await pinRetired.RefusalCode());
    }

    private static void AssertEveryFieldPresent(JsonElement row, string[] fields)
    {
        var missing = fields.Where(field => !row.TryGetProperty(field, out _)).ToArray();

        Assert.True(
            missing.Length == 0,
            $"The mobile client reads {string.Join(", ", missing)}, and the server no longer "
            + "sends it. Either keep the field, or change MobileContractTests deliberately "
            + "after deciding what its loss costs the phone.");
    }

    private static async Task<Guid> PlanAsync(
        HttpClient client,
        Guid household,
        Guid person,
        Guid definition)
    {
        var plan = await client.PostOk($"/api/households/{household}/plans", new
        {
            personId = person,
            medicationDefinitionId = definition,

            // Half a tablet, so the fraction is on the wire rather than implied.
            doseNumerator = 1,
            doseDenominator = 2,
            timeZoneId = "UTC",
            kind = "Scheduled",
            pattern = "Daily",
            localTime = "08:00:00",
            dayPeriod = "Morning",
            mealRelation = "AfterFood",
            minimumIntervalMinutes = 360,
            instructions = "Synthetic instruction",
        });

        return plan.GetProperty("id").GetGuid();
    }

    private static async Task<(Guid PersonId, Guid DefinitionId)> SyntheticHouseholdAsync(
        HttpClient client,
        Guid household)
    {
        var person = await client.PostId(
            $"/api/households/{household}/people", new { name = "Synthetic person" });

        var definition = await client.PostId(
            $"/api/households/{household}/medication-definitions",
            new
            {
                name = "Synthetic tablet",
                form = "Tablet",
                unit = "Tablet",
                notes = "Synthetic note",
                cautionWarning = "Synthetic pharmacist warning",
            });

        await client.PostOk($"/api/households/{household}/inventory/{definition}/stock", new
        {
            fullPackages = 1,
            capacityNumerator = 20,
            capacityDenominator = 1,
            openedPackages = Array.Empty<object>(),
        });

        return (person, definition);
    }
}
