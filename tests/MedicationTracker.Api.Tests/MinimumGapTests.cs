using System.Text.Json;

namespace MedicationTracker.Api.Tests;

/// <summary>
/// The household's own minimum gap between doses.
/// </summary>
/// <remarks>
/// <para>
/// "En az ara (dakika)" was validated, stored, copied forward by the pause cascade,
/// exported and editable — and no code on the recording path ever read it. The form
/// promised a guard that did not exist, on the field an as-needed painkiller needs most.
/// A promise nothing keeps is worse than a missing feature: somebody relies on it.
/// </para>
/// <para>
/// It is now advisory and it says so. The gap is the household's own note, so acting on
/// it is not the software reaching a clinical conclusion. But refusing to record a dose
/// that was actually taken would make the ledger lie about the one thing it exists to
/// remember, and punishing somebody for recording the truth is the surest way to teach
/// them to stop recording it. So the screen warns and records anyway — and the test
/// below is the one that matters.
/// </para>
/// <para>All data is synthetic.</para>
/// </remarks>
public sealed class MinimumGapTests
{
    [PostgreSqlFact]
    public async Task The_gap_never_refuses_a_dose_that_was_actually_taken()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var (person, definition) = await CreateStockedMedicineAsync(client, household);

        await CreatePlanAsync(client, household, person, definition, minimumIntervalMinutes: 360);

        var now = DateTimeOffset.UtcNow;

        var first = await RecordAsync(client, household, person, definition, now.AddMinutes(-10));
        var second = await RecordAsync(client, household, person, definition, now);

