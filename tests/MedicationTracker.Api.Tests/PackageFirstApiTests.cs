using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MedicationTracker.Api.Domain.Quantities;
using MedicationTracker.Api.Modules.Inventory;
using Microsoft.EntityFrameworkCore;

namespace MedicationTracker.Api.Tests;

/// <summary>
/// End-to-end proof of the package-first model against PostgreSQL: real HTTP, real
/// transactions, real constraints.
/// </summary>
/// <remarks>
/// All data is synthetic. "Parol 500 mg" stands in for a generic tablet with invented
/// quantities; no real person or prescription is represented.
/// </remarks>
public sealed class PackageFirstApiTests
{
    // ---------------------------------------------------------------------------
    // The owner-specified mandatory acceptance scenario.
    // ---------------------------------------------------------------------------

    [PostgreSqlFact]
    public async Task Adding_two_full_boxes_and_one_opened_box_creates_three_packages_totalling_forty_eight()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var definition = await CreateParolAsync(client, household);

        await AddAcceptanceStockAsync(client, household, definition);

        var stock = await client.GetOk($"/api/households/{household}/inventory/{definition}");

        Assert.Equal("48", stock.GetProperty("total").Quantity());
        Assert.Equal(3, stock.GetProperty("packageCount").GetInt32());

        var packages = stock.GetProperty("packages").EnumerateArray().ToList();
        Assert.Equal(3, packages.Count);

        // Three physical containers, three identifiers, three friendly labels.
        Assert.Equal(3, packages.Select(p => p.GetProperty("id").GetGuid()).Distinct().Count());
        Assert.Equal([1, 2, 3], packages.Select(p => p.GetProperty("ordinal").GetInt32()));

