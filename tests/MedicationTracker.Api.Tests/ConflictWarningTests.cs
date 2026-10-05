using System.Text.Json;
using MedicationTracker.Api.Domain.Catalog;

namespace MedicationTracker.Api.Tests;

/// <summary>
/// The household's own "do not take with" tags, matched among a person's medicines on a
/// day, and read back to them in red.
/// </summary>
/// <remarks>
/// <para>
/// The owner's decision D1 (ADR 0016). The load-bearing part is what the matching refuses
/// to be: plain equality on words the household typed, scoped to one person and one day,
/// attributed to their own tag, never a block. These tests pin each of those, and the
/// owner's own example — Allerset tagged against parol and paracetamol, Parol containing
/// paracetamol — to the word.
/// </para>
/// <para>All data is synthetic.</para>
/// </remarks>
public sealed class ConflictWarningTests
{
    private static readonly Guid AllersetId = Guid.NewGuid();
    private static readonly Guid ParolId = Guid.NewGuid();

    [Theory]
    [InlineData("C Vitamini", "cvitamini")]
    [InlineData("c vitamini", "cvitamini")]
    [InlineData("İbuprofen", "ibuprofen")]
    [InlineData("ılık ŞURUP", "iliksurup")]
    [InlineData("Parol 500 mg", "parol500mg")]
    public void Normalisation_folds_case_diacritics_and_spacing_and_nothing_else(string word, string expected)
    {
        Assert.Equal(expected, DoNotTakeWithMatcher.Normalize(word));
    }

    [Fact]
    public void The_owners_example_warns_on_both_rows_with_the_reasons_they_asked_for()
    {
        var allerset = new MedicineIdentity(
            AllersetId, "Allerset", null, ["desloratadine"], ["ligone", "parol", "paracetamol", "cvitamine"]);
        var parol = new MedicineIdentity(ParolId, "Parol", null, ["paracetamol"], []);

        var found = DoNotTakeWithMatcher.Among([allerset, parol]);

        // On Allerset: do not take with Parol — reason: the words of Parol's that matched.
        var onAllerset = Assert.Single(found[AllersetId]);
        Assert.Equal("Parol", onAllerset.MedicationName);
        Assert.Equal(["Parol", "paracetamol"], onAllerset.Matched);
        Assert.Equal("Allerset", onAllerset.NotedOn);

        // On Parol: do not take with Allerset — reason: Allerset, whose tag said so.
        var onParol = Assert.Single(found[ParolId]);
        Assert.Equal("Allerset", onParol.MedicationName);
        Assert.Equal(["Allerset"], onParol.Matched);
        Assert.Equal("Allerset", onParol.NotedOn);
    }

    [Fact]
    public void A_word_the_household_did_not_write_does_not_match()
    {
        // "cvitamine" is not "C Vitamini". Guessing that it is would be the one thing
        // this must never do; the household fixes the tag, the software does not.
        var allerset = new MedicineIdentity(AllersetId, "Allerset", null, [], ["cvitamine"]);
        var vitamin = new MedicineIdentity(ParolId, "C Vitamini", null, ["askorbik asit"], []);

        var found = DoNotTakeWithMatcher.Among([allerset, vitamin]);

        Assert.Empty(found[AllersetId]);
        Assert.Empty(found[ParolId]);
    }

    [Fact]
    public void A_medicine_never_matches_itself_and_a_brand_counts_as_a_name()
    {
        var parol = new MedicineIdentity(ParolId, "Parol", "Parol Forte", ["paracetamol"], ["parol", "paracetamol"]);
        Assert.Empty(DoNotTakeWithMatcher.Among([parol])[ParolId]);

        var other = new MedicineIdentity(AllersetId, "Allerset", null, [], ["parol forte"]);
        var found = DoNotTakeWithMatcher.Among([parol, other]);
        Assert.Equal(["Parol Forte"], Assert.Single(found[AllersetId]).Matched);
    }

    [Fact]
    public void Two_medicines_naming_each_other_give_one_warning_per_row()
    {
        var a = new MedicineIdentity(AllersetId, "A", null, [], ["b"]);
        var b = new MedicineIdentity(ParolId, "B", null, [], ["a"]);

        var found = DoNotTakeWithMatcher.Among([a, b]);

        Assert.Single(found[AllersetId]);
        Assert.Single(found[ParolId]);
    }

    [PostgreSqlFact]
    public async Task The_today_rows_warn_for_the_same_person_and_day_only_and_never_block()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();

        var ayse = await client.PostId($"/api/households/{household}/people", new { name = "Synthetic Ayşe" });
        var ismail = await client.PostId($"/api/households/{household}/people", new { name = "Synthetic İsmail" });

