using System.Security.Claims;
using MedicationTracker.Api.Domain.Quantities;
using MedicationTracker.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedicationTracker.Api.Modules.Care;

public static class WorkspaceEndpoints
{
    public static void MapWorkspaceEndpoints(this WebApplication app)
    {
        app.MapPost("/api/households/{householdId:guid}/inventory/{medicationId:guid}/packages", async (Guid householdId, Guid medicationId, AddPackageRequest request, HttpContext context, MedicationTrackerDbContext db, CancellationToken ct) =>
        {
            if (!await IsMember(db, householdId, context, ct)) return Results.StatusCode(403);
            if (!TryPositive(request.CapacityNumerator, request.CapacityDenominator, out var capacity) || request.RemainingNumerator < 0 || request.RemainingNumerator > 1000000 || request.RemainingDenominator is < 1 or > 10000 || string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 100) return Results.BadRequest();
            var remaining = new ExactQuantity(request.RemainingNumerator, request.RemainingDenominator);
            if (Compare(remaining, capacity) > 0) return Results.BadRequest();
            if (request.PersonId is not null && !await db.People.AnyAsync(x => x.HouseholdId == householdId && x.Id == request.PersonId, ct)) return Results.NotFound();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({householdId.ToString()}, 0))", ct);
            var prior = await db.SyncCommandReceipts.SingleOrDefaultAsync(x => x.HouseholdId == householdId && x.IdempotencyKey == request.IdempotencyKey, ct);
            if (prior is not null) return Results.Ok(new { id = prior.ResultEntityId, replayed = true });
            var item = await db.InventoryItems.SingleOrDefaultAsync(x => x.HouseholdId == householdId && x.MedicationId == medicationId, ct);
            if (item is null) return Results.NotFound();
            var actor = Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!); var now = DateTimeOffset.UtcNow;
            var package = new InventoryPackage(Guid.NewGuid(), householdId, item.Id, request.PersonId, capacity.Numerator, capacity.Denominator, now);
            if (request.FromExistingStock)
            {
                var looseEntries = await db.InventoryLedgerEntries.Where(x => x.HouseholdId == householdId && x.InventoryItemId == item.Id && x.PackageId == null).ToListAsync(ct);
                var looseBalance = looseEntries.Aggregate(new ExactQuantity(0), (sum, entry) => sum + new ExactQuantity(entry.QuantityNumerator, entry.QuantityDenominator));
                if (Compare(looseBalance, remaining) < 0) return Results.Conflict();
                db.InventoryLedgerEntries.Add(new InventoryLedgerEntry(Guid.NewGuid(), householdId, item.Id, null, -remaining.Numerator, remaining.Denominator, "package_allocation_out", now, now));
            }
            db.InventoryPackages.Add(package); db.InventoryLedgerEntries.Add(new InventoryLedgerEntry(Guid.NewGuid(), householdId, item.Id, null, remaining.Numerator, remaining.Denominator, request.FromExistingStock ? "package_allocation_in" : "package_acquisition", now, now, package.Id));
            if (request.PersonId is not null) db.InventoryPackageAssignmentEvents.Add(new InventoryPackageAssignmentEvent(Guid.NewGuid(), householdId, package.Id, actor, null, request.PersonId, now));
            db.SyncCommandReceipts.Add(new SyncCommandReceipt(Guid.NewGuid(), householdId, actor, request.IdempotencyKey, "package.added", package.Id, now));
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            return Results.Ok(new { id = package.Id, replayed = false });
        });

        app.MapPost("/api/households/{householdId:guid}/inventory/packages/{packageId:guid}/assignment", async (Guid householdId, Guid packageId, AssignPackageRequest request, HttpContext context, MedicationTrackerDbContext db, CancellationToken ct) =>
        {
            if (!await IsMember(db, householdId, context, ct)) return Results.StatusCode(403);
            if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 100) return Results.BadRequest();
            if (request.PersonId is not null && !await db.People.AnyAsync(x => x.HouseholdId == householdId && x.Id == request.PersonId, ct)) return Results.NotFound();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({householdId.ToString()}, 0))", ct);
            var prior = await db.SyncCommandReceipts.SingleOrDefaultAsync(x => x.HouseholdId == householdId && x.IdempotencyKey == request.IdempotencyKey, ct);
            if (prior is not null) return Results.Ok(new { id = prior.ResultEntityId, replayed = true });
            var package = await db.InventoryPackages.SingleOrDefaultAsync(x => x.HouseholdId == householdId && x.Id == packageId, ct);
            if (package is null) return Results.NotFound();
            var actor = Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!); var now = DateTimeOffset.UtcNow; var previous = package.PersonId;
            package.AssignTo(request.PersonId); db.InventoryPackageAssignmentEvents.Add(new InventoryPackageAssignmentEvent(Guid.NewGuid(), householdId, package.Id, actor, previous, request.PersonId, now));
            db.SyncCommandReceipts.Add(new SyncCommandReceipt(Guid.NewGuid(), householdId, actor, request.IdempotencyKey, "package.assigned", package.Id, now));
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            return Results.Ok(new { id = package.Id, replayed = false });
        });

        app.MapPost("/api/households/{householdId:guid}/medications/{medicationId:guid}/settings", async (Guid householdId, Guid medicationId, MedicationSettingsRequest request, HttpContext context, MedicationTrackerDbContext db, CancellationToken ct) =>
        {
            if (!await IsMember(db, householdId, context, ct)) return Results.StatusCode(403);
            var tags = NormalizeTags(request.Tags); if (request.Category?.Length > 100 || tags is null) return Results.BadRequest();
            var medication = await db.Medications.SingleOrDefaultAsync(x => x.HouseholdId == householdId && x.Id == medicationId, ct); if (medication is null) return Results.NotFound();
            var actor = Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!); var now = DateTimeOffset.UtcNow;
            db.MedicationChangeEvents.Add(new MedicationChangeEvent(Guid.NewGuid(), householdId, medication.Id, actor, "metadata", System.Text.Json.JsonSerializer.Serialize(new { medication.Category, medication.Tags, medication.IsActive }), System.Text.Json.JsonSerializer.Serialize(new { request.Category, Tags = tags, request.IsActive }), now));
            medication.UpdateMetadata(request.Category, tags, request.IsActive); await db.SaveChangesAsync(ct);
            return Results.Ok(new { medication.Id });
        });

        app.MapPost("/api/households/{householdId:guid}/inventory/{medicationId:guid}", async (Guid householdId, Guid medicationId, InventoryRequest request, HttpContext context, MedicationTrackerDbContext db, CancellationToken ct) =>
        {
            if (!await IsMember(db, householdId, context, ct)) return Results.StatusCode(403);
            if (request.Kind is not ("refill" or "count") || request.Numerator < 0 || request.Numerator > 1000000 || request.Denominator is < 1 or > 10000 || (request.Kind == "refill" && request.Numerator == 0) || string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 100) return Results.BadRequest();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            // The same household lock is used for every stock mutation, including administrations.
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({householdId.ToString()}, 0))", ct);
            var prior = await db.SyncCommandReceipts.SingleOrDefaultAsync(x => x.HouseholdId == householdId && x.IdempotencyKey == request.IdempotencyKey, ct);
            if (prior is not null) return Results.Ok(new { id = prior.ResultEntityId, replayed = true });
            var item = await db.InventoryItems.SingleOrDefaultAsync(x => x.HouseholdId == householdId && x.MedicationId == medicationId, ct);
            if (item is null) return Results.NotFound();
            var entries = await db.InventoryLedgerEntries.Where(x => x.InventoryItemId == item.Id).ToListAsync(ct);
            var before = entries.Aggregate(new ExactQuantity(0), (sum, x) => sum + new ExactQuantity(x.QuantityNumerator, x.QuantityDenominator));
            var observed = new ExactQuantity(request.Numerator, request.Denominator);
            var delta = request.Kind == "count" ? observed - before : observed;
            var now = DateTimeOffset.UtcNow;
            var actor = Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var ledgerId = Guid.NewGuid();
            db.InventoryLedgerEntries.Add(new InventoryLedgerEntry(ledgerId, householdId, item.Id, null, delta.Numerator, delta.Denominator, request.Kind == "count" ? "count_reconciliation" : "refill", now, now));
            if (request.Kind == "count") db.Set<InventoryCount>().Add(new InventoryCount { Id = Guid.NewGuid(), HouseholdId = householdId, InventoryItemId = item.Id, AccountId = actor, BeforeNumerator = before.Numerator, BeforeDenominator = before.Denominator, ObservedNumerator = observed.Numerator, ObservedDenominator = observed.Denominator, LedgerEntryId = ledgerId, AcceptedAt = now });
            db.SyncCommandReceipts.Add(new SyncCommandReceipt(Guid.NewGuid(), householdId, actor, request.IdempotencyKey, request.Kind, ledgerId, now));
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            return Results.Ok(new { id = ledgerId, replayed = false });
        });
        app.MapGet("/api/households/{householdId:guid}/workspace", async (Guid householdId, HttpContext context, MedicationTrackerDbContext db, CancellationToken ct) =>
        {
            if (!await IsMember(db, householdId, context, ct)) return Results.StatusCode(403);
            await using var snapshot = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
            var people = await db.People.AsNoTracking().Where(x => x.HouseholdId == householdId).OrderBy(x => x.CreatedAt).ToListAsync(ct);
            var medications = await db.Medications.AsNoTracking().Where(x => x.HouseholdId == householdId).OrderBy(x => x.CreatedAt).ToListAsync(ct);
            var items = await db.InventoryItems.AsNoTracking().Where(x => x.HouseholdId == householdId).ToListAsync(ct);
            var packages = await db.InventoryPackages.AsNoTracking().Where(x => x.HouseholdId == householdId).OrderBy(x => x.CreatedAt).ToListAsync(ct);
            var ledger = await db.InventoryLedgerEntries.AsNoTracking().Where(x => x.HouseholdId == householdId).OrderByDescending(x => x.RecordedAt).ToListAsync(ct);
            var regimens = await (from r in db.Regimens.AsNoTracking() join v in db.RegimenVersions.AsNoTracking() on r.Id equals v.RegimenId where r.HouseholdId == householdId select new { r.Id, r.PersonId, r.MedicationId, versionId = v.Id, v.ValidFrom, v.ValidTo, v.LocalTime, v.TimeZoneId, v.DoseNumerator, v.DoseDenominator, v.ScheduleType, v.DayPeriod, v.MealRelation, v.MinimumIntervalMinutes }).ToListAsync(ct);
            var administrations = await db.AdministrationEvents.AsNoTracking().Where(x => x.HouseholdId == householdId).ToListAsync(ct);
            return Results.Ok(new { people, medications = medications.Select(m =>
            {
                var item = items.Single(x => x.MedicationId == m.Id); var stock = ledger.Where(x => x.InventoryItemId == item.Id).Aggregate(new ExactQuantity(0), (sum, x) => sum + new ExactQuantity(x.QuantityNumerator, x.QuantityDenominator));
                var packageRows = packages.Where(x => x.InventoryItemId == item.Id).Select((package, index) =>
                {
                    var balance = ledger.Where(x => x.PackageId == package.Id).Aggregate(new ExactQuantity(0), (sum, x) => sum + new ExactQuantity(x.QuantityNumerator, x.QuantityDenominator));
                    return new { package.Id, number = index + 1, package.PersonId, package.CapacityNumerator, package.CapacityDenominator, remainingNumerator = balance.Numerator, remainingDenominator = balance.Denominator };
                });
                return new { m.Id, m.PersonId, m.Name, m.Form, m.Strength, m.ActiveIngredient, m.Notes, m.Category, m.Tags, m.IsActive, inventoryItemId = item.Id, stockNumerator = stock.Numerator, stockDenominator = stock.Denominator, packages = packageRows };
            }), regimens, ledger, administrations });
        });
    }

    private static bool TryPositive(long numerator, long denominator, out ExactQuantity quantity)
    {
        quantity = default;
        if (numerator is <= 0 or > 1000000 || denominator is <= 0 or > 10000) return false;
        quantity = new ExactQuantity(numerator, denominator); return true;
    }
    private static int Compare(ExactQuantity left, ExactQuantity right) => checked(left.Numerator * right.Denominator).CompareTo(checked(right.Numerator * left.Denominator));
    private static string[]? NormalizeTags(string[]? tags)
    {
        var normalized = (tags ?? []).Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return normalized.Length <= 12 && normalized.All(x => x.Length <= 40) ? normalized : null;
    }
    private static Task<bool> IsMember(MedicationTrackerDbContext db, Guid householdId, HttpContext context, CancellationToken ct)
    {
        var id = Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var now = DateTimeOffset.UtcNow;
        return db.HouseholdMemberships.AnyAsync(x => x.HouseholdId == householdId && x.AccountId == id && x.ValidFrom <= now && (x.ValidTo == null || x.ValidTo > now), ct);
    }
}

public sealed record InventoryRequest(string IdempotencyKey, string Kind, long Numerator, long Denominator);
public sealed record AddPackageRequest(string IdempotencyKey, long CapacityNumerator, long CapacityDenominator, long RemainingNumerator, long RemainingDenominator, Guid? PersonId = null, bool FromExistingStock = false);
public sealed record AssignPackageRequest(string IdempotencyKey, Guid? PersonId);
public sealed record MedicationSettingsRequest(string? Category, string[]? Tags, bool IsActive);
