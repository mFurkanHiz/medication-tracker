using System.Net;
using System.Text.Json;

namespace MedicationTracker.Api.Tests;

/// <summary>
/// The reporting and export surface against PostgreSQL, over real HTTP.
/// </summary>
/// <remarks>
/// All data is synthetic. The period is a fixed window in the past so the assertions do
/// not depend on the day the suite runs.
/// </remarks>
public sealed class ReportApiTests
{
    private const string PeriodFrom = "2026-09-01";
    private const string PeriodTo = "2026-09-05";

    // -----------------------------------------------------------------------------
    // Adherence
    // -----------------------------------------------------------------------------

    [PostgreSqlFact]
    public async Task Adherence_reports_what_was_planned_what_was_recorded_and_what_was_missed()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var definition = await CreateDefinitionAsync(client, household);
        await AddStockAsync(client, household, definition);
        var (person, planVersion) = await CreateFiveDayPlanAsync(client, household, definition);

        // Five due days. Three answered, two left alone, plus one dose nobody planned.
        await RecordSlotAsync(client, household, planVersion, "2026-09-01", "Taken");
        await RecordSlotAsync(client, household, planVersion, "2026-09-02", "Skipped");
        await RecordSlotAsync(client, household, planVersion, "2026-09-03", "PartialDose", numerator: 1, denominator: 2);
        await RecordUnplannedDoseAsync(client, household, person, definition, "2026-09-04T19:00:00Z");

        var report = await client.GetOk(
            $"/api/households/{household}/reports/adherence?from={PeriodFrom}&to={PeriodTo}");

        Assert.Equal(PeriodFrom, report.GetProperty("from").GetString());
        Assert.Equal("UTC", report.GetProperty("timeZoneId").GetString());
        Assert.Empty(report.GetProperty("unknownTimeZoneIds").EnumerateArray());

        var row = Assert.Single(report.GetProperty("rows").EnumerateArray().ToList());
        Assert.Equal(person, row.GetProperty("personId").GetGuid());
        Assert.Equal(definition, row.GetProperty("medicationDefinitionId").GetGuid());

        var tally = row.GetProperty("tally");
        Assert.Equal(5, tally.GetProperty("scheduledDoses").GetInt32());
        Assert.Equal(1, tally.GetProperty("taken").GetInt32());
        Assert.Equal(1, tally.GetProperty("skipped").GetInt32());
        Assert.Equal(1, tally.GetProperty("partialDoses").GetInt32());
        Assert.Equal(1, tally.GetProperty("extraDoses").GetInt32());
        Assert.Equal(4, tally.GetProperty("recordedDoses").GetInt32());

        // Three of the five slots were answered, so two were missed. The extra dose
        // belonged to no slot and does not reduce that.
        Assert.Equal(3, tally.GetProperty("recordedSlots").GetInt32());
        Assert.Equal(2, tally.GetProperty("missedDoses").GetInt32());

        // The skip answered its slot without medication, so two slots were dosed.
        Assert.Equal(2, tally.GetProperty("onScheduleDoses").GetInt32());
        Assert.Equal(2, tally.GetProperty("onScheduleRatio").GetProperty("numerator").GetInt32());
        Assert.Equal(5, tally.GetProperty("onScheduleRatio").GetProperty("denominator").GetInt32());