        Assert.Equal(["20", "20", "8"], packages.Select(p => p.GetProperty("remaining").Quantity()));
        Assert.Equal(["Sealed", "Sealed", "Opened"], packages.Select(p => p.GetProperty("state").GetString()));
        Assert.All(packages, p => Assert.Equal("20", p.GetProperty("nominalCapacity").Quantity()));
    }

    [PostgreSqlFact]
    public async Task A_normal_dose_draws_from_the_opened_box_and_the_next_can_be_taken_from_a_chosen_box()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var definition = await CreateParolAsync(client, household);
        var packages = await AddAcceptanceStockAsync(client, household, definition);
        var (person, planVersion) = await CreateDailyPlanAsync(client, household, definition);

        // One tap: no package named, no amount named.
        var first = await client.PostOk($"/api/households/{household}/administrations", new
        {
            planVersionId = planVersion,
            scheduledFor = DueInstant(DateOnly.FromDateTime(DateTime.UtcNow)),
        });

        var allocation = Assert.Single(first.GetProperty("allocations").EnumerateArray().ToList());
        Assert.Equal(packages.Opened, allocation.GetProperty("packageId").GetGuid());
        Assert.Equal("1", allocation.GetProperty("quantity").Quantity());

        var afterFirst = await BalancesAsync(client, household, definition);
        Assert.Equal("20", afterFirst[packages.BoxA]);
        Assert.Equal("20", afterFirst[packages.BoxB]);
        Assert.Equal("7", afterFirst[packages.Opened]);
        Assert.Equal("47", await TotalAsync(client, household, definition));

        // The advanced path: this dose explicitly comes out of Box B.
        var second = await client.PostOk($"/api/households/{household}/administrations", new
        {
            personId = person,
            medicationDefinitionId = definition,
            outcome = "ExtraDose",
            source = "SpecificPackage",
            packageId = packages.BoxB,
            actualQuantityNumerator = 1,
        });

        Assert.Equal(
            packages.BoxB,
            Assert.Single(second.GetProperty("allocations").EnumerateArray().ToList())
                .GetProperty("packageId").GetGuid());

        var afterSecond = await BalancesAsync(client, household, definition);
        Assert.Equal("19", afterSecond[packages.BoxB]);
        Assert.Equal("7", afterSecond[packages.Opened]);
        Assert.Equal("20", afterSecond[packages.BoxA]);
    }

    [PostgreSqlFact]
    public async Task Correcting_the_stock_source_restores_the_wrong_box_and_leaves_the_total_unchanged()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var definition = await CreateParolAsync(client, household);
        var packages = await AddAcceptanceStockAsync(client, household, definition);
        var (_, planVersion) = await CreateDailyPlanAsync(client, household, definition);

        var dose = await client.PostOk($"/api/households/{household}/administrations", new
        {
            planVersionId = planVersion,
            scheduledFor = DueInstant(DateOnly.FromDateTime(DateTime.UtcNow)),
        });

        var administrationId = dose.GetProperty("administrationEventId").GetGuid();
        var allocationId = Assert.Single(dose.GetProperty("allocations").EnumerateArray().ToList())
            .GetProperty("allocationId").GetGuid();

        var totalBefore = await TotalAsync(client, household, definition);
        Assert.Equal("47", totalBefore);

        // "Actually I used Box B."
        await client.PostOk(
            $"/api/households/{household}/administrations/{administrationId}"
            + $"/allocations/{allocationId}/correction",
            new { target = "SpecificPackage", packageId = packages.BoxB, reason = "used the other box" });

        var after = await BalancesAsync(client, household, definition);

        // Box C is back to the 8 it held, Box B paid instead, and nothing else moved.
        Assert.Equal("8", after[packages.Opened]);
        Assert.Equal("19", after[packages.BoxB]);
        Assert.Equal("20", after[packages.BoxA]);
        Assert.Equal(totalBefore, await TotalAsync(client, household, definition));

        // The correction is visible, and the superseded allocation is retained.
        var allocations = await client.GetOk(
            $"/api/households/{household}/administrations/{administrationId}/allocations");

        var rows = allocations.GetProperty("allocations").EnumerateArray().ToList();
        Assert.Equal(2, rows.Count);
        Assert.Single(rows, row => row.GetProperty("isActive").GetBoolean());

        var superseded = rows.Single(row => !row.GetProperty("isActive").GetBoolean());
        Assert.Equal(packages.Opened, superseded.GetProperty("packageId").GetGuid());

        var correction = Assert.Single(allocations.GetProperty("corrections").EnumerateArray().ToList());
        Assert.Equal(3, correction.GetProperty("fromPackageLabel").GetInt32());
        Assert.Equal(2, correction.GetProperty("toPackageLabel").GetInt32());

        // The historical ledger entry was never rewritten; the correction appended to it.
        await using var db = harness.NewDbContext();
        var entries = await db.LedgerEntries.AsNoTracking()
            .Where(entry => entry.AdministrationEventId == administrationId)
            .ToListAsync();

        Assert.Equal(3, entries.Count);
        Assert.Single(entries, entry => entry.EntryType == Domain.Inventory.LedgerEntryType.Consume);
        var reversal = Assert.Single(
            entries, entry => entry.EntryType == Domain.Inventory.LedgerEntryType.CorrectionReversal);
        Assert.NotNull(reversal.ReversesEntryId);
        Assert.True(ExactQuantity.Sum(entries.Select(entry => entry.Quantity)) == new ExactQuantity(-1));
    }

    [PostgreSqlFact]
    public async Task Emptying_the_opened_box_opens_the_next_eligible_package()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var definition = await CreateParolAsync(client, household);
        var packages = await AddAcceptanceStockAsync(client, household, definition);
        var (person, _) = await CreateDailyPlanAsync(client, household, definition);

        // Eight unplanned doses empty Box C.
        for (var dose = 0; dose < 8; dose++)
        {
            var recorded = await RecordExtraDoseAsync(client, household, person, definition);
            Assert.Equal(packages.Opened, PackageOf(recorded));
        }

        Assert.Equal("0", (await BalancesAsync(client, household, definition))[packages.Opened]);

        var ninth = await RecordExtraDoseAsync(client, household, person, definition);
        Assert.Equal(packages.BoxA, PackageOf(ninth));

        var after = await client.GetOk($"/api/households/{household}/inventory/{definition}");
        Assert.Equal("39", after.GetProperty("total").Quantity());

        var boxA = after.GetProperty("packages").EnumerateArray()
            .Single(p => p.GetProperty("id").GetGuid() == packages.BoxA);

        Assert.Equal("Opened", boxA.GetProperty("state").GetString());
        Assert.Equal("19", boxA.GetProperty("remaining").Quantity());
    }

    // ---------------------------------------------------------------------------
    // Invariants that only a real database can demonstrate.
    // ---------------------------------------------------------------------------

    [PostgreSqlFact]
    public async Task Editing_the_catalog_default_capacity_never_changes_an_existing_package()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var definition = await CreateParolAsync(client, household);
        await AddAcceptanceStockAsync(client, household, definition);

        await client.PutOk($"/api/households/{household}/medication-definitions/{definition}", new
        {
            name = "Parol 500 mg",
            form = "Tablet",
            defaultPackageCapacityNumerator = 30,
        });

        var stock = await client.GetOk($"/api/households/{household}/inventory/{definition}");

        Assert.All(
            stock.GetProperty("packages").EnumerateArray(),
            package => Assert.Equal("20", package.GetProperty("nominalCapacity").Quantity()));

        Assert.Equal("48", stock.GetProperty("total").Quantity());
    }

    [PostgreSqlFact]
    public async Task A_dose_larger_than_one_package_splits_across_packages_and_sums_exactly()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var definition = await CreateParolAsync(client, household);

        // One opened box with half a tablet left, then a sealed box.
        await client.PostOk($"/api/households/{household}/inventory/{definition}/stock", new
        {
            capacityNumerator = 20,
            fullPackages = 1,
            openedPackages = new[] { new { remainingNumerator = 1, remainingDenominator = 2 } },
        });

        var (person, _) = await CreateDailyPlanAsync(client, household, definition);

        var dose = await client.PostOk($"/api/households/{household}/administrations", new
        {
            personId = person,
            medicationDefinitionId = definition,
            outcome = "ExtraDose",
            actualQuantityNumerator = 3,
            actualQuantityDenominator = 2,
        });

        var allocations = dose.GetProperty("allocations").EnumerateArray().ToList();
        Assert.Equal(2, allocations.Count);
        Assert.Equal(["1/2", "1"], allocations.Select(a => a.GetProperty("quantity").Quantity()));

        await using var db = harness.NewDbContext();
        var administrationId = dose.GetProperty("administrationEventId").GetGuid();

        var recorded = await db.AdministrationEvents.AsNoTracking()
            .SingleAsync(e => e.Id == administrationId);

        var allocated = ExactQuantity.Sum(
            await db.AdministrationAllocations.AsNoTracking()
                .Where(a => a.AdministrationEventId == administrationId && a.IsActive)
                .Select(a => a.Quantity)
                .ToListAsync());

        // The invariant the allocation table exists for.
        Assert.Equal(recorded.ActualQuantity, allocated);
    }

    [PostgreSqlFact]
    public async Task Insufficient_stock_is_refused_atomically_with_no_partial_writes()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var definition = await CreateParolAsync(client, household);

        await client.PostOk($"/api/households/{household}/inventory/{definition}/stock", new
        {
            capacityNumerator = 20,
            openedPackages = new[] { new { remainingNumerator = 2 } },
        });

        var (person, _) = await CreateDailyPlanAsync(client, household, definition);

        var response = await client.PostAsJsonAsync($"/api/households/{household}/administrations", new
        {
            personId = person,
            medicationDefinitionId = definition,
            outcome = "ExtraDose",
            actualQuantityNumerator = 5,
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("insufficient_stock", await response.RefusalCode());

        await using var db = harness.NewDbContext();
        Assert.Empty(await db.AdministrationEvents.AsNoTracking()
            .Where(e => e.HouseholdId == household).ToListAsync());
        Assert.Empty(await db.AdministrationAllocations.AsNoTracking()
            .Where(a => a.HouseholdId == household).ToListAsync());

        // The balance is untouched: no half-applied consumption.
        Assert.Equal("2", await TotalAsync(client, household, definition));
    }

    [PostgreSqlFact]
    public async Task Two_concurrent_doses_cannot_spend_the_same_last_tablet()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var definition = await CreateParolAsync(client, household);

        await client.PostOk($"/api/households/{household}/inventory/{definition}/stock", new
        {
            capacityNumerator = 20,
            openedPackages = new[] { new { remainingNumerator = 1 } },
        });

        var (person, _) = await CreateDailyPlanAsync(client, household, definition);

        var body = new
        {
            personId = person,
            medicationDefinitionId = definition,
            outcome = "ExtraDose",
            actualQuantityNumerator = 1,
        };

        // Both requests are in flight before either commits, so they genuinely contend
        // for the household lock rather than running one after the other.
        var attempts = await Task.WhenAll(
            client.PostAsJsonAsync($"/api/households/{household}/administrations", body),
            client.PostAsJsonAsync($"/api/households/{household}/administrations", body));

        Assert.Equal(1, attempts.Count(response => response.IsSuccessStatusCode));
        var refused = Assert.Single(attempts, response => !response.IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("insufficient_stock", await refused.RefusalCode());

        // Exactly one tablet left the household, so the balance is zero, never negative.
        Assert.Equal("0", await TotalAsync(client, household, definition));
    }

    [PostgreSqlFact]
    public async Task A_replayed_offline_command_records_the_dose_once_and_consumes_stock_once()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var definition = await CreateParolAsync(client, household);
        await AddAcceptanceStockAsync(client, household, definition);
        var (_, planVersion) = await CreateDailyPlanAsync(client, household, definition);

        var body = new
        {
            planVersionId = planVersion,
            scheduledFor = DueInstant(DateOnly.FromDateTime(DateTime.UtcNow)),
            idempotencyKey = "synthetic-offline-key",
        };

        var first = await client.PostOk($"/api/households/{household}/administrations", body);
        var replay = await client.PostOk($"/api/households/{household}/administrations", body);

        Assert.False(first.GetProperty("replayed").GetBoolean());
        Assert.True(replay.GetProperty("replayed").GetBoolean());
        Assert.Equal(
            first.GetProperty("administrationEventId").GetGuid(),
            replay.GetProperty("administrationEventId").GetGuid());

        // One tablet consumed in total, not two.
        Assert.Equal("47", await TotalAsync(client, household, definition));

        await using var db = harness.NewDbContext();
        Assert.Single(await db.AdministrationEvents.AsNoTracking()
            .Where(e => e.HouseholdId == household).ToListAsync());
        Assert.Single(await db.AdministrationAllocations.AsNoTracking()
            .Where(a => a.HouseholdId == household).ToListAsync());
    }

    [PostgreSqlFact]
    public async Task A_second_device_recording_the_same_slot_is_told_rather_than_overwriting_it()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var definition = await CreateParolAsync(client, household);
        await AddAcceptanceStockAsync(client, household, definition);
        var (_, planVersion) = await CreateDailyPlanAsync(client, household, definition);

        var slot = DueInstant(DateOnly.FromDateTime(DateTime.UtcNow));

        // The first phone records the dose while the second is offline.
        var first = await client.PostOk($"/api/households/{household}/administrations", new
        {
            planVersionId = planVersion,
            scheduledFor = slot,
            outcome = "Taken",
            idempotencyKey = "first-device",
        });

        // The second phone syncs later with its own key and the same outcome: it should
        // learn the slot is already recorded rather than create a duplicate.
        var agreeing = await client.PostOk($"/api/households/{household}/administrations", new
        {
            planVersionId = planVersion,
            scheduledFor = slot,
            outcome = "Taken",
            idempotencyKey = "second-device-agreeing",
        });

        Assert.True(agreeing.GetProperty("replayed").GetBoolean());
        Assert.Equal(
            first.GetProperty("administrationEventId").GetGuid(),
            agreeing.GetProperty("administrationEventId").GetGuid());

        // A different outcome is a genuine disagreement about a health record. It is
        // refused with a code the client can act on, never silently applied.
        var disagreeing = await client.PostAsJsonAsync($"/api/households/{household}/administrations", new
        {
            planVersionId = planVersion,
            scheduledFor = slot,
            outcome = "Skipped",
            idempotencyKey = "second-device-disagreeing",
        });

        Assert.Equal(HttpStatusCode.Conflict, disagreeing.StatusCode);
        Assert.Equal("slot_already_recorded", await disagreeing.RefusalCode());

        // Exactly one dose, consuming exactly one tablet.
        await using var db = harness.NewDbContext();
        Assert.Single(await db.AdministrationEvents.AsNoTracking()
            .Where(e => e.HouseholdId == household).ToListAsync());
        Assert.Equal("47", await TotalAsync(client, household, definition));
    }

    [PostgreSqlFact]
    public async Task An_untracked_dose_is_recorded_without_touching_inventory_or_going_negative()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var definition = await CreateParolAsync(client, household);
        var (person, _) = await CreateDailyPlanAsync(client, household, definition);

        // Deliberately no stock at all: the dose really happened, from elsewhere.
        var dose = await client.PostOk($"/api/households/{household}/administrations", new
        {
            personId = person,
            medicationDefinitionId = definition,
            outcome = "ExtraDose",
            source = "UntrackedExternal",
            actualQuantityNumerator = 1,
            note = "dose from a friend",
        });

        Assert.Equal("UntrackedExternal", dose.GetProperty("stockSource").GetString());
        Assert.Empty(dose.GetProperty("allocations").EnumerateArray());

        // The health record is kept; inventory is not corrupted.
        Assert.Equal("0", await TotalAsync(client, household, definition));

        await using var db = harness.NewDbContext();
        Assert.Single(await db.AdministrationEvents.AsNoTracking()
            .Where(e => e.HouseholdId == household).ToListAsync());
        Assert.Empty(await db.LedgerEntries.AsNoTracking()
            .Where(entry => entry.HouseholdId == household
                            && entry.AdministrationEventId != null).ToListAsync());

        // Nothing to correct, because nothing was allocated.
        var administrationId = dose.GetProperty("administrationEventId").GetGuid();
        var refusal = await client.PostAsJsonAsync(
            $"/api/households/{household}/administrations/{administrationId}"
            + $"/allocations/{Guid.CreateVersion7()}/correction",
            new { target = "LooseStock" });

        Assert.Equal(HttpStatusCode.Conflict, refusal.StatusCode);
        Assert.Equal("administration_untracked", await refusal.RefusalCode());
    }

    [PostgreSqlFact]
    public async Task A_correction_onto_a_box_without_enough_stock_is_refused()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var definition = await CreateParolAsync(client, household);

        var stock = await client.PostOk($"/api/households/{household}/inventory/{definition}/stock", new
        {
            capacityNumerator = 20,
            openedPackages = new[]
            {
                new { remainingNumerator = 4 },
                new { remainingNumerator = 0 },
            },
        });

        var packages = stock.GetProperty("packages").EnumerateArray().ToList();
        var empty = packages.Single(p => p.GetProperty("remaining").Quantity() == "0")
            .GetProperty("id").GetGuid();

        var (person, _) = await CreateDailyPlanAsync(client, household, definition);
        var dose = await RecordExtraDoseAsync(client, household, person, definition);

        var administrationId = dose.GetProperty("administrationEventId").GetGuid();
        var allocationId = Assert.Single(dose.GetProperty("allocations").EnumerateArray().ToList())
            .GetProperty("allocationId").GetGuid();

        var refusal = await client.PostAsJsonAsync(
            $"/api/households/{household}/administrations/{administrationId}"
            + $"/allocations/{allocationId}/correction",
            new { target = "SpecificPackage", packageId = empty });

        Assert.Equal(HttpStatusCode.Conflict, refusal.StatusCode);
        Assert.Equal("target_insufficient_stock", await refusal.RefusalCode());

        // The original allocation stands, so the record is not left in limbo.
        var allocations = await client.GetOk(
            $"/api/households/{household}/administrations/{administrationId}/allocations");

        Assert.Single(allocations.GetProperty("allocations").EnumerateArray().ToList());
        Assert.Empty(allocations.GetProperty("corrections").EnumerateArray());
    }

    [PostgreSqlFact]
    public async Task A_lent_package_is_never_drawn_from_implicitly_and_lending_changes_no_stock()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var definition = await CreateParolAsync(client, household);

        var owner = await client.PostId($"/api/households/{household}/people", new { name = "Synthetic owner" });
        var borrower = await client.PostId($"/api/households/{household}/people", new { name = "Synthetic borrower" });

        var lentStock = await client.PostOk($"/api/households/{household}/inventory/{definition}/stock", new
        {
            capacityNumerator = 20,
            openedPackages = new[] { new { remainingNumerator = 5 } },
            ownerPersonId = owner,
        });

        var lentPackage = Assert.Single(lentStock.GetProperty("packages").EnumerateArray().ToList())
            .GetProperty("id").GetGuid();

        var totalBefore = await TotalAsync(client, household, definition);

        await client.PostOk(
            $"/api/households/{household}/inventory/packages/{lentPackage}/loans",
            new { borrowerPersonId = borrower });

        // Lending moves custody only.
        Assert.Equal(totalBefore, await TotalAsync(client, household, definition));

        var afterLoan = await client.GetOk($"/api/households/{household}/inventory/{definition}");
        var package = Assert.Single(afterLoan.GetProperty("packages").EnumerateArray().ToList());
        Assert.Equal(owner, package.GetProperty("ownerPersonId").GetGuid());
        Assert.Equal(borrower, package.GetProperty("holderPersonId").GetGuid());

        // The owner can no longer draw on it implicitly; the borrower can.
        var ownerAttempt = await client.PostAsJsonAsync($"/api/households/{household}/administrations", new
        {
            personId = owner,
            medicationDefinitionId = definition,
            outcome = "ExtraDose",
            actualQuantityNumerator = 1,
        });

        Assert.Equal(HttpStatusCode.Conflict, ownerAttempt.StatusCode);
        Assert.Equal("insufficient_stock", await ownerAttempt.RefusalCode());

        var borrowerDose = await RecordExtraDoseAsync(client, household, borrower, definition);
        Assert.Equal(lentPackage, PackageOf(borrowerDose));
    }

    [PostgreSqlFact]
    public async Task A_pinned_package_is_preferred_over_the_default_order()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var definition = await CreateParolAsync(client, household);
        var packages = await AddAcceptanceStockAsync(client, household, definition);
        var (person, _) = await CreateDailyPlanAsync(client, household, definition);

        await client.PostOk($"/api/households/{household}/inventory/packages/{packages.BoxA}/pin");

        var dose = await RecordExtraDoseAsync(client, household, person, definition);

        Assert.Equal(packages.BoxA, PackageOf(dose));
        Assert.Equal("19", (await BalancesAsync(client, household, definition))[packages.BoxA]);
    }

    [PostgreSqlFact]
    public async Task Another_household_cannot_read_or_change_this_households_stock()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var definition = await CreateParolAsync(client, household);
        var packages = await AddAcceptanceStockAsync(client, household, definition);

        var (outsider, _) = await harness.NewHouseholdAsync();

        var reads = new[]
        {
            $"/api/households/{household}/inventory/{definition}",
            $"/api/households/{household}/workspace",
            $"/api/households/{household}/activity",
            $"/api/households/{household}/today",
            $"/api/households/{household}/medication-definitions/{definition}/forecast",
        };

        foreach (var path in reads)
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync(path)).StatusCode);
        }

        var addStock = await outsider.PostAsJsonAsync(
            $"/api/households/{household}/inventory/{definition}/stock",
            new { capacityNumerator = 20, fullPackages = 1 });
        Assert.Equal(HttpStatusCode.Forbidden, addStock.StatusCode);

        var pin = await outsider.PostAsJsonAsync(
            $"/api/households/{household}/inventory/packages/{packages.BoxA}/pin", new { });
        Assert.Equal(HttpStatusCode.Forbidden, pin.StatusCode);

        var edit = await outsider.PutAsJsonAsync(
            $"/api/households/{household}/medication-definitions/{definition}",
            new { name = "Hijacked", form = "Tablet" });
        Assert.Equal(HttpStatusCode.Forbidden, edit.StatusCode);

        // Nothing changed.
        Assert.Equal("48", await TotalAsync(client, household, definition));
    }

    [PostgreSqlFact]
    public async Task Retiring_a_package_removes_its_stock_through_the_ledger_rather_than_silently()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var definition = await CreateParolAsync(client, household);
        var packages = await AddAcceptanceStockAsync(client, household, definition);
        var (person, _) = await CreateDailyPlanAsync(client, household, definition);

        await client.PostOk(
            $"/api/households/{household}/inventory/packages/{packages.BoxA}/retire",
            new { state = "Lost", reason = "left on a train" });

        // The twenty tablets in the lost box leave the total.
        Assert.Equal("28", await TotalAsync(client, household, definition));

        await using var db = harness.NewDbContext();
        var loss = Assert.Single(await db.LedgerEntries.AsNoTracking()
            .Where(entry => entry.PackageId == packages.BoxA
                            && entry.EntryType == Domain.Inventory.LedgerEntryType.Loss)
            .ToListAsync());

        Assert.Equal(new ExactQuantity(-20), loss.Quantity);
        Assert.Equal("left on a train", loss.Reason);

        // A retired package is never drawn from again.
        var dose = await RecordExtraDoseAsync(client, household, person, definition);
        Assert.NotEqual(packages.BoxA, PackageOf(dose));
    }

    [PostgreSqlFact]
    public async Task The_forecast_warns_when_stock_runs_out_before_the_prescription_can_be_refilled()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var definition = await CreateParolAsync(client, household);

        // Five tablets, one a day.
        await client.PostOk($"/api/households/{household}/inventory/{definition}/stock", new
        {
            capacityNumerator = 20,
            openedPackages = new[] { new { remainingNumerator = 5 } },
        });

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await CreateDailyPlanAsync(client, household, definition);

        // The prescription may not be refilled for another fortnight.
        await client.PutOk(
            $"/api/households/{household}/medication-definitions/{definition}/refill-policy",
            new { lowStockDays = 7, nextEligibleRefillOn = today.AddDays(14) });

        var forecast = await client.GetOk(
            $"/api/households/{household}/medication-definitions/{definition}/forecast?from={today:yyyy-MM-dd}");

        Assert.True(forecast.GetProperty("isForecastable").GetBoolean());
        Assert.Equal(today.AddDays(5), forecast.GetProperty("projectedDepletionOn").GetDateOnly());
        Assert.Equal(5, forecast.GetProperty("daysOfStockRemaining").GetInt32());

        Assert.True(forecast.GetProperty("isLowStock").GetBoolean());
        Assert.Equal("WithinDayHorizon", forecast.GetProperty("lowStockReason").GetString());

        // Depletion on day 5, eligibility on day 14: nine days without medication.
        Assert.True(forecast.GetProperty("hasRefillGap").GetBoolean());
        Assert.Equal(9, forecast.GetProperty("refillGapDays").GetInt32());
    }

    [PostgreSqlFact]
    public async Task A_count_that_matches_is_still_recorded_and_a_correction_creates_a_new_revision()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var definition = await CreateParolAsync(client, household);

        await client.PostOk($"/api/households/{household}/inventory/{definition}/stock", new
        {
            capacityNumerator = 20,
            openedPackages = new[] { new { remainingNumerator = 6 } },
        });

        // Counted and matched: a zero adjustment that must still leave a record.
        var matched = await client.PostOk($"/api/households/{household}/inventory/count-sessions", new
        {
            idempotencyKey = "synthetic-count-1",
            lines = new[] { new { medicationDefinitionId = definition, observedNumerator = 6 } },
        });

        var batch = matched.GetProperty("batchId").GetGuid();
        Assert.Equal(1, matched.GetProperty("revisionNumber").GetInt32());
        Assert.Equal("6", await TotalAsync(client, household, definition));

        await using var db = harness.NewDbContext();
        var line = Assert.Single(await db.InventoryCounts.AsNoTracking()
            .Where(count => count.BatchId == batch).ToListAsync());
        Assert.True(line.Adjustment.IsZero);

        // A correction is a new revision, never an edit of the accepted count.
        var revision = await client.PostOk(
            $"/api/households/{household}/inventory/count-sessions/{batch}/revisions",
            new
            {
                idempotencyKey = "synthetic-count-2",
                lines = new[] { new { medicationDefinitionId = definition, observedNumerator = 4 } },
            });

        Assert.Equal(2, revision.GetProperty("revisionNumber").GetInt32());
        Assert.Equal("4", await TotalAsync(client, household, definition));

        // The original batch is untouched.
        Assert.True((await db.InventoryCounts.AsNoTracking()
            .SingleAsync(count => count.Id == line.Id)).Adjustment.IsZero);

        // Only the newest revision may be revised again.
        var stale = await client.PostAsJsonAsync(
            $"/api/households/{household}/inventory/count-sessions/{batch}/revisions",
            new
            {
                idempotencyKey = "synthetic-count-3",
                lines = new[] { new { medicationDefinitionId = definition, observedNumerator = 2 } },
            });

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("stale_revision", await stale.RefusalCode());
    }

    [PostgreSqlFact]
    public async Task Archiving_a_medication_keeps_its_packages_and_recorded_doses()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var definition = await CreateParolAsync(client, household);
        var packages = await AddAcceptanceStockAsync(client, household, definition);
        var (person, planVersion) = await CreateDailyPlanAsync(client, household, definition);

        var dose = await RecordExtraDoseAsync(client, household, person, definition);
        var administrationId = dose.GetProperty("administrationEventId").GetGuid();

        var response = await client.DeleteAsync(
            $"/api/households/{household}/medication-definitions/{definition}");
        response.EnsureSuccessStatusCode();

        await using var db = harness.NewDbContext();

        Assert.NotNull((await db.MedicationDefinitions.AsNoTracking()
            .SingleAsync(d => d.Id == definition)).ArchivedAt);

        // History and physical stock are intact.
        Assert.Equal(3, await db.Packages.AsNoTracking()
            .CountAsync(package => package.MedicationDefinitionId == definition));
        Assert.True(await db.AdministrationEvents.AsNoTracking()
            .AnyAsync(e => e.Id == administrationId));
        Assert.Equal("47", await TotalAsync(client, household, definition));

        // The active plan was deactivated rather than left pointing at archived stock.
        var plan = await db.TreatmentPlanVersions.AsNoTracking()
            .SingleAsync(version => version.Id == planVersion);
        Assert.NotNull((await db.TreatmentPlans.AsNoTracking()
            .SingleAsync(p => p.Id == plan.TreatmentPlanId)).DeletedAt);

        // The box the dose came from still exists with its history.
        Assert.True(await db.Packages.AsNoTracking().AnyAsync(package => package.Id == packages.BoxA));
    }

    // ---------------------------------------------------------------------------
    // Shared synthetic fixtures.
    // ---------------------------------------------------------------------------

    private sealed record AcceptancePackages(Guid BoxA, Guid BoxB, Guid Opened);

    private static Task<Guid> CreateParolAsync(HttpClient client, Guid household) =>
        client.PostId($"/api/households/{household}/medication-definitions", new
        {
            name = "Parol 500 mg",
            form = "Tablet",
            strength = "500 mg",
            activeIngredients = new[] { "paracetamol" },
            defaultPackageCapacityNumerator = 20,
        });

    /// <summary>Box A 20/20 sealed, Box B 20/20 sealed, Box C 8/20 opened.</summary>
    private static async Task<AcceptancePackages> AddAcceptanceStockAsync(
        HttpClient client,
        Guid household,
        Guid definition)
    {
        var created = await client.PostOk($"/api/households/{household}/inventory/{definition}/stock", new
        {
            capacityNumerator = 20,
            fullPackages = 2,
            openedPackages = new[] { new { remainingNumerator = 8 } },
        });

        var packages = created.GetProperty("packages").EnumerateArray().ToList();
        return new AcceptancePackages(
            packages[0].GetProperty("id").GetGuid(),
            packages[1].GetProperty("id").GetGuid(),
            packages[2].GetProperty("id").GetGuid());
    }

    private static async Task<(Guid PersonId, Guid PlanVersionId)> CreateDailyPlanAsync(
        HttpClient client,
        Guid household,
        Guid definition)
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
            localTime = "08:00:00",
        });

        return (person, plan.GetProperty("versionId").GetGuid());
    }

    private static Task<JsonElement> RecordExtraDoseAsync(
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

    private static Guid? PackageOf(JsonElement dose)
    {
        var allocation = Assert.Single(dose.GetProperty("allocations").EnumerateArray().ToList());
        var package = allocation.GetProperty("packageId");
        return package.ValueKind == JsonValueKind.Null ? null : package.GetGuid();
    }

    private static async Task<string> TotalAsync(HttpClient client, Guid household, Guid definition)
    {
        var stock = await client.GetOk($"/api/households/{household}/inventory/{definition}");
        return stock.GetProperty("total").Quantity();
    }

    private static async Task<Dictionary<Guid, string>> BalancesAsync(
        HttpClient client,
        Guid household,
        Guid definition)
    {
        var stock = await client.GetOk($"/api/households/{household}/inventory/{definition}");
        return stock.GetProperty("packages").EnumerateArray().ToDictionary(
            package => package.GetProperty("id").GetGuid(),
            package => package.GetProperty("remaining").Quantity());
    }

    /// <summary>The instant a UTC daily plan at 08:00 is due on the given local day.</summary>
    private static DateTimeOffset DueInstant(DateOnly day) =>
        new(day.ToDateTime(new TimeOnly(8, 0)), TimeSpan.Zero);
}

internal static class JsonDateExtensions
{
    public static DateOnly GetDateOnly(this JsonElement element) =>
        DateOnly.Parse(element.GetString()!, System.Globalization.CultureInfo.InvariantCulture);
}
