using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace MedicationTracker.Api.Tests;

/// <summary>
/// Who paid for a medicine, and the two dates it lets the household fill with one press.
/// </summary>
/// <remarks>
/// The owner's decisions D2 and D5: coverage lives on the medicine with a per-box
/// override, because insurance dispenses per box but a household thinks per medicine; the
/// official refill date is suggested from the insurance-covered stock only, the expected
/// end date from all stock, and neither is ever written without the household saving it.
/// All data is synthetic.
/// </remarks>
public sealed class CoverageApiTests
{
    [PostgreSqlFact]
    public async Task A_medicine_remembers_who_paid_and_a_box_may_differ()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var (_, definition) = await CreateMedicineAsync(client, household, coverage: "SelfPaid");

        await AddBoxAsync(client, household, definition, capacity: 20);
        await AddBoxAsync(client, household, definition, capacity: 20, coverage: "insurancecovered");

        var medication = await MedicationAsync(client, household, definition);
        Assert.Equal("SelfPaid", medication.GetProperty("coverage").GetString());

        var boxes = medication.GetProperty("packages").EnumerateArray()
            .Select(package => package.GetProperty("view"))
            .OrderBy(view => view.GetProperty("ordinal").GetInt32())
            .ToList();

        // The first box inherits the medicine's answer; the second carries its own,
        // parsed without regard to case.
        Assert.Equal(JsonValueKind.Null, boxes[0].GetProperty("coverage").ValueKind);
        Assert.Equal("InsuranceCovered", boxes[1].GetProperty("coverage").GetString());

