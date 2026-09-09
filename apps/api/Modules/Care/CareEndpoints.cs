using MedicationTracker.Api.Domain.Quantities;
using MedicationTracker.Api.Domain.Inventory;
using MedicationTracker.Api.Modules.Households;
using MedicationTracker.Api.Modules.Identity;
using MedicationTracker.Api.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace MedicationTracker.Api.Modules.Care;

public static class CareEndpoints
{
    public static IEndpointRouteBuilder MapCareEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api");

        api.MapPost("/accounts", async (CreateAccountRequest request, MedicationTrackerDbContext db, CancellationToken ct) =>
        {
            var email = request.Email.Trim().ToUpperInvariant();
            if (email.Length is 0 or > 320) return Results.ValidationProblem(Error("email", "invalid_email"));
            var account = new Account(Guid.NewGuid(), email, DateTimeOffset.UtcNow);
            db.Accounts.Add(account); await db.SaveChangesAsync(ct);
            return Results.Created($"/api/accounts/{account.Id}", new { account.Id });
        });

        api.MapPost("/households", async (CreateHouseholdRequest request, [FromHeader(Name = "X-Account-Id")] Guid? accountId, MedicationTrackerDbContext db, CancellationToken ct) =>
        {
            if (accountId is null || !await db.Accounts.AnyAsync(x => x.Id == accountId, ct)) return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(request.Name)) return Results.ValidationProblem(Error("name", "required"));
            var now = DateTimeOffset.UtcNow; var household = new Household(Guid.NewGuid(), request.Name.Trim(), now);
            db.Households.Add(household); db.HouseholdMemberships.Add(new HouseholdMembership(Guid.NewGuid(), household.Id, accountId.Value, "owner", now));
            await db.SaveChangesAsync(ct); return Results.Created($"/api/households/{household.Id}", new { household.Id });
        });

        api.MapPost("/households/{householdId:guid}/people", async (Guid householdId, CreatePersonRequest request, [FromHeader(Name = "X-Account-Id")] Guid? accountId, MedicationTrackerDbContext db, CancellationToken ct) =>
        {
            if (!await IsMember(db, householdId, accountId, ct)) return Results.StatusCode(StatusCodes.Status403Forbidden);
            if (string.IsNullOrWhiteSpace(request.Name)) return Results.ValidationProblem(Error("name", "required"));
            var person = new Person(Guid.NewGuid(), householdId, request.Name.Trim(), DateTimeOffset.UtcNow);
            db.People.Add(person); await db.SaveChangesAsync(ct); return Results.Created($"/api/households/{householdId}/people/{person.Id}", new { person.Id });
        });

        api.MapPost("/households/{householdId:guid}/medications", async (Guid householdId, CreateMedicationRequest request, [FromHeader(Name = "X-Account-Id")] Guid? accountId, MedicationTrackerDbContext db, CancellationToken ct) =>
        {
            if (!await IsMember(db, householdId, accountId, ct)) return Results.StatusCode(StatusCodes.Status403Forbidden);
            if (string.IsNullOrWhiteSpace(request.Name) || request.Form != "tablet") return Results.ValidationProblem(Error("medication", "tablet_required"));
            if (!TryPositive(request.StockNumerator, request.StockDenominator, out var stock)) return Results.ValidationProblem(Error("stock", "positive_exact_quantity_required"));
            if (!await db.People.AnyAsync(x => x.Id == request.PersonId && x.HouseholdId == householdId, ct)) return Results.NotFound();
            var now = DateTimeOffset.UtcNow; var medication = new Medication(Guid.NewGuid(), householdId, request.PersonId, request.Name.Trim(), request.Form, now); var item = new InventoryItem(Guid.NewGuid(), householdId, medication.Id, now);
            db.Medications.Add(medication); db.InventoryItems.Add(item); db.InventoryLedgerEntries.Add(new InventoryLedgerEntry(Guid.NewGuid(), householdId, item.Id, null, stock.Numerator, stock.Denominator, "acquisition", now, now));
            await db.SaveChangesAsync(ct); return Results.Created($"/api/households/{householdId}/medications/{medication.Id}", new { medication.Id, inventoryItemId = item.Id, stock.Numerator, stock.Denominator });
        });

        api.MapPost("/households/{householdId:guid}/regimens", async (Guid householdId, CreateRegimenRequest request, [FromHeader(Name = "X-Account-Id")] Guid? accountId, MedicationTrackerDbContext db, CancellationToken ct) =>
        {
            if (!await IsMember(db, householdId, accountId, ct)) return Results.StatusCode(StatusCodes.Status403Forbidden);
            if (!TryPositive(request.DoseNumerator, request.DoseDenominator, out var dose)) return Results.ValidationProblem(Error("dose", "positive_exact_quantity_required"));
            if (request.ValidTo < request.ValidFrom) return Results.ValidationProblem(Error("validTo", "invalid_period"));
            try { _ = TimeZoneInfo.FindSystemTimeZoneById(request.TimeZoneId); } catch (TimeZoneNotFoundException) { return Results.ValidationProblem(Error("timeZoneId", "unknown_time_zone")); }
            var medication = await db.Medications.SingleOrDefaultAsync(x => x.Id == request.MedicationId && x.HouseholdId == householdId && x.PersonId == request.PersonId, ct);
            if (medication is null) return Results.NotFound();
            var now = DateTimeOffset.UtcNow; var regimen = new Regimen(Guid.NewGuid(), householdId, request.PersonId, request.MedicationId, now); var version = new RegimenVersion(Guid.NewGuid(), regimen.Id, request.ValidFrom, request.ValidTo, dose.Numerator, dose.Denominator, request.LocalTime, request.TimeZoneId, now);
            db.Regimens.Add(regimen); db.RegimenVersions.Add(version); await db.SaveChangesAsync(ct);
            return Results.Created($"/api/households/{householdId}/regimens/{regimen.Id}", new { regimen.Id, regimenVersionId = version.Id });
        });

        api.MapGet("/households/{householdId:guid}/today", async (Guid householdId, DateOnly? date, [FromHeader(Name = "X-Account-Id")] Guid? accountId, MedicationTrackerDbContext db, CancellationToken ct) =>
        {
            if (!await IsMember(db, householdId, accountId, ct)) return Results.StatusCode(StatusCodes.Status403Forbidden);
            var day = date ?? DateOnly.FromDateTime(DateTime.UtcNow);
            var rows = await (from version in db.RegimenVersions
                              join regimen in db.Regimens on version.RegimenId equals regimen.Id
                              join person in db.People on regimen.PersonId equals person.Id
                              join medication in db.Medications on regimen.MedicationId equals medication.Id
                              where regimen.HouseholdId == householdId && version.ValidFrom <= day && (version.ValidTo == null || version.ValidTo >= day)
                              select new { version, regimen, person, medication }).ToListAsync(ct);
            var versionIds = rows.Select(x => x.version.Id).ToArray();
            var taken = await db.AdministrationEvents.Where(x => x.HouseholdId == householdId && versionIds.Contains(x.RegimenVersionId)).Select(x => new { x.RegimenVersionId, x.ScheduledFor }).ToListAsync(ct);
            return Results.Ok(rows.Select(x => { var scheduledFor = ScheduledInstant(day, x.version.LocalTime, x.version.TimeZoneId); return new { regimenVersionId = x.version.Id, personId = x.person.Id, personName = x.person.Name, medicationId = x.medication.Id, medicationName = x.medication.Name, doseNumerator = x.version.DoseNumerator, doseDenominator = x.version.DoseDenominator, scheduledFor, status = taken.Any(a => a.RegimenVersionId == x.version.Id && a.ScheduledFor == scheduledFor) ? "taken" : "due" }; }));
        });

        api.MapGet("/households/{householdId:guid}/medications/{medicationId:guid}/forecast", async (Guid householdId, Guid medicationId, DateOnly? date, [FromHeader(Name = "X-Account-Id")] Guid? accountId, MedicationTrackerDbContext db, CancellationToken ct) =>
        {
            if (!await IsMember(db, householdId, accountId, ct)) return Results.StatusCode(StatusCodes.Status403Forbidden);
            var item = await db.InventoryItems.AsNoTracking().SingleOrDefaultAsync(x => x.HouseholdId == householdId && x.MedicationId == medicationId, ct);
            if (item is null) return Results.NotFound();
            var entries = await db.InventoryLedgerEntries.AsNoTracking().Where(x => x.HouseholdId == householdId && x.InventoryItemId == item.Id).ToListAsync(ct);
            var balance = entries.Aggregate(new ExactQuantity(0), (sum, entry) => sum + new ExactQuantity(entry.QuantityNumerator, entry.QuantityDenominator));
            var day = date ?? DateOnly.FromDateTime(DateTime.UtcNow);
            var doses = await (from version in db.RegimenVersions.AsNoTracking()
                               join regimen in db.Regimens.AsNoTracking() on version.RegimenId equals regimen.Id
                               where regimen.HouseholdId == householdId && regimen.MedicationId == medicationId && version.ValidFrom <= day && (version.ValidTo == null || version.ValidTo >= day)
                               select new { version.DoseNumerator, version.DoseDenominator }).ToListAsync(ct);
            var daily = doses.Aggregate(new ExactQuantity(0), (sum, dose) => sum + new ExactQuantity(dose.DoseNumerator, dose.DoseDenominator));
            var depletionDate = StockProjection.DepletionDate(day, balance, daily);
            return Results.Ok(new { remainingNumerator = balance.Numerator, remainingDenominator = balance.Denominator, dailyNumerator = daily.Numerator, dailyDenominator = daily.Denominator, fullDaysRemaining = StockProjection.FullDaysRemaining(balance, daily), depletionDate });
        });

        api.MapPost("/households/{householdId:guid}/sync/commands", async (Guid householdId, SyncOfflineCommandRequest request, [FromHeader(Name = "X-Account-Id")] Guid? accountId, MedicationTrackerDbContext db, CancellationToken ct) =>
        {
            if (!await IsMember(db, householdId, accountId, ct)) return Results.StatusCode(StatusCodes.Status403Forbidden);
            if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 100) return Results.ValidationProblem(Error("idempotencyKey", "required"));
            var prior = await db.SyncCommandReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.HouseholdId == householdId && x.IdempotencyKey == request.IdempotencyKey, ct);
            if (prior is not null) return Results.Ok(new { resultEntityId = prior.ResultEntityId, replayed = true });

            Guid resultEntityId;
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            switch (request.Kind)
            {
                case "person.created":
                    var person = request.Payload.Deserialize<PersonCreatedPayload>(JsonSerializerOptions.Web);
                    if (person is null || person.Id == Guid.Empty || string.IsNullOrWhiteSpace(person.Name)) return Results.ValidationProblem(Error("payload", "invalid_person"));
                    db.People.Add(new Person(person.Id, householdId, person.Name.Trim(), DateTimeOffset.UtcNow)); resultEntityId = person.Id;
                    break;
                case "medication.created":
                    var medication = request.Payload.Deserialize<MedicationCreatedPayload>(JsonSerializerOptions.Web);
                    if (medication is null || medication.Id == Guid.Empty || medication.InventoryItemId == Guid.Empty || !TryPositive(medication.StockNumerator, medication.StockDenominator, out var stock)) return Results.ValidationProblem(Error("payload", "invalid_medication"));
                    if (!await db.People.AnyAsync(x => x.Id == medication.PersonId && x.HouseholdId == householdId, ct)) return Results.NotFound();
                    var now = DateTimeOffset.UtcNow;
                    db.Medications.Add(new Medication(medication.Id, householdId, medication.PersonId, medication.Name.Trim(), "tablet", now));
                    db.InventoryItems.Add(new InventoryItem(medication.InventoryItemId, householdId, medication.Id, now));
                    db.InventoryLedgerEntries.Add(new InventoryLedgerEntry(Guid.NewGuid(), householdId, medication.InventoryItemId, null, stock.Numerator, stock.Denominator, "acquisition", now, now)); resultEntityId = medication.Id;
                    break;
                case "regimen.created":
                    var regimenPayload = request.Payload.Deserialize<RegimenCreatedPayload>(JsonSerializerOptions.Web);
                    if (regimenPayload is null || regimenPayload.Id == Guid.Empty || regimenPayload.VersionId == Guid.Empty || !TryPositive(regimenPayload.DoseNumerator, regimenPayload.DoseDenominator, out var dose)) return Results.ValidationProblem(Error("payload", "invalid_regimen"));
                    try { _ = TimeZoneInfo.FindSystemTimeZoneById(regimenPayload.TimeZoneId); } catch (TimeZoneNotFoundException) { return Results.ValidationProblem(Error("payload", "unknown_time_zone")); }
                    if (!await db.Medications.AnyAsync(x => x.Id == regimenPayload.MedicationId && x.PersonId == regimenPayload.PersonId && x.HouseholdId == householdId, ct)) return Results.NotFound();
                    var createdAt = DateTimeOffset.UtcNow;
                    db.Regimens.Add(new Regimen(regimenPayload.Id, householdId, regimenPayload.PersonId, regimenPayload.MedicationId, createdAt));
                    db.RegimenVersions.Add(new RegimenVersion(regimenPayload.VersionId, regimenPayload.Id, regimenPayload.ValidFrom, null, dose.Numerator, dose.Denominator, regimenPayload.LocalTime, regimenPayload.TimeZoneId, createdAt)); resultEntityId = regimenPayload.Id;
                    break;
                default:
                    return Results.ValidationProblem(Error("kind", "unsupported_command"));
            }
            db.SyncCommandReceipts.Add(new SyncCommandReceipt(Guid.NewGuid(), householdId, accountId!.Value, request.IdempotencyKey, request.Kind, resultEntityId, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            return Results.Ok(new { resultEntityId, replayed = false });
        });

        api.MapPost("/households/{householdId:guid}/sync/administrations", async (Guid householdId, RecordAdministrationRequest request, [FromHeader(Name = "X-Account-Id")] Guid? accountId, MedicationTrackerDbContext db, CancellationToken ct) =>
        {
            if (!await IsMember(db, householdId, accountId, ct)) return Results.StatusCode(StatusCodes.Status403Forbidden);
            if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 100) return Results.ValidationProblem(Error("idempotencyKey", "required"));
            var prior = await db.ProcessedAdministrationCommands.AsNoTracking().SingleOrDefaultAsync(x => x.HouseholdId == householdId && x.IdempotencyKey == request.IdempotencyKey, ct);
            if (prior is not null) return Results.Ok(new { administrationEventId = prior.AdministrationEventId, replayed = true });
            var row = await (from version in db.RegimenVersions join regimen in db.Regimens on version.RegimenId equals regimen.Id where version.Id == request.RegimenVersionId && regimen.HouseholdId == householdId select new { version, regimen }).SingleOrDefaultAsync(ct);
            if (row is null) return Results.NotFound();
            var item = await db.InventoryItems.SingleAsync(x => x.HouseholdId == householdId && x.MedicationId == row.regimen.MedicationId, ct);
            var administrationId = request.AdministrationId ?? Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            if (request.Outcome is not ("taken" or "skipped")) return Results.ValidationProblem(Error("outcome", "unsupported_outcome"));
            var scheduledForUtc = request.ScheduledFor.ToUniversalTime();
            var occurredAtUtc = request.TakenAt.ToUniversalTime();
            db.AdministrationEvents.Add(new AdministrationEvent(administrationId, householdId, row.regimen.PersonId, row.regimen.MedicationId, row.version.Id, request.Outcome, scheduledForUtc, occurredAtUtc, now));
            if (request.Outcome == "taken") db.InventoryLedgerEntries.Add(new InventoryLedgerEntry(Guid.NewGuid(), householdId, item.Id, administrationId, -row.version.DoseNumerator, row.version.DoseDenominator, "administration", occurredAtUtc, now));
            db.ProcessedAdministrationCommands.Add(new ProcessedAdministrationCommand(Guid.NewGuid(), householdId, accountId!.Value, request.IdempotencyKey, administrationId, now));
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            return Results.Ok(new { administrationEventId = administrationId, replayed = false });
        });

        return endpoints;
    }

    private static async Task<bool> IsMember(MedicationTrackerDbContext db, Guid householdId, Guid? accountId, CancellationToken ct) =>
        accountId is not null && await db.HouseholdMemberships.AnyAsync(x => x.HouseholdId == householdId && x.AccountId == accountId && x.ValidFrom <= DateTimeOffset.UtcNow && (x.ValidTo == null || x.ValidTo > DateTimeOffset.UtcNow), ct);
    private static bool TryPositive(long numerator, long denominator, out ExactQuantity quantity)
    {
        quantity = default;
        if (numerator <= 0 || denominator <= 0) return false;
        quantity = new ExactQuantity(numerator, denominator); return true;
    }
    private static Dictionary<string, string[]> Error(string key, string value) => new() { [key] = [value] };
    private static DateTimeOffset ScheduledInstant(DateOnly date, TimeOnly time, string timeZoneId)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified); var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        if (zone.IsInvalidTime(local)) local = local.AddHours(1);
        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }
}

