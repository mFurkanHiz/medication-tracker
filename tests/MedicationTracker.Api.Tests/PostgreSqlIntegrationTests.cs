using System.Net;
using MedicationTracker.Api.Modules.Identity;
using MedicationTracker.Api.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
using System.Text.Json;
using MedicationTracker.Api.Modules.Care;
using MedicationTracker.Api.Domain.Quantities;

namespace MedicationTracker.Api.Tests;

public sealed class PostgreSqlIntegrationTests
{
    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task Migrations_health_check_and_transaction_rollback_work()
    {
        var connectionString = Environment.GetEnvironmentVariable(
            "MEDICATION_TRACKER_TEST_POSTGRES")!;

        await using var factory = new PostgreSqlApiFactory(connectionString);
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MedicationTrackerDbContext>();
        await dbContext.Database.MigrateAsync();

        using var client = factory.CreateClient();
        var response = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        var accountId = Guid.NewGuid();
        dbContext.Accounts.Add(new Account(
            accountId,
            $"INTEGRATION-{accountId:N}@EXAMPLE.INVALID",
            DateTimeOffset.UtcNow));
        await dbContext.SaveChangesAsync();
        Assert.True(await dbContext.Accounts.AnyAsync(account => account.Id == accountId));
        await transaction.RollbackAsync();
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task Offline_administration_replay_is_household_scoped_and_consumes_stock_once()
    {
        var connectionString = Environment.GetEnvironmentVariable("MEDICATION_TRACKER_TEST_POSTGRES")!;
        await using var factory = new PostgreSqlApiFactory(connectionString);
        using (var migrationScope = factory.Services.CreateScope())
        {
            await migrationScope.ServiceProvider.GetRequiredService<MedicationTrackerDbContext>().Database.MigrateAsync();
        }
        using var client = factory.CreateClient();
        var accountId = await PostAndReadId(client, "/api/accounts", new { email = $"OWNER-{Guid.NewGuid():N}@EXAMPLE.INVALID" }, "id");
        client.DefaultRequestHeaders.Add("X-Account-Id", accountId.ToString());
        var householdId = await PostAndReadId(client, "/api/households", new { name = "Synthetic household" }, "id");
        var personId = Guid.NewGuid(); var medicationId = Guid.NewGuid(); var inventoryItemId = Guid.NewGuid(); var regimenId = Guid.NewGuid(); var versionId = Guid.NewGuid();
        var personCommand = new { idempotencyKey = $"person:{personId}", kind = "person.created", payload = new { id = personId, name = "Synthetic person" } };
        (await client.PostAsJsonAsync($"/api/households/{householdId}/sync/commands", personCommand)).EnsureSuccessStatusCode();
        var personReplay = await client.PostAsJsonAsync($"/api/households/{householdId}/sync/commands", personCommand);
        Assert.True((await personReplay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("replayed").GetBoolean());
        (await client.PostAsJsonAsync($"/api/households/{householdId}/sync/commands", new { idempotencyKey = $"medication:{medicationId}", kind = "medication.created", payload = new { id = medicationId, personId, inventoryItemId, name = "Synthetic tablet", stockNumerator = 15, stockDenominator = 2 } })).EnsureSuccessStatusCode();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        (await client.PostAsJsonAsync($"/api/households/{householdId}/sync/commands", new { idempotencyKey = $"regimen:{regimenId}", kind = "regimen.created", payload = new { id = regimenId, versionId, personId, medicationId, validFrom = today, doseNumerator = 1, doseDenominator = 2, localTime = new TimeOnly(9, 0), timeZoneId = "Europe/Istanbul" } })).EnsureSuccessStatusCode();
        var scheduledFor = new DateTimeOffset(today.ToDateTime(new TimeOnly(9, 0)), TimeSpan.FromHours(3));
        var command = new { idempotencyKey = $"test-{Guid.NewGuid():N}", regimenVersionId = versionId, scheduledFor, takenAt = DateTimeOffset.UtcNow };
        var first = await client.PostAsJsonAsync($"/api/households/{householdId}/sync/administrations", command);
        var replay = await client.PostAsJsonAsync($"/api/households/{householdId}/sync/administrations", command);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode); Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.False((await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("replayed").GetBoolean());
        Assert.True((await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("replayed").GetBoolean());

        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<MedicationTrackerDbContext>();
        Assert.Equal(1, await db.AdministrationEvents.CountAsync(x => x.HouseholdId == householdId));
        Assert.Equal(1, await db.InventoryLedgerEntries.CountAsync(x => x.HouseholdId == householdId && x.Reason == "administration"));
        var entries = await db.InventoryLedgerEntries.Where(x => x.HouseholdId == householdId).ToListAsync();
        var balance = entries.Aggregate(new ExactQuantity(0), (sum, entry) => sum + new ExactQuantity(entry.QuantityNumerator, entry.QuantityDenominator));
        Assert.Equal(new ExactQuantity(7), balance);
        var forecast = await client.GetFromJsonAsync<JsonElement>($"/api/households/{householdId}/medications/{medicationId}/forecast?date={today:yyyy-MM-dd}");
        Assert.Equal(7, forecast.GetProperty("remainingNumerator").GetInt64()); Assert.Equal(14, forecast.GetProperty("fullDaysRemaining").GetInt64());

        var skipped = new { idempotencyKey = $"skip-{Guid.NewGuid():N}", regimenVersionId = versionId, scheduledFor = scheduledFor.AddDays(1), takenAt = DateTimeOffset.UtcNow, outcome = "skipped" };
        (await client.PostAsJsonAsync($"/api/households/{householdId}/sync/administrations", skipped)).EnsureSuccessStatusCode();
        Assert.Equal(2, await db.AdministrationEvents.CountAsync(x => x.HouseholdId == householdId));
        Assert.Equal(1, await db.InventoryLedgerEntries.CountAsync(x => x.HouseholdId == householdId && x.Reason == "administration"));

        using var anonymous = factory.CreateClient();
        var outsiderId = await PostAndReadId(anonymous, "/api/accounts", new { email = $"OUTSIDER-{Guid.NewGuid():N}@EXAMPLE.INVALID" }, "id");
        using var outsider = factory.CreateClient(); outsider.DefaultRequestHeaders.Add("X-Account-Id", outsiderId.ToString());
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync($"/api/households/{householdId}/today?date={today:yyyy-MM-dd}")).StatusCode);
    }

    private static async Task<Guid> PostAndReadId(HttpClient client, string path, object body, string property)
    {
        var response = await client.PostAsJsonAsync(path, body); response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty(property).GetGuid();
    }

    private sealed class PostgreSqlApiFactory(string connectionString)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Database"] = connectionString
                });
            });
        }
    }
}

public sealed class PostgreSqlFactAttribute : FactAttribute
{
    public PostgreSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable("MEDICATION_TRACKER_TEST_POSTGRES")))
        {
            Skip = "Set MEDICATION_TRACKER_TEST_POSTGRES to run PostgreSQL integration tests.";
        }
    }
}