        // One person and one medication, so the household total is that row again.
        var total = report.GetProperty("total");
        Assert.Equal(5, total.GetProperty("scheduledDoses").GetInt32());
        Assert.Equal(2, total.GetProperty("missedDoses").GetInt32());
    }

    [PostgreSqlFact]
    public async Task Adherence_outside_the_period_is_not_counted_inside_it()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var definition = await CreateDefinitionAsync(client, household);
        await AddStockAsync(client, household, definition);
        var (_, planVersion) = await CreateFiveDayPlanAsync(client, household, definition);

        await RecordSlotAsync(client, household, planVersion, "2026-09-01", "Taken");
        await RecordSlotAsync(client, household, planVersion, "2026-09-05", "Taken");

        // A two-day window over the middle of the plan: both doses are outside it.
        var report = await client.GetOk(
            $"/api/households/{household}/reports/adherence?from=2026-09-02&to=2026-09-03");

        var tally = Assert.Single(report.GetProperty("rows").EnumerateArray().ToList())
            .GetProperty("tally");

        Assert.Equal(2, tally.GetProperty("scheduledDoses").GetInt32());
        Assert.Equal(0, tally.GetProperty("taken").GetInt32());
        Assert.Equal(2, tally.GetProperty("missedDoses").GetInt32());
    }

    [PostgreSqlFact]
    public async Task An_as_needed_medication_is_reported_without_inventing_missed_doses()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var definition = await CreateDefinitionAsync(client, household);
        await AddStockAsync(client, household, definition);

        var person = await client.PostId($"/api/households/{household}/people", new { name = "Synthetic person" });

        await client.PostOk($"/api/households/{household}/plans", new
        {
            personId = person,
            medicationDefinitionId = definition,
            doseNumerator = 1,
            doseDenominator = 1,
            timeZoneId = "UTC",
            kind = "AsNeeded",
            pattern = "Daily",
            effectiveFrom = PeriodFrom,
        });

        await RecordUnplannedDoseAsync(client, household, person, definition, "2026-09-02T14:00:00Z");

        var report = await client.GetOk(
            $"/api/households/{household}/reports/adherence?from={PeriodFrom}&to={PeriodTo}");

        var tally = Assert.Single(report.GetProperty("rows").EnumerateArray().ToList())
            .GetProperty("tally");

        // Used once, due never. An as-needed course has no obligation to miss.
        Assert.Equal(0, tally.GetProperty("scheduledDoses").GetInt32());
        Assert.Equal(0, tally.GetProperty("missedDoses").GetInt32());
        Assert.Equal(1, tally.GetProperty("extraDoses").GetInt32());

        // A ratio with no denominator must be absent rather than rendered as nought.
        Assert.Equal(JsonValueKind.Null, tally.GetProperty("onScheduleRatio").ValueKind);
    }

    [PostgreSqlFact]
    public async Task An_impossible_period_and_an_unknown_time_zone_are_refused()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();

        var backwards = await client.GetAsync(
            $"/api/households/{household}/reports/adherence?from=2026-09-10&to=2026-09-01");
        Assert.Equal(HttpStatusCode.BadRequest, backwards.StatusCode);

        var tooLong = await client.GetAsync(
            $"/api/households/{household}/reports/adherence?from=2020-01-01&to=2026-01-01");
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);

        var unknownZone = await client.GetAsync(
            $"/api/households/{household}/reports/adherence?timeZoneId=Mars/Olympus_Mons");
        Assert.Equal(HttpStatusCode.BadRequest, unknownZone.StatusCode);
    }

    // -----------------------------------------------------------------------------
    // Inventory
    // -----------------------------------------------------------------------------

    [PostgreSqlFact]
    public async Task The_inventory_report_carries_totals_depletion_and_the_refill_gap()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var definition = await CreateDefinitionAsync(client, household);

        // Five tablets, one a day from the 1st, and no refill until the 14th.
        await client.PostOk($"/api/households/{household}/inventory/{definition}/stock", new
        {
            capacityNumerator = 5,
            fullPackages = 1,
        });

        await CreateFiveDayPlanAsync(client, household, definition, effectiveTo: null);

        await client.PutOk(
            $"/api/households/{household}/medication-definitions/{definition}/refill-policy",
            new { nextEligibleRefillOn = "2026-09-14" });

        var report = await client.GetOk(
            $"/api/households/{household}/reports/inventory?asOf={PeriodFrom}");

        Assert.Equal(PeriodFrom, report.GetProperty("asOf").GetString());

        var row = Assert.Single(report.GetProperty("rows").EnumerateArray().ToList());
        Assert.Equal("5", row.GetProperty("total").Quantity());
        Assert.Equal(1, row.GetProperty("packageCount").GetInt32());
        Assert.True(row.GetProperty("isForecastable").GetBoolean());

        // Five tablets at one a day from the 1st: the 6th is the first day it cannot be
        // covered, and the prescription cannot be refilled until the 14th.
        Assert.Equal("2026-09-06", row.GetProperty("projectedDepletionOn").GetString());
        Assert.Equal(5, row.GetProperty("daysOfStockRemaining").GetInt32());
        Assert.True(row.GetProperty("hasRefillGap").GetBoolean());
        Assert.Equal(8, row.GetProperty("refillGapDays").GetInt32());

        Assert.Equal(1, report.GetProperty("refillGapCount").GetInt32());
    }

    // -----------------------------------------------------------------------------
    // Export
    // -----------------------------------------------------------------------------

    [PostgreSqlFact]
    public async Task The_export_carries_the_household_history_as_a_downloadable_file()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var definition = await CreateDefinitionAsync(client, household);
        await AddStockAsync(client, household, definition);
        var (person, planVersion) = await CreateFiveDayPlanAsync(client, household, definition);
        await RecordSlotAsync(client, household, planVersion, "2026-09-01", "Taken");

        var response = await client.GetAsync($"/api/households/{household}/export");
        response.EnsureSuccessStatusCode();

        // A file the household keeps, not a page it reads.
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var disposition = response.Content.Headers.ContentDisposition;
        Assert.Equal("attachment", disposition?.DispositionType);
        Assert.Contains("medication-tracker-export-", disposition?.FileName ?? "");

        var export = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        // Bumped to 2 when plan versions gained isPaused, and to 3 when medication
        // definitions gained the household's caution notes. The number has to move with
        // the shape, or a reader cannot tell a file with no warnings in it from one
        // written before warnings could be recorded at all.
        Assert.Equal(3, export.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(household, export.GetProperty("householdId").GetGuid());
        Assert.Empty(export.GetProperty("truncatedCollections").EnumerateArray());

        Assert.Equal(person, Assert.Single(export.GetProperty("people").EnumerateArray().ToList())
            .GetProperty("id").GetGuid());

        var exported = Assert.Single(export.GetProperty("medicationDefinitions").EnumerateArray().ToList());
        Assert.Equal("Parol 500 mg", exported.GetProperty("name").GetString());

        // Forty-eight across three boxes, less the one tablet the recorded dose took.
        Assert.Equal("47", exported.GetProperty("total").Quantity());

        // The whole chain is present, so the file can be read back as an audit trail
        // rather than as a summary of one.
        Assert.Equal(3, export.GetProperty("packages").EnumerateArray().Count());
        Assert.NotEmpty(export.GetProperty("inventoryLedger").EnumerateArray());
        Assert.Single(export.GetProperty("treatmentPlans").EnumerateArray().ToList());
        Assert.Single(export.GetProperty("treatmentPlanVersions").EnumerateArray().ToList());
        Assert.Single(export.GetProperty("administrations").EnumerateArray().ToList());
        Assert.Single(export.GetProperty("administrationAllocations").EnumerateArray().ToList());

        var dose = export.GetProperty("administrations").EnumerateArray().First();
        Assert.Equal("Taken", dose.GetProperty("outcome").GetString());
        Assert.Equal("1", dose.GetProperty("actualQuantity").Quantity());
    }

    [PostgreSqlFact]
    public async Task The_export_leaks_no_credentials_and_no_other_households_data()
    {
        await using var harness = new ApiTestHarness();

        var (client, household) = await harness.NewHouseholdAsync();
        var definition = await CreateDefinitionAsync(client, household);
        await AddStockAsync(client, household, definition);

        // A second household with its own synthetic data, to prove the file is scoped.
        var (neighbour, neighbourHousehold) = await harness.NewHouseholdAsync();
        var neighbourDefinition = await neighbour.PostId(
            $"/api/households/{neighbourHousehold}/medication-definitions", new
            {
                name = "Neighbour-only medication",
                form = "Tablet",
                defaultPackageCapacityNumerator = 10,
            });

        var payload = await (await client.GetAsync($"/api/households/{household}/export"))
            .Content.ReadAsStringAsync();

        // Nothing of the neighbour's, by name or by identifier.
        Assert.DoesNotContain("Neighbour-only medication", payload, StringComparison.Ordinal);
        Assert.DoesNotContain(neighbourDefinition.ToString(), payload, StringComparison.Ordinal);
        Assert.DoesNotContain(neighbourHousehold.ToString(), payload, StringComparison.Ordinal);

        // No account identity: no address, no hash, no session token. The registration
        // e-mail is a synthetic @example.invalid address, so its domain is enough.
        Assert.DoesNotContain("example.invalid", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passwordHash", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tokenHash", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("synthetic-password", payload, StringComparison.OrdinalIgnoreCase);

        var export = JsonDocument.Parse(payload).RootElement;
        Assert.Contains(
            "householdMemberships",
            export.GetProperty("excluded").EnumerateArray().Select(entry => entry.GetString()));

        // And the neighbour cannot read this household at all.
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await neighbour.GetAsync($"/api/households/{household}/export")).StatusCode);
    }

    // -----------------------------------------------------------------------------
    // Authorisation
    // -----------------------------------------------------------------------------

    [PostgreSqlFact]
    public async Task A_different_household_is_refused_every_report_and_the_export()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var definition = await CreateDefinitionAsync(client, household);
        await AddStockAsync(client, household, definition);

        var (outsider, _) = await harness.NewHouseholdAsync();

        string[] paths =
        [
            $"/api/households/{household}/reports/adherence",
            $"/api/households/{household}/reports/adherence?from={PeriodFrom}&to={PeriodTo}",
            $"/api/households/{household}/reports/inventory",
            $"/api/households/{household}/export",
        ];

        foreach (var path in paths)
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync(path)).StatusCode);
        }

        // Signed out entirely, the same reads are unauthenticated rather than forbidden.
        var anonymous = harness.NewClient();
        foreach (var path in paths)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode);
        }
    }

    // -----------------------------------------------------------------------------
    // Synthetic fixtures
    // -----------------------------------------------------------------------------

    private static Task<Guid> CreateDefinitionAsync(HttpClient client, Guid household) =>
        client.PostId($"/api/households/{household}/medication-definitions", new
        {
            name = "Parol 500 mg",
            form = "Tablet",
            strength = "500 mg",
            activeIngredients = new[] { "paracetamol" },
            defaultPackageCapacityNumerator = 20,
        });

    /// <summary>Two sealed boxes of twenty and one opened box holding eight.</summary>
    private static Task<JsonElement> AddStockAsync(HttpClient client, Guid household, Guid definition) =>
        client.PostOk($"/api/households/{household}/inventory/{definition}/stock", new
        {
            capacityNumerator = 20,
            fullPackages = 2,
            openedPackages = new[] { new { remainingNumerator = 8 } },
        });

    /// <summary>One tablet a day at 08:00 UTC across the reported period.</summary>
    private static async Task<(Guid PersonId, Guid PlanVersionId)> CreateFiveDayPlanAsync(
        HttpClient client,
        Guid household,
        Guid definition,
        string? effectiveTo = PeriodTo)
    {
        var person = await client.PostId($"/api/households/{household}/people", new { name = "Synthetic person" });

        var plan = await client.PostOk($"/api/households/{household}/plans", new
        {
            personId = person,
            medicationDefinitionId = definition,
            doseNumerator = 1,
            doseDenominator = 1,
            timeZoneId = "UTC",
            kind = "Scheduled",
            pattern = "Daily",
            effectiveFrom = PeriodFrom,
            effectiveTo,
            localTime = "08:00:00",
        });

        return (person, plan.GetProperty("versionId").GetGuid());
    }

    /// <summary>
    /// Records a dose against the plan's own slot for a day. The server refuses any
    /// instant that is not the slot the plan defines, so the time is not a free choice.
    /// </summary>
    private static Task<JsonElement> RecordSlotAsync(
        HttpClient client,
        Guid household,
        Guid planVersion,
        string day,
        string outcome,
        long? numerator = null,
        long denominator = 1) =>
        client.PostOk($"/api/households/{household}/administrations", new
        {
            planVersionId = planVersion,
            outcome,
            scheduledFor = $"{day}T08:00:00Z",
            occurredAt = $"{day}T08:05:00Z",
            actualQuantityNumerator = numerator,
            actualQuantityDenominator = denominator,
        });

    private static Task<JsonElement> RecordUnplannedDoseAsync(
        HttpClient client,
        Guid household,
        Guid person,
        Guid definition,
        string occurredAt) =>
        client.PostOk($"/api/households/{household}/administrations", new
        {
            personId = person,
            medicationDefinitionId = definition,
            outcome = "ExtraDose",
            actualQuantityNumerator = 1,
            occurredAt,
        });
}