public sealed record CreateAccountRequest(string Email);
public sealed record CreateHouseholdRequest(string Name);
public sealed record CreatePersonRequest(string Name);
public sealed record CreateMedicationRequest(Guid PersonId, string Name, string Form, long StockNumerator, long StockDenominator);
public sealed record CreateRegimenRequest(Guid PersonId, Guid MedicationId, DateOnly ValidFrom, DateOnly? ValidTo, long DoseNumerator, long DoseDenominator, TimeOnly LocalTime, string TimeZoneId);
public sealed record RecordAdministrationRequest(string IdempotencyKey, Guid RegimenVersionId, DateTimeOffset ScheduledFor, DateTimeOffset TakenAt, string Outcome = "taken", Guid? AdministrationId = null);
public sealed record SyncOfflineCommandRequest(string IdempotencyKey, string Kind, JsonElement Payload);
public sealed record PersonCreatedPayload(Guid Id, string Name);
public sealed record MedicationCreatedPayload(Guid Id, Guid PersonId, Guid InventoryItemId, string Name, long StockNumerator, long StockDenominator);
public sealed record RegimenCreatedPayload(Guid Id, Guid VersionId, Guid PersonId, Guid MedicationId, DateOnly ValidFrom, long DoseNumerator, long DoseDenominator, TimeOnly LocalTime, string TimeZoneId);
