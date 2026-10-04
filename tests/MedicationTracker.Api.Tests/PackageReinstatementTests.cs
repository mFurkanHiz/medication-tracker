using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MedicationTracker.Api.Domain.Inventory;
using MedicationTracker.Api.Domain.Quantities;
using Microsoft.EntityFrameworkCore;

namespace MedicationTracker.Api.Tests;

/// <summary>
/// Finding a box you had written off.
/// </summary>
/// <remarks>
/// <para>
/// The owner said it in passing — "kayıp olan bir ilacı buldum" — and the product had no
/// answer. Marking a package Lost or Disposed was a one-way door:
/// <c>MedicationPackage.Reinstate()</c> sat in the domain with nothing calling it, and no
/// endpoint or screen could bring a box back.
/// </para>
/// <para>
/// The hard part is not the state flag, it is the arithmetic. Retiring a package writes a
/// negative ledger entry for whatever was left in it, because the total must stop counting
/// medication that is gone. Reinstating therefore has to write the symmetric positive one,
/// naming the entry it undoes — anything less leaves the household permanently short by an
/// amount that was never actually missing, and the ledger is the only thing that knows how
/// much stock there is.
/// </para>
/// <para>All data is synthetic.</para>
/// </remarks>
public sealed class PackageReinstatementTests
{
    [PostgreSqlFact]
    public async Task Finding_a_lost_box_puts_exactly_what_was_lost_back()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var (definition, person) = await CreateStockedMedicineAsync(client, household);

        // Take a dose first, so the box holds an awkward amount rather than a round one.
        await RecordDoseAsync(client, household, person, definition);
        var beforeLoss = await TotalAsync(client, household, definition);
        Assert.Equal("19", beforeLoss);

        var package = await PackageIdAsync(client, household, definition);

        await client.PostOk(
            $"/api/households/{household}/inventory/packages/{package}/retire",
            new { state = "Lost", reason = "left on a train" });

        Assert.Equal("0", await TotalAsync(client, household, definition));

        await client.PostOk(
            $"/api/households/{household}/inventory/packages/{package}/reinstate",
            new { reason = "found it in the car" });

