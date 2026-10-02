using System.Net;
using System.Net.Http.Json;
using MedicationTracker.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MedicationTracker.Api.Tests;

/// <summary>
/// Scheduling, registration, migration and health-check behaviour against PostgreSQL.
/// </summary>
/// <remarks>
/// The schedule coverage carries forward PR #9's weekday, interval and
/// daylight-saving-boundary cases onto the rebuilt treatment-plan model.
/// </remarks>
public sealed class PlatformApiTests
{
    [PostgreSqlFact]
    public async Task Selected_weekday_plans_are_only_due_on_the_chosen_local_days()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var definition = await CreateTabletAsync(client, household);
        var person = await client.PostId($"/api/households/{household}/people", new { name = "Synthetic person" });

        // Monday and Thursday, in a zone that changes offset during the period.
        await client.PostOk($"/api/households/{household}/plans", new
        {
            personId = person,
            medicationDefinitionId = definition,
            doseNumerator = 1,
            doseDenominator = 1,
            timeZoneId = "Europe/Berlin",
            pattern = "SelectedWeekdays",
            weekdayMask = (1 << 0) | (1 << 3),
            effectiveFrom = "2026-03-01",
            localTime = "08:00:00",
        });

        Assert.True(await IsDueAsync(client, household, new DateOnly(2026, 3, 23)));  // Monday
        Assert.False(await IsDueAsync(client, household, new DateOnly(2026, 3, 29))); // Sunday, DST change
        Assert.True(await IsDueAsync(client, household, new DateOnly(2026, 4, 2)));   // Thursday
    }

    [PostgreSqlFact]
    public async Task Interval_plans_are_anchored_to_their_effective_start_across_a_month_boundary()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var definition = await CreateTabletAsync(client, household);
        var person = await client.PostId($"/api/households/{household}/people", new { name = "Synthetic person" });

        await client.PostOk($"/api/households/{household}/plans", new
        {
            personId = person,
            medicationDefinitionId = definition,
            doseNumerator = 1,
            doseDenominator = 1,
            timeZoneId = "Europe/Berlin",
            pattern = "EveryNDays",
            intervalDays = 3,
            effectiveFrom = "2026-03-28",
            localTime = "08:00:00",
        });

        Assert.True(await IsDueAsync(client, household, new DateOnly(2026, 3, 28)));
        Assert.False(await IsDueAsync(client, household, new DateOnly(2026, 3, 29)));
        // Local calendar days, not 24-hour spans: the DST transition must not shift this.
        Assert.True(await IsDueAsync(client, household, new DateOnly(2026, 3, 31)));
        Assert.True(await IsDueAsync(client, household, new DateOnly(2026, 4, 3)));
    }

    [PostgreSqlFact]
    public async Task A_dose_cannot_be_recorded_against_a_day_the_plan_is_not_due_on()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var definition = await CreateTabletAsync(client, household);
        var person = await client.PostId($"/api/households/{household}/people", new { name = "Synthetic person" });

        await client.PostOk($"/api/households/{household}/inventory/{definition}/stock", new
        {
            capacityNumerator = 20,
            fullPackages = 1,
        });

        var plan = await client.PostOk($"/api/households/{household}/plans", new
        {
            personId = person,
            medicationDefinitionId = definition,
            doseNumerator = 1,
            doseDenominator = 1,
            timeZoneId = "UTC",
            pattern = "SelectedWeekdays",
            weekdayMask = 1 << 0,
            effectiveFrom = "2026-03-01",
            localTime = "08:00:00",
        });

        var planVersion = plan.GetProperty("versionId").GetGuid();

        // Tuesday, for a Monday-only plan.
        var offSchedule = await client.PostAsJsonAsync($"/api/households/{household}/administrations", new
        {
            planVersionId = planVersion,
            scheduledFor = new DateTimeOffset(2026, 3, 24, 8, 0, 0, TimeSpan.Zero),
        });

        Assert.Equal(HttpStatusCode.BadRequest, offSchedule.StatusCode);

        // A time that is not the plan's slot on a due day is also refused.
        var wrongTime = await client.PostAsJsonAsync($"/api/households/{household}/administrations", new
        {
            planVersionId = planVersion,
            scheduledFor = new DateTimeOffset(2026, 3, 23, 9, 0, 0, TimeSpan.Zero),
        });

        Assert.Equal(HttpStatusCode.BadRequest, wrongTime.StatusCode);

        var onSchedule = await client.PostAsJsonAsync($"/api/households/{household}/administrations", new
        {
            planVersionId = planVersion,
            scheduledFor = new DateTimeOffset(2026, 3, 23, 8, 0, 0, TimeSpan.Zero),
        });

        onSchedule.EnsureSuccessStatusCode();
    }

    [PostgreSqlFact]
    public async Task Editing_a_plan_appends_a_version_and_leaves_recorded_history_on_the_old_one()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();
        var definition = await CreateTabletAsync(client, household);
        var person = await client.PostId($"/api/households/{household}/people", new { name = "Synthetic person" });

        await client.PostOk($"/api/households/{household}/inventory/{definition}/stock", new
        {
            capacityNumerator = 20,
            fullPackages = 1,
        });

        var created = await client.PostOk($"/api/households/{household}/plans", new
        {
            personId = person,
            medicationDefinitionId = definition,
            doseNumerator = 1,
            doseDenominator = 2,
            timeZoneId = "UTC",
            localTime = "08:00:00",
            effectiveFrom = "2026-01-01",
        });

        var planId = created.GetProperty("id").GetGuid();
        var firstVersion = created.GetProperty("versionId").GetGuid();

        var dose = await client.PostOk($"/api/households/{household}/administrations", new
        {
            planVersionId = firstVersion,
            scheduledFor = new DateTimeOffset(2026, 2, 1, 8, 0, 0, TimeSpan.Zero),
        });

        var updated = await client.PutOk($"/api/households/{household}/plans/{planId}", new
        {
            personId = person,
            medicationDefinitionId = definition,
            doseNumerator = 1,
            doseDenominator = 1,
            timeZoneId = "UTC",
            localTime = "08:00:00",
            effectiveFrom = "2026-06-01",
        });

        Assert.Equal(2, updated.GetProperty("versionNumber").GetInt32());

        await using var db = harness.NewDbContext();

        // Two immutable versions; the historical dose still points at the first.
        Assert.Equal(2, await db.TreatmentPlanVersions.AsNoTracking()
            .CountAsync(version => version.TreatmentPlanId == planId));

        var recorded = await db.AdministrationEvents.AsNoTracking()
            .SingleAsync(e => e.Id == dose.GetProperty("administrationEventId").GetGuid());

        Assert.Equal(firstVersion, recorded.TreatmentPlanVersionId);
        Assert.Equal(new Domain.Quantities.ExactQuantity(1, 2), recorded.ActualQuantity);

        // The original version's dose was not rewritten by the edit.
        var original = await db.TreatmentPlanVersions.AsNoTracking()
            .SingleAsync(version => version.Id == firstVersion);
        Assert.Equal(new Domain.Quantities.ExactQuantity(1, 2), original.Dose);

        // An edit may not be back-dated behind the version it replaces.
        var backdated = await client.PutAsJsonAsync($"/api/households/{household}/plans/{planId}", new
        {
            personId = person,
            medicationDefinitionId = definition,
            doseNumerator = 2,
            doseDenominator = 1,
            timeZoneId = "UTC",
            localTime = "08:00:00",
            effectiveFrom = "2025-01-01",
        });

        Assert.Equal(HttpStatusCode.BadRequest, backdated.StatusCode);
    }

    [PostgreSqlFact]
    public async Task Registration_requires_a_confirmed_password_and_rejects_a_duplicate_account()
    {
        await using var harness = new ApiTestHarness();
        using var client = harness.NewClient();
        var email = $"synthetic-{Guid.CreateVersion7():N}@example.invalid";

        var mismatched = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            password = "synthetic-password-1",
            confirmPassword = "synthetic-password-2",
        });

        Assert.Equal(HttpStatusCode.BadRequest, mismatched.StatusCode);

        var created = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            password = "synthetic-password-1",
            confirmPassword = "synthetic-password-1",
        });

        created.EnsureSuccessStatusCode();

        using var second = harness.NewClient();
        var duplicate = await second.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            password = "synthetic-password-1",
            confirmPassword = "synthetic-password-1",
        });

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [PostgreSqlFact]
    public async Task Migrations_apply_cleanly_and_the_readiness_check_reports_the_database()
    {
        var connectionString = ApiTestHarness.ConnectionString!;
        await using var harness = new ApiTestHarness();

        await using (var db = harness.NewDbContext())
        {
            await db.Database.MigrateAsync();
        }

        using var client = harness.NewClient();
        var ready = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);

        var live = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);

        // The rebuild's own migration is recorded, and the renamed tables exist with
        // their replacements rather than alongside the superseded names.
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT count(*) FROM information_schema.tables
             WHERE (table_schema, table_name) IN (
                 ('catalog', 'medication_definitions'),
                 ('treatments', 'plans'),
                 ('treatments', 'plan_versions'),
                 ('administrations', 'allocations'),
                 ('administrations', 'allocation_corrections'),
                 ('refill', 'medication_refill_policies'));
            """;

        Assert.Equal(6L, (long)(await command.ExecuteScalarAsync())!);
    }

    [PostgreSqlFact]
    public async Task An_unauthenticated_request_is_rejected_before_any_household_data_is_read()
    {
        await using var harness = new ApiTestHarness();
        var (_, household) = await harness.NewHouseholdAsync();

        using var anonymous = harness.NewClient();

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync($"/api/households/{household}/workspace")).StatusCode);

        // Supplying the internal adapter header does not grant an identity.
        anonymous.DefaultRequestHeaders.Add("X-Account-Id", Guid.CreateVersion7().ToString());

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync($"/api/households/{household}/workspace")).StatusCode);
    }

    private static Task<Guid> CreateTabletAsync(HttpClient client, Guid household) =>
        client.PostId($"/api/households/{household}/medication-definitions", new
        {
            name = "Synthetic tablet",
            form = "Tablet",
            defaultPackageCapacityNumerator = 20,
        });

    private static async Task<bool> IsDueAsync(HttpClient client, Guid household, DateOnly day)
    {
        var today = await client.GetOk($"/api/households/{household}/today?date={day:yyyy-MM-dd}");
        return today.GetProperty("due").EnumerateArray().Any();
    }
}