        var allerset = await client.PostId($"/api/households/{household}/medication-definitions", new
        {
            name = "Allerset",
            form = "Tablet",
            unit = "Tablet",
            doNotTakeWithTags = new[] { "ligone", "parol", "paracetamol", "cvitamine" },
        });
        var parol = await client.PostId($"/api/households/{household}/medication-definitions", new
        {
            name = "Parol",
            form = "Tablet",
            unit = "Tablet",
            activeIngredients = new[] { "paracetamol" },
        });

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await CreateDailyPlanAsync(client, household, ayse, allerset, today);
        await CreateDailyPlanAsync(client, household, ayse, parol, today);

        // İsmail takes Parol too; nobody tagged anything of his, and Ayşe's tag is about
        // Ayşe's medicines, not the cabinet.
        await CreateDailyPlanAsync(client, household, ismail, parol, today);

        var due = (await client.GetOk($"/api/households/{household}/today")).GetProperty("due").EnumerateArray().ToList();

        var aysesAllerset = due.Single(row => row.GetProperty("personId").GetGuid() == ayse && row.GetProperty("medicationDefinitionId").GetGuid() == allerset);
        var warning = Assert.Single(aysesAllerset.GetProperty("conflicts").EnumerateArray().ToList());
        Assert.Equal("Parol", warning.GetProperty("medicationName").GetString());
        Assert.Equal(["Parol", "paracetamol"], warning.GetProperty("matched").EnumerateArray().Select(m => m.GetString()).ToList());
        Assert.Equal("Allerset", warning.GetProperty("notedOn").GetString());

        var aysesParol = due.Single(row => row.GetProperty("personId").GetGuid() == ayse && row.GetProperty("medicationDefinitionId").GetGuid() == parol);
        var mirrored = Assert.Single(aysesParol.GetProperty("conflicts").EnumerateArray().ToList());
        Assert.Equal("Allerset", mirrored.GetProperty("medicationName").GetString());

        var ismailsParol = due.Single(row => row.GetProperty("personId").GetGuid() == ismail);
        Assert.Empty(ismailsParol.GetProperty("conflicts").EnumerateArray().ToList());

        // The warning is a reminder, not a gate: the dose records.
        await client.PostOk($"/api/households/{household}/inventory/{allerset}/stock", new { capacityNumerator = 20, fullPackages = 1 });
        var dose = await client.PostOk($"/api/households/{household}/administrations", new
        {
            personId = ayse,
            medicationDefinitionId = allerset,
            outcome = "ExtraDose",
            actualQuantityNumerator = 1,
        });
        Assert.NotEqual(Guid.Empty, dose.GetProperty("administrationEventId").GetGuid());

        // The tags round-trip through the workspace, normalised like every other tag.
        var workspace = await client.GetOk($"/api/households/{household}/workspace");
        var listed = workspace.GetProperty("medications").EnumerateArray().Single(m => m.GetProperty("id").GetGuid() == allerset);
        Assert.Equal(4, listed.GetProperty("doNotTakeWithTags").GetArrayLength());
    }

    [PostgreSqlFact]
    public async Task A_medicine_not_due_today_raises_no_warning_today()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var person = await client.PostId($"/api/households/{household}/people", new { name = "Synthetic person" });

        var allerset = await client.PostId($"/api/households/{household}/medication-definitions", new
        {
            name = "Allerset", form = "Tablet", unit = "Tablet", doNotTakeWithTags = new[] { "parol" },
        });
        var parol = await client.PostId($"/api/households/{household}/medication-definitions", new
        {
            name = "Parol", form = "Tablet", unit = "Tablet",
        });

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await CreateDailyPlanAsync(client, household, person, allerset, today);

        // Parol starts tomorrow: today the two do not fall together.
        await CreateDailyPlanAsync(client, household, person, parol, today.AddDays(1));

        var due = (await client.GetOk($"/api/households/{household}/today")).GetProperty("due").EnumerateArray().ToList();
        var row = Assert.Single(due);
        Assert.Empty(row.GetProperty("conflicts").EnumerateArray().ToList());

        var tomorrow = (await client.GetOk($"/api/households/{household}/today?date={today.AddDays(1):yyyy-MM-dd}"))
            .GetProperty("due").EnumerateArray().ToList();
        Assert.All(tomorrow, r => Assert.Single(r.GetProperty("conflicts").EnumerateArray().ToList()));
    }

    private static Task CreateDailyPlanAsync(HttpClient client, Guid household, Guid person, Guid definition, DateOnly from) =>
        client.PostOk($"/api/households/{household}/plans", new
        {
            personId = person,
            medicationDefinitionId = definition,
            doseNumerator = 1,
            doseDenominator = 1,
            timeZoneId = "UTC",
            kind = "Scheduled",
            pattern = "Daily",
            localTime = "08:00:00",
            effectiveFrom = from,
        });
}
