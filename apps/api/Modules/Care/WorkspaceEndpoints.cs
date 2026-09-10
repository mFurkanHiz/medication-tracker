using System.Security.Claims;
using MedicationTracker.Api.Domain.Quantities;
using MedicationTracker.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedicationTracker.Api.Modules.Care;

public static class WorkspaceEndpoints
{
    public static void MapWorkspaceEndpoints(this WebApplication app)
    {
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
            var ledger = await db.InventoryLedgerEntries.AsNoTracking().Where(x => x.HouseholdId == householdId).OrderByDescending(x => x.RecordedAt).ToListAsync(ct);
            var regimens = await (from r in db.Regimens.AsNoTracking() join v in db.RegimenVersions.AsNoTracking() on r.Id equals v.RegimenId where r.HouseholdId == householdId select new { r.Id, r.PersonId, r.MedicationId, versionId = v.Id, v.ValidFrom, v.ValidTo, v.LocalTime, v.TimeZoneId, v.DoseNumerator, v.DoseDenominator }).ToListAsync(ct);
            var administrations = await db.AdministrationEvents.AsNoTracking().Where(x => x.HouseholdId == householdId).ToListAsync(ct);
            return Results.Ok(new { people, medications = medications.Select(m => { var item = items.Single(x => x.MedicationId == m.Id); var stock = ledger.Where(x => x.InventoryItemId == item.Id).Aggregate(new ExactQuantity(0), (sum, x) => sum + new ExactQuantity(x.QuantityNumerator, x.QuantityDenominator)); return new { m.Id, m.PersonId, m.Name, m.Form, inventoryItemId = item.Id, stockNumerator = stock.Numerator, stockDenominator = stock.Denominator }; }), regimens, ledger, administrations });
        });
    }

    private static Task<bool> IsMember(MedicationTrackerDbContext db, Guid householdId, HttpContext context, CancellationToken ct)
    {
        var id = Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var now = DateTimeOffset.UtcNow;
        return db.HouseholdMemberships.AnyAsync(x => x.HouseholdId == householdId && x.AccountId == id && x.ValidFrom <= now && (x.ValidTo == null || x.ValidTo > now), ct);
    }
}

public sealed record InventoryRequest(string IdempotencyKey, string Kind, long Numerator, long Denominator);