        // Clearing the override returns the box to the medicine's answer.
        var secondId = boxes[1].GetProperty("id").GetGuid();
        await client.PutOk($"/api/households/{household}/inventory/packages/{secondId}", new { coverage = (string?)null });
        var cleared = (await MedicationAsync(client, household, definition)).GetProperty("packages").EnumerateArray()
            .Select(package => package.GetProperty("view"))
            .Single(view => view.GetProperty("id").GetGuid() == secondId);
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("coverage").ValueKind);
    }

    [PostgreSqlFact]
    public async Task The_official_date_counts_covered_stock_only_and_the_expected_date_counts_all()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var (person, definition) = await CreateMedicineAsync(client, household);

        await AddBoxAsync(client, household, definition, capacity: 20);
        await AddBoxAsync(client, household, definition, capacity: 20, coverage: "SelfPaid");
        await CreatePlanAsync(client, household, person, definition, kind: "Scheduled", dose: 2);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var forecast = await client.GetOk($"/api/households/{household}/medication-definitions/{definition}/forecast");

        Assert.True(forecast.GetProperty("canSuggest").GetBoolean());
        Assert.Equal("20", forecast.GetProperty("coveredBalance").GetProperty("display").GetString());

        // Twenty covered tablets at two a day: the pharmacy's clock says ten days. All
        // forty on hand: twenty days. The owner's example, to the day.
        Assert.Equal(today.AddDays(10).ToString("yyyy-MM-dd"), forecast.GetProperty("suggestedNextEligibleRefillOn").GetString());
        Assert.Equal(today.AddDays(20).ToString("yyyy-MM-dd"), forecast.GetProperty("suggestedDepletionOn").GetString());
    }

    [PostgreSqlFact]
    public async Task A_self_paid_medicine_has_no_covered_supply_to_refill()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var (person, definition) = await CreateMedicineAsync(client, household, coverage: "SelfPaid");

        await AddBoxAsync(client, household, definition, capacity: 20);
        await CreatePlanAsync(client, household, person, definition, kind: "AsNeeded", dose: 1);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var forecast = await client.GetOk($"/api/households/{household}/medication-definitions/{definition}/forecast");

        // Nothing covered, so the covered supply is already gone; and the as-needed plan
        // is counted daily for the suggestion while the forecast proper still declines.
        Assert.Equal("0", forecast.GetProperty("coveredBalance").GetProperty("display").GetString());
        Assert.Equal(today.ToString("yyyy-MM-dd"), forecast.GetProperty("suggestedNextEligibleRefillOn").GetString());
        Assert.Equal(today.AddDays(20).ToString("yyyy-MM-dd"), forecast.GetProperty("suggestedDepletionOn").GetString());
        Assert.False(forecast.GetProperty("isForecastable").GetBoolean());
    }

    [PostgreSqlFact]
    public async Task Without_a_plan_nothing_is_suggested()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var (_, definition) = await CreateMedicineAsync(client, household);
        await AddBoxAsync(client, household, definition, capacity: 20);

        var forecast = await client.GetOk($"/api/households/{household}/medication-definitions/{definition}/forecast");

        Assert.False(forecast.GetProperty("canSuggest").GetBoolean());
        Assert.Equal(JsonValueKind.Null, forecast.GetProperty("suggestedNextEligibleRefillOn").ValueKind);
        Assert.Equal(JsonValueKind.Null, forecast.GetProperty("suggestedDepletionOn").ValueKind);
    }

    [PostgreSqlFact]
    public async Task The_expected_end_date_is_the_households_to_write_and_round_trips()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var (_, definition) = await CreateMedicineAsync(client, household);

        await client.PutOk($"/api/households/{household}/medication-definitions/{definition}/refill-policy", new
        {
            nextEligibleRefillOn = "2026-11-01",
            expectedDepletionOn = "2026-11-05",
        });

        var policy = (await MedicationAsync(client, household, definition)).GetProperty("refillPolicy");
        Assert.Equal("2026-11-01", policy.GetProperty("nextEligibleRefillOn").GetString());
        Assert.Equal("2026-11-05", policy.GetProperty("expectedDepletionOn").GetString());

        var forecast = await client.GetOk($"/api/households/{household}/medication-definitions/{definition}/forecast");
        Assert.Equal("2026-11-05", forecast.GetProperty("expectedDepletionOn").GetString());
    }

    [PostgreSqlFact]
    public async Task An_unknown_coverage_is_refused_by_name()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();

        var refusal = await client.PostAsJsonAsync(
            $"/api/households/{household}/medication-definitions",
            new { name = "Synthetic tablet", form = "Tablet", coverage = "Free" });

        Assert.Equal(HttpStatusCode.BadRequest, refusal.StatusCode);
    }

    private static async Task<(Guid Person, Guid Definition)> CreateMedicineAsync(
        HttpClient client,
        Guid household,
        string? coverage = null)
    {
        var person = await client.PostId($"/api/households/{household}/people", new { name = "Synthetic person" });
        var definition = await client.PostId(
            $"/api/households/{household}/medication-definitions",
            new { name = "Synthetic tablet", form = "Tablet", unit = "Tablet", coverage });
        return (person, definition);
    }

    private static Task AddBoxAsync(HttpClient client, Guid household, Guid definition, int capacity, string? coverage = null) =>
        client.PostOk($"/api/households/{household}/inventory/{definition}/stock", new
        {
            capacityNumerator = capacity,
            fullPackages = 1,
            coverage,
        });

    private static Task CreatePlanAsync(HttpClient client, Guid household, Guid person, Guid definition, string kind, int dose) =>
        client.PostOk($"/api/households/{household}/plans", new
        {
            personId = person,
            medicationDefinitionId = definition,
            doseNumerator = dose,
            doseDenominator = 1,
            timeZoneId = "UTC",
            kind,
            pattern = "Daily",
            localTime = kind == "Scheduled" ? "08:00:00" : null,
            effectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow),
        });

    private static async Task<JsonElement> MedicationAsync(HttpClient client, Guid household, Guid definition)
    {
        var workspace = await client.GetOk($"/api/households/{household}/workspace");
        return workspace.GetProperty("medications").EnumerateArray()
            .Single(medication => medication.GetProperty("id").GetGuid() == definition);
    }
}
