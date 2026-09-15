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
    public async Task V1_crud_is_audited_and_administration_never_creates_negative_stock()
    {
        await using var factory = new PostgreSqlApiFactory(Environment.GetEnvironmentVariable("MEDICATION_TRACKER_TEST_POSTGRES")!);
        using (var migrationScope = factory.Services.CreateScope())
            await migrationScope.ServiceProvider.GetRequiredService<MedicationTrackerDbContext>().Database.MigrateAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Medication-Client", "1");
        var household = await PostAndReadId(client, "/api/auth/register", new { email = $"crud-{Guid.NewGuid():N}@example.invalid", password = "Synthetic-test-password", confirmPassword = "Synthetic-test-password" }, "householdId");
        var path = $"/api/households/{household}";
        var person = await PostAndReadId(client, $"{path}/people", new { name = "Synthetic CRUD person" }, "id");
        var medication = await PostAndReadId(client, $"{path}/medications", new { personId = (Guid?)null, name = "Synthetic old name", form = "tablet", stockNumerator = 1, stockDenominator = 1 }, "id");

        (await client.PutAsJsonAsync($"{path}/medications/{medication}", new { personId = person, name = "Synthetic updated tablet", form = "tablet", strength = "5 mg", activeIngredient = "Synthetic ingredient", notes = "Synthetic note", category = "Synthetic category", tags = new[] { "audit", "crud" }, isActive = true })).EnsureSuccessStatusCode();
        var regimenResponse = await client.PostAsJsonAsync($"{path}/regimens", new { personId = person, medicationId = medication, validFrom = (DateOnly?)null, validTo = (DateOnly?)null, doseNumerator = 1, doseDenominator = 1, localTime = (TimeOnly?)null, timeZoneId = "Europe/Istanbul", scheduleType = "as_needed", dayPeriod = (string?)null, mealRelation = "with_food", minimumIntervalMinutes = 60 });
        regimenResponse.EnsureSuccessStatusCode();
        var regimenJson = await regimenResponse.Content.ReadFromJsonAsync<JsonElement>();
        var regimen = regimenJson.GetProperty("regimenId").GetGuid();
        var originalVersion = regimenJson.GetProperty("regimenVersionId").GetGuid();
        var updateResponse = await client.PutAsJsonAsync($"{path}/regimens/{regimen}", new { personId = person, medicationId = medication, validFrom = (DateOnly?)null, validTo = (DateOnly?)null, doseNumerator = 1, doseDenominator = 2, localTime = (TimeOnly?)null, timeZoneId = "Europe/Istanbul", scheduleType = "as_needed", dayPeriod = "night", mealRelation = "fasting", minimumIntervalMinutes = 120 });
        updateResponse.EnsureSuccessStatusCode();
        var currentVersion = (await updateResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("regimenVersionId").GetGuid();
        Assert.NotEqual(originalVersion, currentVersion);

        foreach (var attempt in Enumerable.Range(0, 2))
        {
            var taken = await client.PostAsJsonAsync($"{path}/sync/administrations", new { idempotencyKey = $"taken-{attempt}-{Guid.NewGuid():N}", regimenVersionId = currentVersion, scheduledFor = (DateTimeOffset?)null, takenAt = DateTimeOffset.UtcNow.AddSeconds(attempt), outcome = "taken" });
            taken.EnsureSuccessStatusCode();
        }
        var rejected = await client.PostAsJsonAsync($"{path}/sync/administrations", new { idempotencyKey = $"rejected-{Guid.NewGuid():N}", regimenVersionId = currentVersion, scheduledFor = (DateTimeOffset?)null, takenAt = DateTimeOffset.UtcNow.AddMinutes(1), outcome = "taken" });
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Equal("insufficient_stock", (await rejected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        var workspace = await client.GetFromJsonAsync<JsonElement>($"{path}/workspace");
        var medicationRow = Assert.Single(workspace.GetProperty("medications").EnumerateArray());
        Assert.Equal("Synthetic updated tablet", medicationRow.GetProperty("name").GetString());
        Assert.Equal(person, medicationRow.GetProperty("personId").GetGuid());
        Assert.Equal(0, medicationRow.GetProperty("stockNumerator").GetInt64());
        var regimenRow = Assert.Single(workspace.GetProperty("regimens").EnumerateArray());
        Assert.Equal(currentVersion, regimenRow.GetProperty("versionId").GetGuid());
        Assert.Equal(1, regimenRow.GetProperty("doseNumerator").GetInt64());
        Assert.Equal(2, regimenRow.GetProperty("doseDenominator").GetInt64());
        var kinds = workspace.GetProperty("activities").EnumerateArray().Select(x => x.GetProperty("kind").GetString()).ToArray();
        Assert.Contains("medication_created", kinds); Assert.Contains("medication_updated", kinds); Assert.Contains("regimen_created", kinds); Assert.Contains("regimen_updated", kinds); Assert.Equal(2, kinds.Count(x => x == "administration_taken"));

        using var outsider = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        outsider.DefaultRequestHeaders.Add("X-Medication-Client", "1");
        await PostAndReadId(outsider, "/api/auth/register", new { email = $"crud-outsider-{Guid.NewGuid():N}@example.invalid", password = "Synthetic-test-password", confirmPassword = "Synthetic-test-password" }, "householdId");
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.DeleteAsync($"{path}/regimens/{regimen}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.PutAsJsonAsync($"{path}/medications/{medication}", new { personId = (Guid?)null, name = "Forbidden", form = "tablet", isActive = true })).StatusCode);

        (await client.DeleteAsync($"{path}/regimens/{regimen}")).EnsureSuccessStatusCode();
        var replacement = await client.PostAsJsonAsync($"{path}/regimens", new { personId = person, medicationId = medication, validFrom = (DateOnly?)null, validTo = (DateOnly?)null, doseNumerator = 1, doseDenominator = 2, localTime = (TimeOnly?)null, timeZoneId = "Europe/Istanbul", scheduleType = "as_needed", dayPeriod = (string?)null, mealRelation = (string?)null, minimumIntervalMinutes = (int?)null });
        replacement.EnsureSuccessStatusCode();
        (await client.DeleteAsync($"{path}/medications/{medication}")).EnsureSuccessStatusCode();
        workspace = await client.GetFromJsonAsync<JsonElement>($"{path}/workspace");
        Assert.Empty(workspace.GetProperty("medications").EnumerateArray());
        Assert.Empty(workspace.GetProperty("regimens").EnumerateArray());
        kinds = workspace.GetProperty("activities").EnumerateArray().Select(x => x.GetProperty("kind").GetString()).ToArray();
        Assert.Contains("medication_deleted", kinds); Assert.True(kinds.Count(x => x == "regimen_deleted") >= 2);

        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<MedicationTrackerDbContext>();
        Assert.Equal(2, await db.AdministrationEvents.CountAsync(x => x.HouseholdId == household));
        var stock = (await db.InventoryLedgerEntries.Where(x => x.HouseholdId == household).ToListAsync()).Aggregate(new ExactQuantity(0), (sum, entry) => sum + new ExactQuantity(entry.QuantityNumerator, entry.QuantityDenominator));
        Assert.Equal(new ExactQuantity(0), stock);
        Assert.Equal(2, await db.RegimenVersions.CountAsync(x => x.RegimenId == regimen));
    }

    [PostgreSqlFact]
    public async Task V1_catalog_packages_assignment_and_flexible_usage_are_household_scoped_and_exact()
    {
        await using var factory = new PostgreSqlApiFactory(Environment.GetEnvironmentVariable("MEDICATION_TRACKER_TEST_POSTGRES")!);
        using (var migrationScope = factory.Services.CreateScope())
        {
            await migrationScope.ServiceProvider.GetRequiredService<MedicationTrackerDbContext>().Database.MigrateAsync();
        }
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Medication-Client", "1");
        var household = await PostAndReadId(client, "/api/auth/register", new { email = $"v1-{Guid.NewGuid():N}@example.invalid", password = "Synthetic-test-password", confirmPassword = "Synthetic-test-password" }, "householdId");
        var path = $"/api/households/{household}";
        var person = await PostAndReadId(client, $"{path}/people", new { name = "Synthetic package person" }, "id");
        var medication = await PostAndReadId(client, $"{path}/medications", new
        {
            personId = (Guid?)null,
            name = "Synthetic catalog tablet",
            form = "tablet",
            stockNumerator = 0,
            stockDenominator = 1,
            strength = "10 mg",
            category = "Synthetic category",
            tags = new[] { "qa", "occasional" },
            isActive = true,
            packages = new[]
            {
                new { capacityNumerator = 20, capacityDenominator = 1, remainingNumerator = 20, remainingDenominator = 1, personId = (Guid?)null },
                new { capacityNumerator = 20, capacityDenominator = 1, remainingNumerator = 8, remainingDenominator = 1, personId = (Guid?)null }
            }
        }, "id");

        var workspace = await client.GetFromJsonAsync<JsonElement>($"{path}/workspace");
        var medicationRow = Assert.Single(workspace.GetProperty("medications").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, medicationRow.GetProperty("personId").ValueKind);
        Assert.Equal(28, medicationRow.GetProperty("stockNumerator").GetInt64());
        Assert.Equal("Synthetic category", medicationRow.GetProperty("category").GetString());
        Assert.Equal(2, medicationRow.GetProperty("packages").GetArrayLength());
        var packages = medicationRow.GetProperty("packages").EnumerateArray().ToArray();
        Assert.Equal(20, packages[0].GetProperty("remainingNumerator").GetInt64());
        Assert.Equal(8, packages[1].GetProperty("remainingNumerator").GetInt64());

        var partialPackage = packages[1].GetProperty("id").GetGuid();
        (await client.PostAsJsonAsync($"{path}/inventory/packages/{partialPackage}/assignment", new { idempotencyKey = Guid.NewGuid().ToString(), personId = person })).EnsureSuccessStatusCode();
        var regimen = await client.PostAsJsonAsync($"{path}/regimens", new { personId = person, medicationId = medication, validFrom = (DateOnly?)null, validTo = (DateOnly?)null, doseNumerator = 1, doseDenominator = 1, localTime = (TimeOnly?)null, timeZoneId = "Europe/Istanbul", scheduleType = "as_needed", dayPeriod = (string?)null, mealRelation = "fasting", minimumIntervalMinutes = 240 });
        regimen.EnsureSuccessStatusCode();
        var regimenVersion = (await regimen.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("regimenVersionId").GetGuid();
        var todayRows = await client.GetFromJsonAsync<JsonElement>($"{path}/today?date={DateOnly.FromDateTime(DateTime.UtcNow):yyyy-MM-dd}");
        var todayRow = Assert.Single(todayRows.EnumerateArray());
        Assert.Equal("available", todayRow.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, todayRow.GetProperty("scheduledFor").ValueKind);
        Assert.Equal("fasting", todayRow.GetProperty("mealRelation").GetString());

        (await client.PostAsJsonAsync($"{path}/sync/administrations", new { idempotencyKey = Guid.NewGuid().ToString(), regimenVersionId = regimenVersion, scheduledFor = (DateTimeOffset?)null, takenAt = DateTimeOffset.UtcNow, outcome = "taken" })).EnsureSuccessStatusCode();
        workspace = await client.GetFromJsonAsync<JsonElement>($"{path}/workspace");
        medicationRow = Assert.Single(workspace.GetProperty("medications").EnumerateArray());
        Assert.Equal(27, medicationRow.GetProperty("stockNumerator").GetInt64());
        packages = medicationRow.GetProperty("packages").EnumerateArray().ToArray();
        Assert.Equal(20, packages[0].GetProperty("remainingNumerator").GetInt64());
        Assert.Equal(7, packages[1].GetProperty("remainingNumerator").GetInt64());
        (await client.PostAsJsonAsync($"{path}/inventory/packages/{partialPackage}/assignment", new { idempotencyKey = Guid.NewGuid().ToString(), personId = (Guid?)null })).EnsureSuccessStatusCode();
        workspace = await client.GetFromJsonAsync<JsonElement>($"{path}/workspace");
        Assert.Equal(JsonValueKind.Null, workspace.GetProperty("medications")[0].GetProperty("packages")[1].GetProperty("personId").ValueKind);

        (await client.PostAsJsonAsync($"{path}/medications/{medication}/settings", new { category = "Synthetic category", tags = new[] { "qa" }, isActive = false })).EnsureSuccessStatusCode();
        todayRows = await client.GetFromJsonAsync<JsonElement>($"{path}/today?date={DateOnly.FromDateTime(DateTime.UtcNow):yyyy-MM-dd}");
        Assert.Empty(todayRows.EnumerateArray());
        (await client.PostAsJsonAsync($"{path}/medications/{medication}/settings", new { category = "Synthetic category", tags = new[] { "qa" }, isActive = true })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync($"{path}/regimens", new { personId = person, medicationId = medication, validFrom = (DateOnly?)null, validTo = (DateOnly?)null, doseNumerator = 1, doseDenominator = 2, localTime = (TimeOnly?)null, timeZoneId = "Europe/Istanbul", scheduleType = "scheduled", dayPeriod = "morning", mealRelation = "with_food", minimumIntervalMinutes = (int?)null })).EnsureSuccessStatusCode();
        todayRows = await client.GetFromJsonAsync<JsonElement>($"{path}/today?date={DateOnly.FromDateTime(DateTime.UtcNow):yyyy-MM-dd}");
        Assert.Equal(2, todayRows.GetArrayLength());
        var namedPeriod = todayRows.EnumerateArray().Single(x => x.GetProperty("scheduleType").GetString() == "scheduled");
        Assert.Equal("morning", namedPeriod.GetProperty("dayPeriod").GetString());
        Assert.Equal("due", namedPeriod.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, namedPeriod.GetProperty("scheduledFor").ValueKind);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<MedicationTrackerDbContext>();
        Assert.Equal(2, await db.InventoryPackageAssignmentEvents.CountAsync(x => x.HouseholdId == household));
        Assert.Equal(2, await db.MedicationChangeEvents.CountAsync(x => x.HouseholdId == household));

        using var outsider = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        outsider.DefaultRequestHeaders.Add("X-Medication-Client", "1");
        await PostAndReadId(outsider, "/api/auth/register", new { email = $"v1-outsider-{Guid.NewGuid():N}@example.invalid", password = "Synthetic-test-password", confirmPassword = "Synthetic-test-password" }, "householdId");
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.PostAsJsonAsync($"{path}/inventory/packages/{partialPackage}/assignment", new { idempotencyKey = Guid.NewGuid().ToString(), personId = (Guid?)null })).StatusCode);
    }

    [PostgreSqlFact]
    public async Task Existing_loose_stock_can_be_allocated_into_full_and_opened_packages_without_changing_total()
    {
        await using var factory = new PostgreSqlApiFactory(Environment.GetEnvironmentVariable("MEDICATION_TRACKER_TEST_POSTGRES")!);
        using (var migrationScope = factory.Services.CreateScope()) await migrationScope.ServiceProvider.GetRequiredService<MedicationTrackerDbContext>().Database.MigrateAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Medication-Client", "1");
        var household = await PostAndReadId(client, "/api/auth/register", new { email = $"allocation-{Guid.NewGuid():N}@example.invalid", password = "Synthetic-test-password", confirmPassword = "Synthetic-test-password" }, "householdId");
        var path = $"/api/households/{household}";
        var medication = await PostAndReadId(client, $"{path}/medications", new { personId = (Guid?)null, name = "Synthetic legacy tablet", form = "tablet", stockNumerator = 28, stockDenominator = 1 }, "id");
        foreach (var remaining in new[] { 20, 8 })
        {
            (await client.PostAsJsonAsync($"{path}/inventory/{medication}/packages", new { idempotencyKey = Guid.NewGuid().ToString(), capacityNumerator = 20, capacityDenominator = 1, remainingNumerator = remaining, remainingDenominator = 1, personId = (Guid?)null, fromExistingStock = true })).EnsureSuccessStatusCode();
        }
        var workspace = await client.GetFromJsonAsync<JsonElement>($"{path}/workspace");
        var row = Assert.Single(workspace.GetProperty("medications").EnumerateArray());
        Assert.Equal(28, row.GetProperty("stockNumerator").GetInt64());
        Assert.Equal(2, row.GetProperty("packages").GetArrayLength());
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<MedicationTrackerDbContext>();
        Assert.Equal(2, await db.InventoryLedgerEntries.CountAsync(x => x.HouseholdId == household && x.Reason == "package_allocation_in"));
        Assert.Equal(2, await db.InventoryLedgerEntries.CountAsync(x => x.HouseholdId == household && x.Reason == "package_allocation_out"));
    }

    [PostgreSqlFact]
    public async Task Medication_details_and_zero_stock_survive_reload_and_remain_household_scoped()
    {
        await using var factory = new PostgreSqlApiFactory(Environment.GetEnvironmentVariable("MEDICATION_TRACKER_TEST_POSTGRES")!);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicationTrackerDbContext>();
        await db.Database.MigrateAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Medication-Client", "1");
        var household = await PostAndReadId(client, "/api/auth/register", new { email = $"details-{Guid.NewGuid():N}@example.invalid", password = "Synthetic-test-password", confirmPassword = "Synthetic-test-password" }, "householdId");
        var path = $"/api/households/{household}";
        var person = await PostAndReadId(client, $"{path}/people", new { name = "Synthetic details person" }, "id");
        var request = new CreateMedicationRequest(person, "Synthetic tablet", "tablet", 0, 1, "10 mg", "Synthetic ingredient", "Synthetic package note");
        var medication = await PostAndReadId(client, $"{path}/medications", request, "id");
        var workspace = await client.GetFromJsonAsync<JsonElement>($"{path}/workspace");
        var row = Assert.Single(workspace.GetProperty("medications").EnumerateArray());
        Assert.Equal(medication, row.GetProperty("id").GetGuid());
        Assert.Equal("10 mg", row.GetProperty("strength").GetString());
        Assert.Equal("Synthetic ingredient", row.GetProperty("activeIngredient").GetString());
        Assert.Equal("Synthetic package note", row.GetProperty("notes").GetString());
        Assert.Equal(0, row.GetProperty("stockNumerator").GetInt64());
        Assert.Single(workspace.GetProperty("ledger").EnumerateArray());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"{path}/medications", request with { StockNumerator = -1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"{path}/medications", request with { Strength = new string('x', 101) })).StatusCode);
        using var outsider = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        outsider.DefaultRequestHeaders.Add("X-Medication-Client", "1");
        var otherHousehold = await PostAndReadId(outsider, "/api/auth/register", new { email = $"details-outsider-{Guid.NewGuid():N}@example.invalid", password = "Synthetic-test-password", confirmPassword = "Synthetic-test-password" }, "householdId");
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync($"{path}/workspace")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.PostAsJsonAsync($"{path}/medications", request)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.PostAsJsonAsync($"/api/households/{otherHousehold}/medications", request)).StatusCode);
    }

    [PostgreSqlFact]
    public async Task Registration_requires_confirmation_but_login_accepts_existing_short_passwords()
    {
        await using var factory = new PostgreSqlApiFactory(Environment.GetEnvironmentVariable("MEDICATION_TRACKER_TEST_POSTGRES")!);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicationTrackerDbContext>();
        await db.Database.MigrateAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Medication-Client", "1");
        var email = $"CONFIRM-{Guid.NewGuid():N}@EXAMPLE.INVALID".ToUpperInvariant();
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