        // Conservation, exactly. Not "about right": the nineteen that were never really
        // gone are back, and the one that was taken is still gone.
        Assert.Equal(beforeLoss, await TotalAsync(client, household, definition));
    }

    [PostgreSqlFact]
    public async Task The_loss_is_not_erased_it_is_undone_and_the_pair_says_so()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var (definition, _) = await CreateStockedMedicineAsync(client, household);
        var package = await PackageIdAsync(client, household, definition);

        await client.PostOk(
            $"/api/households/{household}/inventory/packages/{package}/retire",
            new { state = "Lost", reason = "left on a train" });
        await client.PostOk(
            $"/api/households/{household}/inventory/packages/{package}/reinstate",
            new { reason = "found it in the car" });

        await using var db = harness.NewDbContext();
        var entries = await db.LedgerEntries.AsNoTracking()
            .Where(entry => entry.PackageId == package)
            .OrderBy(entry => entry.RecordedAt)
            .ToListAsync();

        var loss = Assert.Single(entries, entry => entry.EntryType == LedgerEntryType.Loss);
        var found = Assert.Single(entries, entry => entry.EntryType == LedgerEntryType.Found);

        // The loss really was recorded. Deleting it would rewrite history to say the box
        // was never lost, and the household's own account of what happened is the thing
        // this ledger exists to keep.
        Assert.Equal(new ExactQuantity(-20), loss.Quantity);
        Assert.Equal("left on a train", loss.Reason);

        Assert.Equal(new ExactQuantity(20), found.Quantity);
        Assert.Equal("found it in the car", found.Reason);

        // And the pair is readable: without the link the history shows a loss and then an
        // unexplained windfall, which is the shape of a mistake rather than a correction.
        Assert.Equal(loss.Id, found.ReversesEntryId);
    }

    [PostgreSqlFact]
    public async Task A_box_lost_and_found_twice_is_not_undone_twice()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var (definition, _) = await CreateStockedMedicineAsync(client, household);
        var package = await PackageIdAsync(client, household, definition);

        for (var round = 0; round < 2; round++)
        {
            await client.PostOk(
                $"/api/households/{household}/inventory/packages/{package}/retire",
                new { state = "Lost" });
            Assert.Equal("0", await TotalAsync(client, household, definition));

            await client.PostOk(
                $"/api/households/{household}/inventory/packages/{package}/reinstate");
            Assert.Equal("20", await TotalAsync(client, household, definition));
        }

        await using var db = harness.NewDbContext();
        var found = await db.LedgerEntries.AsNoTracking()
            .Where(entry => entry.PackageId == package && entry.EntryType == LedgerEntryType.Found)
            .ToListAsync();

        // Two losses, two finds, each naming its own loss. Reversing the older entry a
        // second time would conjure twenty tablets out of nothing.
        Assert.Equal(2, found.Count);
        Assert.Equal(2, found.Select(entry => entry.ReversesEntryId).Distinct().Count());
    }

    [PostgreSqlFact]
    public async Task A_box_that_was_empty_when_it_was_retired_brings_nothing_back()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var (definition, person) = await CreateStockedMedicineAsync(client, household);
        var package = await PackageIdAsync(client, household, definition);

        for (var dose = 0; dose < 20; dose++)
        {
            await RecordDoseAsync(client, household, person, definition);
        }

        Assert.Equal("0", await TotalAsync(client, household, definition));

        await client.PostOk(
            $"/api/households/{household}/inventory/packages/{package}/retire",
            new { state = "Disposed" });
        await client.PostOk(
            $"/api/households/{household}/inventory/packages/{package}/reinstate");

        Assert.Equal("0", await TotalAsync(client, household, definition));

        await using var db = harness.NewDbContext();

        // An empty box took nothing out of the total, so putting it back puts nothing in.
        // A zero entry would also be refused by ck_ledger_entries_non_zero, and rightly:
        // a stock change that changes no stock is a bug, not a record.
        Assert.Empty(await db.LedgerEntries.AsNoTracking()
            .Where(entry => entry.PackageId == package && entry.EntryType == LedgerEntryType.Found)
            .ToListAsync());
    }

    [PostgreSqlFact]
    public async Task A_box_comes_back_sealed_or_opened_as_it_was_left()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var (definition, person) = await CreateStockedMedicineAsync(client, household);
        var package = await PackageIdAsync(client, household, definition);

        await client.PostOk(
            $"/api/households/{household}/inventory/packages/{package}/retire", new { state = "Lost" });
        await client.PostOk(
            $"/api/households/{household}/inventory/packages/{package}/reinstate");

        // Never opened, so it returns sealed.
        Assert.Equal("Sealed", await PackageStateAsync(client, household, definition));

        // Break the seal, lose it, find it: it must not come back pretending to be new.
        await RecordDoseAsync(client, household, person, definition);
        Assert.Equal("Opened", await PackageStateAsync(client, household, definition));

        await client.PostOk(
            $"/api/households/{household}/inventory/packages/{package}/retire", new { state = "Lost" });
        await client.PostOk(
            $"/api/households/{household}/inventory/packages/{package}/reinstate");

        Assert.Equal("Opened", await PackageStateAsync(client, household, definition));
    }

    [PostgreSqlFact]
    public async Task A_box_that_was_never_retired_cannot_be_reinstated()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var (definition, _) = await CreateStockedMedicineAsync(client, household);
        var package = await PackageIdAsync(client, household, definition);

        var refusal = await client.PostAsJsonAsync(
            $"/api/households/{household}/inventory/packages/{package}/reinstate", new { });

        // Not a silent no-op: reinstating an available box would write a positive entry
        // for stock that never left, which is how a ledger starts inventing medication.
        Assert.Equal(HttpStatusCode.Conflict, refusal.StatusCode);
        Assert.Equal("20", await TotalAsync(client, household, definition));
    }

    [PostgreSqlFact]
    public async Task A_household_cannot_reinstate_another_households_package()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var (definition, _) = await CreateStockedMedicineAsync(client, household);
        var package = await PackageIdAsync(client, household, definition);

        await client.PostOk(
            $"/api/households/{household}/inventory/packages/{package}/retire", new { state = "Lost" });

        var (outsider, _) = await harness.NewHouseholdAsync();
        var refusal = await outsider.PostAsJsonAsync(
            $"/api/households/{household}/inventory/packages/{package}/reinstate", new { });

        Assert.Equal(HttpStatusCode.Forbidden, refusal.StatusCode);
    }

    private static async Task<JsonElement> MedicationAsync(HttpClient client, Guid household, Guid definition)
    {
        var workspace = await client.GetOk($"/api/households/{household}/workspace");

        return workspace.GetProperty("medications").EnumerateArray()
            .Single(medication => medication.GetProperty("id").GetGuid() == definition);
    }

    private static async Task<string> TotalAsync(HttpClient client, Guid household, Guid definition) =>
        (await MedicationAsync(client, household, definition)).GetProperty("total").Quantity();

    private static async Task<Guid> PackageIdAsync(HttpClient client, Guid household, Guid definition) =>
        (await MedicationAsync(client, household, definition))
        .GetProperty("packages").EnumerateArray().Single()
        .GetProperty("view").GetProperty("id").GetGuid();

    private static async Task<string?> PackageStateAsync(HttpClient client, Guid household, Guid definition) =>
        (await MedicationAsync(client, household, definition))
        .GetProperty("packages").EnumerateArray().Single()
        .GetProperty("view").GetProperty("state").GetString();

    private static Task<JsonElement> RecordDoseAsync(
        HttpClient client,
        Guid household,
        Guid person,
        Guid definition) =>
        client.PostOk($"/api/households/{household}/administrations", new
        {
            personId = person,
            medicationDefinitionId = definition,
            outcome = "ExtraDose",
            actualQuantityNumerator = 1,
        });

    private static async Task<(Guid DefinitionId, Guid PersonId)> CreateStockedMedicineAsync(
        HttpClient client,
        Guid household)
    {
        var person = await client.PostId(
            $"/api/households/{household}/people", new { name = "Synthetic person" });
        var definition = await client.PostId(
            $"/api/households/{household}/medication-definitions",
            new { name = "Synthetic tablet", form = "Tablet", unit = "Tablet" });

        await client.PostOk($"/api/households/{household}/inventory/{definition}/stock", new
        {
            fullPackages = 1,
            capacityNumerator = 20,
            capacityDenominator = 1,
            openedPackages = Array.Empty<object>(),
        });

        return (definition, person);
    }
}
