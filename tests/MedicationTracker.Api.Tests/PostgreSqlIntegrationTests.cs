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
        var personId = await PostAndReadId(client, $"/api/households/{householdId}/people", new { name = "Synthetic person" }, "id");
        var medicationId = await PostAndReadId(client, $"/api/households/{householdId}/medications", new { personId, name = "Synthetic tablet", form = "tablet", stockNumerator = 15, stockDenominator = 2 }, "medicationId");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var versionId = await PostAndReadId(client, $"/api/households/{householdId}/regimens", new { personId, medicationId, validFrom = today, validTo = (DateOnly?)null, doseNumerator = 1, doseDenominator = 2, localTime = new TimeOnly(9, 0), timeZoneId = "Europe/Istanbul" }, "regimenVersionId");
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
