using System.Net;
using System.Text.Json;

namespace MedicationTracker.Api.Tests;

/// <summary>
/// Reading accepted counts back, which is what lets a client offer to revise one.
/// </summary>
/// <remarks>
/// All data is synthetic. The write path — revisioning, stale-revision rejection and a
/// matched count still being recorded — is covered by
/// <c>PackageFirstApiTests.A_count_that_matches_is_still_recorded_and_a_correction_creates_a_new_revision</c>.
/// These cover the read the client needs on top of it.
/// </remarks>
public sealed class InventoryCountApiTests
{
    [PostgreSqlFact]
    public async Task An_accepted_count_reads_back_with_what_was_expected_and_what_was_found()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var definition = await CreateDefinitionAsync(client, household);

        await client.PostOk($"/api/households/{household}/inventory/{definition}/stock", new
        {
            capacityNumerator = 20,
            openedPackages = new[] { new { remainingNumerator = 6 } },
        });

        // The household counts four where the ledger projected six.
        await client.PostOk($"/api/households/{household}/inventory/count-sessions", new
        {
            idempotencyKey = $"synthetic-{Guid.CreateVersion7():N}",
            lines = new[] { new { medicationDefinitionId = definition, observedNumerator = 4 } },
            note = "Mutfak dolabı",
        });

        var sessions = await ReadSessionsAsync(client, household);
        var session = Assert.Single(sessions);

        Assert.Equal(1, session.GetProperty("revisionNumber").GetInt32());
        Assert.Equal(JsonValueKind.Null, session.GetProperty("previousBatchId").ValueKind);

        // Nothing has superseded it, so the client may offer a correction.
        Assert.True(session.GetProperty("isRevisable").GetBoolean());

        var line = Assert.Single(session.GetProperty("lines").EnumerateArray().ToList());
        Assert.Equal(definition, line.GetProperty("medicationDefinitionId").GetGuid());
        Assert.Equal("6", line.GetProperty("before").Quantity());
        Assert.Equal("4", line.GetProperty("observed").Quantity());
        Assert.Equal("-2", line.GetProperty("adjustment").Quantity());

