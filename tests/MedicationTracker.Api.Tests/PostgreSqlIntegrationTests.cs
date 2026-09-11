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
    public async Task Registration_requires_confirmation_but_login_accepts_existing_short_passwords()
    {
        await using var factory = new PostgreSqlApiFactory(Environment.GetEnvironmentVariable("MEDICATION_TRACKER_TEST_POSTGRES")!);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicationTrackerDbContext>();
        await db.Database.MigrateAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Medication-Client", "1");
        var email = $"CONFIRM-{Guid.NewGuid():N}@EXAMPLE.INVALID";
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/register", new { email, password = "Synthetic-long-password" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/register", new { email, password = "Synthetic-long-password", confirmPassword = "Another-long-password" })).StatusCode);
        Assert.False(await db.Accounts.AnyAsync(x => x.NormalizedEmail == email));
        var account = new Account(Guid.NewGuid(), email, DateTimeOffset.UtcNow);
        account.SetPasswordHash(new Microsoft.AspNetCore.Identity.PasswordHasher<Account>().HashPassword(account, "QaShort!"));
        db.Accounts.Add(account); await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new { email, password = "QaShort!" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Wrong123" })).StatusCode);
    }

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
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Medication-Client", "1");
        var householdId = await PostAndReadId(client, "/api/auth/register", new { email = $"OWNER-{Guid.NewGuid():N}@EXAMPLE.INVALID", password = "Synthetic-test-password-123", confirmPassword = "Synthetic-test-password-123" }, "householdId");
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

        var skipDay = today.AddDays(1);
        var skipRows = await client.GetFromJsonAsync<JsonElement>($"/api/households/{householdId}/today?date={skipDay:yyyy-MM-dd}");
        Assert.Equal("skipped", skipRows[0].GetProperty("status").GetString());
        var count = new { idempotencyKey = Guid.NewGuid().ToString(), kind = "count", numerator = 5, denominator = 2 };
        (await client.PostAsJsonAsync($"/api/households/{householdId}/inventory/{medicationId}", count)).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync($"/api/households/{householdId}/inventory/{medicationId}", count)).EnsureSuccessStatusCode();
        Assert.Equal(1, await db.Set<InventoryCount>().CountAsync(x => x.HouseholdId == householdId));
        var workspace = await client.GetFromJsonAsync<JsonElement>($"/api/households/{householdId}/workspace");
        Assert.Equal(5, workspace.GetProperty("medications")[0].GetProperty("stockNumerator").GetInt64());
        Assert.Equal(2, workspace.GetProperty("medications")[0].GetProperty("stockDenominator").GetInt64());
        var refill = new { idempotencyKey = Guid.NewGuid().ToString(), kind = "refill", numerator = 10, denominator = 1 };
        (await client.PostAsJsonAsync($"/api/households/{householdId}/inventory/{medicationId}", refill)).EnsureSuccessStatusCode();
        var afterRefill = await client.GetFromJsonAsync<JsonElement>($"/api/households/{householdId}/workspace");
        Assert.Equal(25, afterRefill.GetProperty("medications")[0].GetProperty("stockNumerator").GetInt64());

        using var anonymous = factory.CreateClient();
        anonymous.DefaultRequestHeaders.Add("X-Account-Id", Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/households/{householdId}/today")).StatusCode);
        using var outsider = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        outsider.DefaultRequestHeaders.Add("X-Medication-Client", "1");
        await PostAndReadId(outsider, "/api/auth/register", new { email = $"OUTSIDER-{Guid.NewGuid():N}@EXAMPLE.INVALID", password = "Synthetic-test-password-456", confirmPassword = "Synthetic-test-password-456" }, "accountId");
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync($"/api/households/{householdId}/today?date={today:yyyy-MM-dd}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync($"/api/households/{householdId}/workspace")).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Medication-Client");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/auth/logout", new { })).StatusCode);
        client.DefaultRequestHeaders.Add("X-Medication-Client", "1");
        (await client.PostAsJsonAsync("/api/auth/logout", new { })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/households/{householdId}/today")).StatusCode);
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