        // Ten minutes apart against a six-hour gap. Both are recorded, because both
        // happened. The interface is where the household gets told it was early.
        Assert.NotEqual(
            first.GetProperty("administrationEventId").GetGuid(),
            second.GetProperty("administrationEventId").GetGuid());
    }

    [PostgreSqlFact]
    public async Task The_dose_row_says_when_the_gap_has_not_passed_yet()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var (person, definition) = await CreateStockedMedicineAsync(client, household);

        await CreatePlanAsync(client, household, person, definition, minimumIntervalMinutes: 360);

        var takenAt = DateTimeOffset.UtcNow.AddMinutes(-30);
        await RecordAsync(client, household, person, definition, takenAt);

        var due = await DueAsync(client, household);

        Assert.Equal(360, due.GetProperty("minimumIntervalMinutes").GetInt32());

        // The arithmetic is the server's, so every client agrees on the instant. Whether
        // that instant has passed is the screen's question, because the answer changes
        // every second.
        var allowedFrom = due.GetProperty("nextDoseAllowedFrom").GetDateTimeOffset();
        Assert.Equal(
            takenAt.AddMinutes(360).ToUnixTimeSeconds(),
            allowedFrom.ToUnixTimeSeconds());
        Assert.True(allowedFrom > DateTimeOffset.UtcNow);
    }

    [PostgreSqlFact]
    public async Task A_plan_with_no_gap_recorded_reports_none()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var (person, definition) = await CreateStockedMedicineAsync(client, household);

        await CreatePlanAsync(client, household, person, definition, minimumIntervalMinutes: null);
        await RecordAsync(client, household, person, definition, DateTimeOffset.UtcNow.AddMinutes(-5));

        var due = await DueAsync(client, household);

        // A household that set no gap is told nothing about one. Inventing a default
        // would be the software deciding how often a medicine may be taken.
        Assert.Equal(JsonValueKind.Null, due.GetProperty("minimumIntervalMinutes").ValueKind);
        Assert.Equal(JsonValueKind.Null, due.GetProperty("nextDoseAllowedFrom").ValueKind);

        // The dose itself is still reported, because it still happened.
        Assert.Equal(JsonValueKind.String, due.GetProperty("lastTakenAt").ValueKind);
    }

    [PostgreSqlFact]
    public async Task Nothing_taken_yet_means_no_gap_to_report()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var (person, definition) = await CreateStockedMedicineAsync(client, household);

        await CreatePlanAsync(client, household, person, definition, minimumIntervalMinutes: 360);

        var due = await DueAsync(client, household);

        Assert.Equal(JsonValueKind.Null, due.GetProperty("lastTakenAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, due.GetProperty("nextDoseAllowedFrom").ValueKind);
    }

    [PostgreSqlFact]
    public async Task A_skipped_dose_starts_no_clock()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var (person, definition) = await CreateStockedMedicineAsync(client, household);

        await CreatePlanAsync(client, household, person, definition, minimumIntervalMinutes: 360);

        await client.PostOk($"/api/households/{household}/administrations", new
        {
            personId = person,
            medicationDefinitionId = definition,
            outcome = "Skipped",
            occurredAt = DateTimeOffset.UtcNow.AddMinutes(-5),
        });

        var due = await DueAsync(client, household);

        // A dose nobody took puts nothing in the body, so it cannot make the next one
        // early. Counting it would tell somebody to wait six hours after not taking it.
        Assert.Equal(JsonValueKind.Null, due.GetProperty("lastTakenAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, due.GetProperty("nextDoseAllowedFrom").ValueKind);
    }

    [PostgreSqlFact]
    public async Task Editing_the_plan_does_not_erase_the_dose_taken_an_hour_ago()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var (person, definition) = await CreateStockedMedicineAsync(client, household);

        var planId = await CreatePlanAsync(client, household, person, definition, minimumIntervalMinutes: 360);

        var takenAt = DateTimeOffset.UtcNow.AddMinutes(-60);
        await RecordAsync(client, household, person, definition, takenAt);

        // Appending a version is an ordinary edit. The clock is keyed on the person and
        // the medicine, not on the plan version, so changing the dose cannot wipe out
        // what is already in somebody's body.
        await client.PutOk($"/api/households/{household}/plans/{planId}", new
        {
            personId = person,
            medicationDefinitionId = definition,
            doseNumerator = 2,
            doseDenominator = 1,
            timeZoneId = "UTC",
            kind = "AsNeeded",
            pattern = "Daily",
            minimumIntervalMinutes = 360,
        });

        var due = await DueAsync(client, household);

        Assert.Equal(
            takenAt.AddMinutes(360).ToUnixTimeSeconds(),
            due.GetProperty("nextDoseAllowedFrom").GetDateTimeOffset().ToUnixTimeSeconds());
    }

    private static async Task<JsonElement> DueAsync(HttpClient client, Guid household)
    {
        var today = await client.GetOk($"/api/households/{household}/today");
        return today.GetProperty("due").EnumerateArray().Single();
    }

    private static Task<JsonElement> RecordAsync(
        HttpClient client,
        Guid household,
        Guid person,
        Guid definition,
        DateTimeOffset occurredAt) =>
        client.PostOk($"/api/households/{household}/administrations", new
        {
            personId = person,
            medicationDefinitionId = definition,
            outcome = "ExtraDose",
            actualQuantityNumerator = 1,
            occurredAt,
        });

    private static async Task<Guid> CreatePlanAsync(
        HttpClient client,
        Guid household,
        Guid person,
        Guid definition,
        int? minimumIntervalMinutes)
    {
        var plan = await client.PostOk($"/api/households/{household}/plans", new
        {
            personId = person,
            medicationDefinitionId = definition,
            doseNumerator = 1,
            doseDenominator = 1,
            timeZoneId = "UTC",
            kind = "AsNeeded",
            pattern = "Daily",
            minimumIntervalMinutes,
        });

        return plan.GetProperty("id").GetGuid();
    }

    private static async Task<(Guid PersonId, Guid DefinitionId)> CreateStockedMedicineAsync(
        HttpClient client,
        Guid household)
    {
        var person = await client.PostId(
            $"/api/households/{household}/people", new { name = "Synthetic person" });
        var definition = await client.PostId(
            $"/api/households/{household}/medication-definitions",
            new { name = "Synthetic analgesic", form = "Tablet", unit = "Tablet" });

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