        // A medication-level count names no package.
        Assert.Equal(JsonValueKind.Null, line.GetProperty("packageId").ValueKind);
        Assert.Equal(JsonValueKind.Null, line.GetProperty("packageLabel").ValueKind);
    }

    [PostgreSqlFact]
    public async Task A_revised_count_stops_being_revisable_and_the_revision_takes_over()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var definition = await CreateDefinitionAsync(client, household);

        await client.PostOk($"/api/households/{household}/inventory/{definition}/stock", new
        {
            capacityNumerator = 20,
            openedPackages = new[] { new { remainingNumerator = 6 } },
        });

        var first = await client.PostOk($"/api/households/{household}/inventory/count-sessions", new
        {
            idempotencyKey = $"synthetic-{Guid.CreateVersion7():N}",
            lines = new[] { new { medicationDefinitionId = definition, observedNumerator = 4 } },
        });

        var original = first.GetProperty("batchId").GetGuid();

        await client.PostOk(
            $"/api/households/{household}/inventory/count-sessions/{original}/revisions",
            new
            {
                idempotencyKey = $"synthetic-{Guid.CreateVersion7():N}",
                lines = new[] { new { medicationDefinitionId = definition, observedNumerator = 5 } },
            });

        var sessions = await ReadSessionsAsync(client, household);
        Assert.Equal(2, sessions.Count);

        var revision = sessions.Single(session => session.GetProperty("revisionNumber").GetInt32() == 2);
        var superseded = sessions.Single(session => session.GetProperty("revisionNumber").GetInt32() == 1);

        Assert.Equal(original, revision.GetProperty("previousBatchId").GetGuid());

        // The chain stays linear: the newest link is the only one that may be revised,
        // which is the rule the write path refuses to break.
        Assert.True(revision.GetProperty("isRevisable").GetBoolean());
        Assert.False(superseded.GetProperty("isRevisable").GetBoolean());

        // The original is still readable exactly as it was accepted.
        var originalLine = Assert.Single(superseded.GetProperty("lines").EnumerateArray().ToList());
        Assert.Equal("4", originalLine.GetProperty("observed").Quantity());
        Assert.Equal("-2", originalLine.GetProperty("adjustment").Quantity());

        // The revision measures against the balance the first count left behind.
        var revisionLine = Assert.Single(revision.GetProperty("lines").EnumerateArray().ToList());
        Assert.Equal("4", revisionLine.GetProperty("before").Quantity());
        Assert.Equal("5", revisionLine.GetProperty("observed").Quantity());
        Assert.Equal("1", revisionLine.GetProperty("adjustment").Quantity());
    }

    [PostgreSqlFact]
    public async Task A_package_level_count_reads_back_against_the_box_that_was_counted()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var definition = await CreateDefinitionAsync(client, household);

        var created = await client.PostOk($"/api/households/{household}/inventory/{definition}/stock", new
        {
            capacityNumerator = 20,
            fullPackages = 2,
            openedPackages = new[] { new { remainingNumerator = 8 } },
        });

        var packages = created.GetProperty("packages").EnumerateArray().ToList();
        var opened = packages[2].GetProperty("id").GetGuid();

        // Advanced reconciliation: one physical box, not the medication as a whole.
        await client.PostOk($"/api/households/{household}/inventory/count-sessions", new
        {
            idempotencyKey = $"synthetic-{Guid.CreateVersion7():N}",
            lines = new[]
            {
                new { medicationDefinitionId = definition, observedNumerator = 7, packageId = opened },
            },
        });

        var session = Assert.Single(await ReadSessionsAsync(client, household));
        var line = Assert.Single(session.GetProperty("lines").EnumerateArray().ToList());

        Assert.Equal(opened, line.GetProperty("packageId").GetGuid());

        // Box 3 of three, by its friendly ordinal.
        Assert.Equal(3, line.GetProperty("packageLabel").GetInt32());
        Assert.Equal("8", line.GetProperty("before").Quantity());
        Assert.Equal("7", line.GetProperty("observed").Quantity());

        // Only the counted box moved; the household total fell by exactly one.
        var stock = await client.GetOk($"/api/households/{household}/inventory/{definition}");
        Assert.Equal("47", stock.GetProperty("total").Quantity());
    }

    [PostgreSqlFact]
    public async Task A_household_with_no_counts_reads_back_an_empty_list()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();

        Assert.Empty(await ReadSessionsAsync(client, household));
    }

    [PostgreSqlFact]
    public async Task A_different_household_cannot_read_the_counts()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var definition = await CreateDefinitionAsync(client, household);

        await client.PostOk($"/api/households/{household}/inventory/{definition}/stock", new
        {
            capacityNumerator = 20,
            fullPackages = 1,
        });

        await client.PostOk($"/api/households/{household}/inventory/count-sessions", new
        {
            idempotencyKey = $"synthetic-{Guid.CreateVersion7():N}",
            lines = new[] { new { medicationDefinitionId = definition, observedNumerator = 19 } },
        });

        var (outsider, _) = await harness.NewHouseholdAsync();
        var path = $"/api/households/{household}/inventory/count-sessions";

        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await harness.NewClient().GetAsync(path)).StatusCode);

        // The owning household still reads its own count, so the refusal is about who is
        // asking rather than about the data being unreachable.
        Assert.Single(await ReadSessionsAsync(client, household));
    }

    private static async Task<List<JsonElement>> ReadSessionsAsync(HttpClient client, Guid household)
    {
        var body = await client.GetOk($"/api/households/{household}/inventory/count-sessions");
        return body.GetProperty("sessions").EnumerateArray().ToList();
    }

    private static Task<Guid> CreateDefinitionAsync(HttpClient client, Guid household) =>
        client.PostId($"/api/households/{household}/medication-definitions", new
        {
            name = "Parol 500 mg",
            form = "Tablet",
            strength = "500 mg",
            defaultPackageCapacityNumerator = 20,
        });
}
