using System.Security.Claims;
using System.Text.Json;
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
            var item = await (from inventory in db.InventoryItems
                              join medication in db.Medications on inventory.MedicationId equals medication.Id
                              where inventory.HouseholdId == householdId && inventory.MedicationId == medicationId && medication.DeletedAt == null
                              select inventory).SingleOrDefaultAsync(ct);
            if (item is null) return Results.NotFound();
            var actor = Actor(context); var now = DateTimeOffset.UtcNow;
            var package = new InventoryPackage(Guid.NewGuid(), householdId, item.Id, request.PersonId, capacity.Numerator, capacity.Denominator, now);
            if (request.FromExistingStock)
            {
                var looseEntries = await db.InventoryLedgerEntries.Where(x => x.HouseholdId == householdId && x.InventoryItemId == item.Id && x.PackageId == null).ToListAsync(ct);
                var looseBalance = looseEntries.Aggregate(new ExactQuantity(0), (sum, entry) => sum + new ExactQuantity(entry.QuantityNumerator, entry.QuantityDenominator));
                if (Compare(looseBalance, remaining) < 0) return Results.Conflict(new { code = "insufficient_stock" });
                db.InventoryLedgerEntries.Add(new InventoryLedgerEntry(Guid.NewGuid(), householdId, item.Id, null, -remaining.Numerator, remaining.Denominator, "package_allocation_out", now, now));
            }
            db.InventoryPackages.Add(package);
            db.InventoryLedgerEntries.Add(new InventoryLedgerEntry(Guid.NewGuid(), householdId, item.Id, null, remaining.Numerator, remaining.Denominator, request.FromExistingStock ? "package_allocation_in" : "package_acquisition", now, now, package.Id));
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
            if (await db.InventoryLoans.AnyAsync(x => x.HouseholdId == householdId && x.PackageId == packageId && x.ReturnedAt == null, ct)) return Results.Conflict(new { code = "package_on_loan" });
            var actor = Actor(context); var now = DateTimeOffset.UtcNow; var previous = package.PersonId;
            package.AssignOwner(request.PersonId); db.InventoryPackageAssignmentEvents.Add(new InventoryPackageAssignmentEvent(Guid.NewGuid(), householdId, package.Id, actor, previous, request.PersonId, now));
            db.SyncCommandReceipts.Add(new SyncCommandReceipt(Guid.NewGuid(), householdId, actor, request.IdempotencyKey, "package.assigned", package.Id, now));
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            return Results.Ok(new { id = package.Id, replayed = false });
        });

        app.MapPost("/api/households/{householdId:guid}/inventory/packages/{packageId:guid}/loans", async (Guid householdId, Guid packageId, CreatePackageLoanRequest request, HttpContext context, MedicationTrackerDbContext db, CancellationToken ct) =>
        {
            if (!await IsMember(db, householdId, context, ct)) return Results.StatusCode(403);
            if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 100) return Results.BadRequest();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({householdId.ToString()}, 0))", ct);
            var prior = await db.SyncCommandReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.HouseholdId == householdId && x.IdempotencyKey == request.IdempotencyKey, ct);
            if (prior is not null) return Results.Ok(new { id = prior.ResultEntityId, replayed = true });
            var package = await db.InventoryPackages.SingleOrDefaultAsync(x => x.HouseholdId == householdId && x.Id == packageId, ct);
            if (package is null || package.OwnerPersonId is null) return Results.NotFound();
            var medicationAvailable = await (from item in db.InventoryItems
                                             join medication in db.Medications on item.MedicationId equals medication.Id
                                             where item.Id == package.InventoryItemId && item.HouseholdId == householdId && medication.DeletedAt == null
                                             select medication.Id).AnyAsync(ct);
            if (!medicationAvailable) return Results.NotFound();
            if (package.PersonId != package.OwnerPersonId || request.BorrowerPersonId == package.OwnerPersonId || await db.InventoryLoans.AnyAsync(x => x.HouseholdId == householdId && x.PackageId == packageId && x.ReturnedAt == null, ct)) return Results.Conflict(new { code = "package_not_lendable" });
            if (!await db.People.AnyAsync(x => x.HouseholdId == householdId && x.Id == request.BorrowerPersonId, ct)) return Results.NotFound();
            var packageEntries = await db.InventoryLedgerEntries.AsNoTracking().Where(x => x.HouseholdId == householdId && x.PackageId == packageId).ToListAsync(ct);
            var balance = packageEntries.Aggregate(new ExactQuantity(0), (sum, entry) => sum + new ExactQuantity(entry.QuantityNumerator, entry.QuantityDenominator));
            if (balance.Numerator <= 0) return Results.Conflict(new { code = "empty_package" });
            var actor = Actor(context); var now = DateTimeOffset.UtcNow;
            var loan = new InventoryLoan(Guid.NewGuid(), householdId, package.Id, package.OwnerPersonId.Value, request.BorrowerPersonId, actor, now);
            package.AssignTo(request.BorrowerPersonId);
            db.InventoryLoans.Add(loan);
            db.InventoryPackageAssignmentEvents.Add(new InventoryPackageAssignmentEvent(Guid.NewGuid(), householdId, package.Id, actor, loan.OwnerPersonId, loan.BorrowerPersonId, now));
            db.SyncCommandReceipts.Add(new SyncCommandReceipt(Guid.NewGuid(), householdId, actor, request.IdempotencyKey, "package.lent", loan.Id, now));
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            return Results.Ok(new { id = loan.Id, replayed = false });
        });

        app.MapPost("/api/households/{householdId:guid}/inventory/loans/{loanId:guid}/return", async (Guid householdId, Guid loanId, ReturnPackageLoanRequest request, HttpContext context, MedicationTrackerDbContext db, CancellationToken ct) =>
        {
            if (!await IsMember(db, householdId, context, ct)) return Results.StatusCode(403);
            if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 100) return Results.BadRequest();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({householdId.ToString()}, 0))", ct);
            var prior = await db.SyncCommandReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.HouseholdId == householdId && x.IdempotencyKey == request.IdempotencyKey, ct);
            if (prior is not null) return Results.Ok(new { id = prior.ResultEntityId, replayed = true });
            var loan = await db.InventoryLoans.SingleOrDefaultAsync(x => x.HouseholdId == householdId && x.Id == loanId && x.ReturnedAt == null, ct);
            if (loan is null) return Results.NotFound();
            var package = await db.InventoryPackages.SingleAsync(x => x.HouseholdId == householdId && x.Id == loan.PackageId, ct);
            if (package.PersonId != loan.BorrowerPersonId || package.OwnerPersonId != loan.OwnerPersonId) return Results.Conflict(new { code = "loan_allocation_changed" });
            var actor = Actor(context); var now = DateTimeOffset.UtcNow;
            package.AssignTo(loan.OwnerPersonId); loan.Return(actor, now);
            db.InventoryPackageAssignmentEvents.Add(new InventoryPackageAssignmentEvent(Guid.NewGuid(), householdId, package.Id, actor, loan.BorrowerPersonId, loan.OwnerPersonId, now));
            db.SyncCommandReceipts.Add(new SyncCommandReceipt(Guid.NewGuid(), householdId, actor, request.IdempotencyKey, "package.returned", loan.Id, now));
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            return Results.Ok(new { id = loan.Id, replayed = false });
        });

        // Backwards-compatible metadata endpoint for older clients.
        app.MapPost("/api/households/{householdId:guid}/medications/{medicationId:guid}/settings", async (Guid householdId, Guid medicationId, MedicationSettingsRequest request, HttpContext context, MedicationTrackerDbContext db, CancellationToken ct) =>
        {
            if (!await IsMember(db, householdId, context, ct)) return Results.StatusCode(403);
            var tags = NormalizeTags(request.Tags); if (request.Category?.Length > 100 || tags is null) return Results.BadRequest();
            var medication = await db.Medications.SingleOrDefaultAsync(x => x.HouseholdId == householdId && x.Id == medicationId && x.DeletedAt == null, ct); if (medication is null) return Results.NotFound();
            var now = DateTimeOffset.UtcNow;
            db.MedicationChangeEvents.Add(new MedicationChangeEvent(Guid.NewGuid(), householdId, medication.Id, Actor(context), "updated", JsonSerializer.Serialize(new { medication.Category, medication.Tags, medication.IsActive }), JsonSerializer.Serialize(new { request.Category, Tags = tags, request.IsActive }), now));
            medication.UpdateMetadata(request.Category, tags, request.IsActive); await db.SaveChangesAsync(ct);
            return Results.Ok(new { medication.Id });
        });

        app.MapPut("/api/households/{householdId:guid}/medications/{medicationId:guid}", async (Guid householdId, Guid medicationId, MedicationUpdateRequest request, HttpContext context, MedicationTrackerDbContext db, CancellationToken ct) =>
        {
            if (!await IsMember(db, householdId, context, ct)) return Results.StatusCode(403);
            var tags = NormalizeTags(request.Tags);
            if (!ValidMedication(request.Name, request.Form, request.Strength, request.ActiveIngredient, request.Notes, request.Category, tags)) return Results.BadRequest();
            if (request.PersonId is not null && !await db.People.AnyAsync(x => x.HouseholdId == householdId && x.Id == request.PersonId, ct)) return Results.NotFound();
            var medication = await db.Medications.SingleOrDefaultAsync(x => x.HouseholdId == householdId && x.Id == medicationId && x.DeletedAt == null, ct);
            if (medication is null) return Results.NotFound();
            var previous = MedicationSnapshot(medication); var now = DateTimeOffset.UtcNow;
            medication.UpdateDetails(request.PersonId, request.Name, request.Form, request.Strength, request.ActiveIngredient, request.Notes, request.Category, tags!, request.IsActive);
            db.MedicationChangeEvents.Add(new MedicationChangeEvent(Guid.NewGuid(), householdId, medication.Id, Actor(context), "updated", previous, MedicationSnapshot(medication), now));
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { medication.Id });
        });

        app.MapDelete("/api/households/{householdId:guid}/medications/{medicationId:guid}", async (Guid householdId, Guid medicationId, HttpContext context, MedicationTrackerDbContext db, CancellationToken ct) =>
        {
            if (!await IsMember(db, householdId, context, ct)) return Results.StatusCode(403);
            var medication = await db.Medications.SingleOrDefaultAsync(x => x.HouseholdId == householdId && x.Id == medicationId && x.DeletedAt == null, ct);
            if (medication is null) return Results.NotFound();
            var activeLoan = await (from loan in db.InventoryLoans
                                    join package in db.InventoryPackages on loan.PackageId equals package.Id
                                    join item in db.InventoryItems on package.InventoryItemId equals item.Id
                                    where loan.HouseholdId == householdId && loan.ReturnedAt == null && item.MedicationId == medicationId
                                    select loan.Id).AnyAsync(ct);
            if (activeLoan) return Results.Conflict(new { code = "active_package_loan" });
            var actor = Actor(context); var now = DateTimeOffset.UtcNow; var previous = MedicationSnapshot(medication);
            medication.Delete(now);
            db.MedicationChangeEvents.Add(new MedicationChangeEvent(Guid.NewGuid(), householdId, medication.Id, actor, "deleted", previous, null, now));
            var regimens = await db.Regimens.Where(x => x.HouseholdId == householdId && x.MedicationId == medicationId && x.DeletedAt == null).ToListAsync(ct);
            foreach (var regimen in regimens)
            {
                regimen.Delete(now);
                db.RegimenChangeEvents.Add(new RegimenChangeEvent(Guid.NewGuid(), householdId, regimen.Id, actor, "deleted", JsonSerializer.Serialize(new { regimen.PersonId, regimen.MedicationId }), null, now));
            }
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        app.MapPut("/api/households/{householdId:guid}/regimens/{regimenId:guid}", async (Guid householdId, Guid regimenId, RegimenUpdateRequest request, HttpContext context, MedicationTrackerDbContext db, CancellationToken ct) =>
        {
            if (!await IsMember(db, householdId, context, ct)) return Results.StatusCode(403);
            if (!ValidRegimen(request, out var dose)) return Results.BadRequest();
            var medicationExists = await db.Medications.AnyAsync(x => x.Id == request.MedicationId && x.HouseholdId == householdId && x.IsActive && x.DeletedAt == null, ct);
            var personExists = await db.People.AnyAsync(x => x.Id == request.PersonId && x.HouseholdId == householdId, ct);
            if (!medicationExists || !personExists) return Results.NotFound();
            var regimen = await db.Regimens.SingleOrDefaultAsync(x => x.Id == regimenId && x.HouseholdId == householdId && x.DeletedAt == null, ct);
            if (regimen is null) return Results.NotFound();
            var current = await db.RegimenVersions.Where(x => x.RegimenId == regimen.Id).OrderByDescending(x => x.CreatedAt).FirstAsync(ct);
            var previous = RegimenSnapshot(regimen, current); var now = DateTimeOffset.UtcNow;
            regimen.UpdateAssignment(request.PersonId, request.MedicationId);
            var version = new RegimenVersion(Guid.NewGuid(), regimen.Id, request.ValidFrom, request.ValidTo, dose.Numerator, dose.Denominator, request.LocalTime, request.TimeZoneId, now, request.ScheduleType, request.DayPeriod, request.MealRelation, request.MinimumIntervalMinutes);
            db.RegimenVersions.Add(version);
            db.RegimenChangeEvents.Add(new RegimenChangeEvent(Guid.NewGuid(), householdId, regimen.Id, Actor(context), "updated", previous, RegimenSnapshot(regimen, version), now));
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { regimen.Id, regimenVersionId = version.Id });
        });

        app.MapDelete("/api/households/{householdId:guid}/regimens/{regimenId:guid}", async (Guid householdId, Guid regimenId, HttpContext context, MedicationTrackerDbContext db, CancellationToken ct) =>
        {
            if (!await IsMember(db, householdId, context, ct)) return Results.StatusCode(403);
            var regimen = await db.Regimens.SingleOrDefaultAsync(x => x.Id == regimenId && x.HouseholdId == householdId && x.DeletedAt == null, ct);
            if (regimen is null) return Results.NotFound();
            var current = await db.RegimenVersions.Where(x => x.RegimenId == regimen.Id).OrderByDescending(x => x.CreatedAt).FirstAsync(ct);
            var now = DateTimeOffset.UtcNow;
            db.RegimenChangeEvents.Add(new RegimenChangeEvent(Guid.NewGuid(), householdId, regimen.Id, Actor(context), "deleted", RegimenSnapshot(regimen, current), null, now));
            regimen.Delete(now); await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        app.MapPost("/api/households/{householdId:guid}/inventory/{medicationId:guid}", async (Guid householdId, Guid medicationId, InventoryRequest request, HttpContext context, MedicationTrackerDbContext db, CancellationToken ct) =>
        {
            if (!await IsMember(db, householdId, context, ct)) return Results.StatusCode(403);
            if (request.Kind is not ("refill" or "count") || request.Numerator < 0 || request.Numerator > 1000000 || request.Denominator is < 1 or > 10000 || (request.Kind == "refill" && request.Numerator == 0) || string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 100) return Results.BadRequest();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({householdId.ToString()}, 0))", ct);
            var prior = await db.SyncCommandReceipts.SingleOrDefaultAsync(x => x.HouseholdId == householdId && x.IdempotencyKey == request.IdempotencyKey, ct);
            if (prior is not null) return Results.Ok(new { id = prior.ResultEntityId, replayed = true });
            var item = await (from inventory in db.InventoryItems join medication in db.Medications on inventory.MedicationId equals medication.Id where inventory.HouseholdId == householdId && inventory.MedicationId == medicationId && medication.DeletedAt == null select inventory).SingleOrDefaultAsync(ct);
            if (item is null) return Results.NotFound();
            var entries = await db.InventoryLedgerEntries.Where(x => x.InventoryItemId == item.Id).ToListAsync(ct);
            var before = entries.Aggregate(new ExactQuantity(0), (sum, x) => sum + new ExactQuantity(x.QuantityNumerator, x.QuantityDenominator));
            var observed = new ExactQuantity(request.Numerator, request.Denominator); var delta = request.Kind == "count" ? observed - before : observed;
            var now = DateTimeOffset.UtcNow; var actor = Actor(context); var ledgerId = Guid.NewGuid();
            db.InventoryLedgerEntries.Add(new InventoryLedgerEntry(ledgerId, householdId, item.Id, null, delta.Numerator, delta.Denominator, request.Kind == "count" ? "count_reconciliation" : "refill", now, now));
            if (request.Kind == "count")
            {
                var batch = new InventoryCountBatch { Id = Guid.NewGuid(), HouseholdId = householdId, AccountId = actor, RevisionNumber = 1, AcceptedAt = now };
                db.InventoryCountBatches.Add(batch);
                db.InventoryCounts.Add(new InventoryCount { Id = Guid.NewGuid(), HouseholdId = householdId, BatchId = batch.Id, InventoryItemId = item.Id, AccountId = actor, BeforeNumerator = before.Numerator, BeforeDenominator = before.Denominator, ObservedNumerator = observed.Numerator, ObservedDenominator = observed.Denominator, LedgerEntryId = ledgerId, AcceptedAt = now });
            }
            db.SyncCommandReceipts.Add(new SyncCommandReceipt(Guid.NewGuid(), householdId, actor, request.IdempotencyKey, request.Kind, ledgerId, now));
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            return Results.Ok(new { id = ledgerId, replayed = false });
        });

        app.MapPost("/api/households/{householdId:guid}/inventory/count-sessions", async (Guid householdId, BulkInventoryCountRequest request, HttpContext context, MedicationTrackerDbContext db, CancellationToken ct) =>
        {
            if (!await IsMember(db, householdId, context, ct)) return Results.StatusCode(403);
            if (!ValidCountRequest(request)) return Results.BadRequest();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({householdId.ToString()}, 0))", ct);
            var prior = await db.SyncCommandReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.HouseholdId == householdId && x.IdempotencyKey == request.IdempotencyKey, ct);
            if (prior is not null) return Results.Ok(new { id = prior.ResultEntityId, replayed = true });
            var actor = Actor(context); var now = DateTimeOffset.UtcNow;
            var batch = new InventoryCountBatch { Id = Guid.NewGuid(), HouseholdId = householdId, AccountId = actor, RevisionNumber = 1, AcceptedAt = now };
            if (!await AddCountLines(db, householdId, actor, batch.Id, request.Items, now, ct)) return Results.NotFound();
            db.InventoryCountBatches.Add(batch);
            db.SyncCommandReceipts.Add(new SyncCommandReceipt(Guid.NewGuid(), householdId, actor, request.IdempotencyKey, "inventory.counted", batch.Id, now));
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            return Results.Ok(new { id = batch.Id, revisionNumber = batch.RevisionNumber, replayed = false });
        });

        app.MapPost("/api/households/{householdId:guid}/inventory/count-sessions/{batchId:guid}/revisions", async (Guid householdId, Guid batchId, BulkInventoryCountRequest request, HttpContext context, MedicationTrackerDbContext db, CancellationToken ct) =>
        {
            if (!await IsMember(db, householdId, context, ct)) return Results.StatusCode(403);
            if (!ValidCountRequest(request)) return Results.BadRequest();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({householdId.ToString()}, 0))", ct);
            var prior = await db.SyncCommandReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.HouseholdId == householdId && x.IdempotencyKey == request.IdempotencyKey, ct);
            if (prior is not null) return Results.Ok(new { id = prior.ResultEntityId, replayed = true });
            var source = await db.InventoryCountBatches.AsNoTracking().SingleOrDefaultAsync(x => x.HouseholdId == householdId && x.Id == batchId, ct);
            if (source is null) return Results.NotFound();
            if (await db.InventoryCountBatches.AnyAsync(x => x.PreviousBatchId == source.Id, ct)) return Results.Conflict(new { code = "stale_count_revision" });
            var sourceItems = await db.InventoryCounts.AsNoTracking().Where(x => x.HouseholdId == householdId && x.BatchId == source.Id).Select(x => x.InventoryItemId).OrderBy(x => x).ToArrayAsync(ct);
            var requestedMedicationIds = request.Items.Select(x => x.MedicationId).ToArray();
            var requestedItems = await db.InventoryItems.AsNoTracking().Where(x => x.HouseholdId == householdId && requestedMedicationIds.Contains(x.MedicationId)).Select(x => x.Id).OrderBy(x => x).ToArrayAsync(ct);
            if (!sourceItems.SequenceEqual(requestedItems)) return Results.BadRequest();
            var actor = Actor(context); var now = DateTimeOffset.UtcNow;
            var revision = new InventoryCountBatch { Id = Guid.NewGuid(), HouseholdId = householdId, AccountId = actor, PreviousBatchId = source.Id, RevisionNumber = source.RevisionNumber + 1, AcceptedAt = now };
            if (!await AddCountLines(db, householdId, actor, revision.Id, request.Items, now, ct)) return Results.NotFound();
            db.InventoryCountBatches.Add(revision);
            db.SyncCommandReceipts.Add(new SyncCommandReceipt(Guid.NewGuid(), householdId, actor, request.IdempotencyKey, "inventory.count.revised", revision.Id, now));
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            return Results.Ok(new { id = revision.Id, revisionNumber = revision.RevisionNumber, replayed = false });
        });

        app.MapGet("/api/households/{householdId:guid}/workspace", async (Guid householdId, HttpContext context, MedicationTrackerDbContext db, CancellationToken ct) =>
        {
            if (!await IsMember(db, householdId, context, ct)) return Results.StatusCode(403);
            await using var snapshot = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
            var people = await db.People.AsNoTracking().Where(x => x.HouseholdId == householdId).OrderBy(x => x.CreatedAt).ToListAsync(ct);
            var allMedications = await db.Medications.AsNoTracking().Where(x => x.HouseholdId == householdId).OrderBy(x => x.CreatedAt).ToListAsync(ct);
            var medications = allMedications.Where(x => x.DeletedAt == null).ToList();
            var items = await db.InventoryItems.AsNoTracking().Where(x => x.HouseholdId == householdId).ToListAsync(ct);
            var packages = await db.InventoryPackages.AsNoTracking().Where(x => x.HouseholdId == householdId).OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToListAsync(ct);
            var ledger = await db.InventoryLedgerEntries.AsNoTracking().Where(x => x.HouseholdId == householdId).OrderByDescending(x => x.RecordedAt).ToListAsync(ct);
            var regimenRoots = await db.Regimens.AsNoTracking().Where(x => x.HouseholdId == householdId).OrderBy(x => x.CreatedAt).ToListAsync(ct);
            var regimenIds = regimenRoots.Select(x => x.Id).ToArray();
            var versions = await db.RegimenVersions.AsNoTracking().Where(x => regimenIds.Contains(x.RegimenId)).ToListAsync(ct);
            var administrations = await db.AdministrationEvents.AsNoTracking().Where(x => x.HouseholdId == householdId).ToListAsync(ct);
            var medicationChanges = await db.MedicationChangeEvents.AsNoTracking().Where(x => x.HouseholdId == householdId).ToListAsync(ct);
            var regimenChanges = await db.RegimenChangeEvents.AsNoTracking().Where(x => x.HouseholdId == householdId).ToListAsync(ct);
            var assignmentEvents = await db.InventoryPackageAssignmentEvents.AsNoTracking().Where(x => x.HouseholdId == householdId).ToListAsync(ct);
            var loans = await db.InventoryLoans.AsNoTracking().Where(x => x.HouseholdId == householdId).ToListAsync(ct);
            var countBatches = await db.InventoryCountBatches.AsNoTracking().Where(x => x.HouseholdId == householdId).OrderByDescending(x => x.AcceptedAt).ToListAsync(ct);
            var counts = await db.InventoryCounts.AsNoTracking().Where(x => x.HouseholdId == householdId && x.BatchId != null).ToListAsync(ct);

            var currentRegimens = regimenRoots.Where(r => r.DeletedAt == null && allMedications.Any(m => m.Id == r.MedicationId && m.DeletedAt == null)).Select(r =>
            {
                var v = versions.Where(x => x.RegimenId == r.Id).OrderByDescending(x => x.CreatedAt).First();
                return new { r.Id, r.PersonId, r.MedicationId, versionId = v.Id, v.ValidFrom, v.ValidTo, v.LocalTime, v.TimeZoneId, v.DoseNumerator, v.DoseDenominator, v.ScheduleType, v.DayPeriod, v.MealRelation, v.MinimumIntervalMinutes };
            }).ToArray();

            var medicationNames = allMedications.ToDictionary(x => x.Id, x => x.Name);
            var personNames = people.ToDictionary(x => x.Id, x => x.Name);
            var itemMedicationIds = items.ToDictionary(x => x.Id, x => x.MedicationId);
            var packageItems = packages.ToDictionary(x => x.Id, x => x.InventoryItemId);
            var versionMap = versions.ToDictionary(x => x.Id);
            var activities = new List<ActivityRow>();
            activities.AddRange(allMedications.Select(m => new ActivityRow($"medication:{m.Id}", "medication_created", m.Id, m.Name, m.PersonId, Name(personNames, m.PersonId), null, null, m.CreatedAt, m.CreatedAt)));
            activities.AddRange(medicationChanges.Select(e => new ActivityRow($"medication-change:{e.Id}", e.Kind == "deleted" ? "medication_deleted" : "medication_updated", e.MedicationId, Name(medicationNames, e.MedicationId), null, null, null, null, e.RecordedAt, e.RecordedAt)));
            activities.AddRange(regimenRoots.Select(r => new ActivityRow($"regimen:{r.Id}", "regimen_created", r.MedicationId, Name(medicationNames, r.MedicationId), r.PersonId, Name(personNames, r.PersonId), null, null, r.CreatedAt, r.CreatedAt)));
            activities.AddRange(regimenChanges.Select(e =>
            {
                var r = regimenRoots.Single(x => x.Id == e.RegimenId);
                return new ActivityRow($"regimen-change:{e.Id}", $"regimen_{e.Kind}", r.MedicationId, Name(medicationNames, r.MedicationId), r.PersonId, Name(personNames, r.PersonId), null, null, e.RecordedAt, e.RecordedAt);
            }));
            activities.AddRange(administrations.Select(a =>
            {
                var v = versionMap[a.RegimenVersionId];
                return new ActivityRow($"administration:{a.Id}", $"administration_{a.Outcome}", a.MedicationId, Name(medicationNames, a.MedicationId), a.PersonId, Name(personNames, a.PersonId), a.Outcome == "taken" ? v.DoseNumerator : null, a.Outcome == "taken" ? v.DoseDenominator : null, a.OccurredAt, a.RecordedAt);
            }));
            activities.AddRange(ledger.Where(x => x.Reason != "administration").Select(e =>
            {
                var medicationId = itemMedicationIds[e.InventoryItemId];
                return new ActivityRow($"ledger:{e.Id}", $"inventory_{e.Reason}", medicationId, Name(medicationNames, medicationId), null, null, e.QuantityNumerator, e.QuantityDenominator, e.OccurredAt, e.RecordedAt);
            }));
            activities.AddRange(assignmentEvents.Select(e =>
            {
                var medicationId = itemMedicationIds[packageItems[e.PackageId]];
                return new ActivityRow($"assignment:{e.Id}", e.ToPersonId is null ? "package_unassigned" : "package_assigned", medicationId, Name(medicationNames, medicationId), e.ToPersonId, Name(personNames, e.ToPersonId), null, null, e.RecordedAt, e.RecordedAt);
            }));
            activities.AddRange(loans.SelectMany(loan =>
            {
                var medicationId = itemMedicationIds[packageItems[loan.PackageId]];
                var rows = new List<ActivityRow> { new($"loan:{loan.Id}", "package_lent", medicationId, Name(medicationNames, medicationId), loan.BorrowerPersonId, Name(personNames, loan.BorrowerPersonId), null, null, loan.LentAt, loan.LentAt) };
                if (loan.ReturnedAt is not null) rows.Add(new ActivityRow($"loan-return:{loan.Id}", "package_returned", medicationId, Name(medicationNames, medicationId), loan.OwnerPersonId, Name(personNames, loan.OwnerPersonId), null, null, loan.ReturnedAt.Value, loan.ReturnedAt.Value));
                return rows;
            }));

            return Results.Ok(new
            {
                people,
                medications = medications.Select(m =>
                {
                    var item = items.Single(x => x.MedicationId == m.Id);
                    var stock = ledger.Where(x => x.InventoryItemId == item.Id).Aggregate(new ExactQuantity(0), (sum, x) => sum + new ExactQuantity(x.QuantityNumerator, x.QuantityDenominator));
                    var packageRows = packages.Where(x => x.InventoryItemId == item.Id).Select((package, index) =>
                    {
                        var balance = ledger.Where(x => x.PackageId == package.Id).Aggregate(new ExactQuantity(0), (sum, x) => sum + new ExactQuantity(x.QuantityNumerator, x.QuantityDenominator));
                        var activeLoan = loans.SingleOrDefault(x => x.PackageId == package.Id && x.ReturnedAt == null);
                        return new { package.Id, number = index + 1, package.OwnerPersonId, package.PersonId, activeLoanId = activeLoan?.Id, package.CapacityNumerator, package.CapacityDenominator, remainingNumerator = balance.Numerator, remainingDenominator = balance.Denominator };
                    });
                    return new { m.Id, m.PersonId, m.Name, m.Form, m.Strength, m.ActiveIngredient, m.Notes, m.Category, m.Tags, m.IsActive, inventoryItemId = item.Id, stockNumerator = stock.Numerator, stockDenominator = stock.Denominator, packages = packageRows };
                }),
                regimens = currentRegimens,
                ledger,
                administrations,
                countSessions = countBatches.Select(batch => new
                {
                    batch.Id,
                    previousSessionId = batch.PreviousBatchId,
                    batch.RevisionNumber,
                    batch.AcceptedAt,
                    items = counts.Where(x => x.BatchId == batch.Id).Select(count =>
                    {
                        var medicationId = itemMedicationIds[count.InventoryItemId];
                        return new { medicationId, medicationName = Name(medicationNames, medicationId), count.BeforeNumerator, count.BeforeDenominator, count.ObservedNumerator, count.ObservedDenominator };
                    })
                }),
                activities = activities.OrderByDescending(x => x.RecordedAt).ThenByDescending(x => x.Id)
            });
        });
    }

    private static bool ValidMedication(string name, string form, string? strength, string? activeIngredient, string? notes, string? category, string[]? tags) =>
        !string.IsNullOrWhiteSpace(name) && name.Length <= 200 && form == "tablet"
        && (strength is null || strength.Length <= 100)
        && (activeIngredient is null || activeIngredient.Length <= 200)
        && (notes is null || notes.Length <= 2000)
        && (category is null || category.Length <= 100)
        && tags is not null;

    private static bool ValidRegimen(RegimenUpdateRequest request, out ExactQuantity dose)
    {
        dose = default;
        if (!TryPositive(request.DoseNumerator, request.DoseDenominator, out dose)) return false;
        if (request.ValidFrom is not null && request.ValidTo < request.ValidFrom) return false;
        if (request.ScheduleType is not ("scheduled" or "as_needed") || (request.ScheduleType == "scheduled" && request.LocalTime is null && request.DayPeriod is null) || !ValidDayPeriod(request.DayPeriod) || !ValidMealRelation(request.MealRelation) || request.MinimumIntervalMinutes is < 1 or > 10080) return false;
        if (string.IsNullOrWhiteSpace(request.TimeZoneId)) return false;
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(request.TimeZoneId); }
        catch (TimeZoneNotFoundException) { return false; }
        catch (InvalidTimeZoneException) { return false; }
        return true;
    }

    private static bool TryPositive(long numerator, long denominator, out ExactQuantity quantity)
    {
        quantity = default;
        if (numerator is <= 0 or > 1000000 || denominator is <= 0 or > 10000) return false;
        quantity = new ExactQuantity(numerator, denominator); return true;
    }

    private static int Compare(ExactQuantity left, ExactQuantity right) => checked(left.Numerator * right.Denominator).CompareTo(checked(right.Numerator * left.Denominator));
    private static bool ValidCountRequest(BulkInventoryCountRequest request) =>
        !string.IsNullOrWhiteSpace(request.IdempotencyKey) && request.IdempotencyKey.Length <= 100
        && request.Items is { Count: > 0 and <= 100 }
        && request.Items.Select(x => x.MedicationId).Distinct().Count() == request.Items.Count
        && request.Items.All(x => x.ObservedNumerator is >= 0 and <= 1000000 && x.ObservedDenominator is >= 1 and <= 10000);

    private static async Task<bool> AddCountLines(MedicationTrackerDbContext db, Guid householdId, Guid actor, Guid batchId,
        IReadOnlyList<InventoryCountLineRequest> lines, DateTimeOffset acceptedAt, CancellationToken ct)
    {
        var medicationIds = lines.Select(x => x.MedicationId).ToArray();
        var items = await (from item in db.InventoryItems
                           join medication in db.Medications on item.MedicationId equals medication.Id
                           where item.HouseholdId == householdId && medication.DeletedAt == null && medicationIds.Contains(item.MedicationId)
                           select item).ToListAsync(ct);
        if (items.Count != medicationIds.Length) return false;
        var itemIds = items.Select(x => x.Id).ToArray();
        var entries = await db.InventoryLedgerEntries.Where(x => x.HouseholdId == householdId && itemIds.Contains(x.InventoryItemId)).ToListAsync(ct);
        foreach (var line in lines)
        {
            var item = items.Single(x => x.MedicationId == line.MedicationId);
            var before = entries.Where(x => x.InventoryItemId == item.Id).Aggregate(new ExactQuantity(0), (sum, entry) => sum + new ExactQuantity(entry.QuantityNumerator, entry.QuantityDenominator));
            var observed = new ExactQuantity(line.ObservedNumerator, line.ObservedDenominator);
            var delta = observed - before; var ledgerId = Guid.NewGuid();
            db.InventoryLedgerEntries.Add(new InventoryLedgerEntry(ledgerId, householdId, item.Id, null, delta.Numerator, delta.Denominator, "count_reconciliation", acceptedAt, acceptedAt));
            db.InventoryCounts.Add(new InventoryCount { Id = Guid.NewGuid(), HouseholdId = householdId, BatchId = batchId, InventoryItemId = item.Id, AccountId = actor, BeforeNumerator = before.Numerator, BeforeDenominator = before.Denominator, ObservedNumerator = observed.Numerator, ObservedDenominator = observed.Denominator, LedgerEntryId = ledgerId, AcceptedAt = acceptedAt });
        }
        return true;
    }
    private static string[]? NormalizeTags(string[]? tags)
    {
        var normalized = (tags ?? []).Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return normalized.Length <= 12 && normalized.All(x => x.Length <= 40) ? normalized : null;
    }
    private static bool ValidDayPeriod(string? value) => value is null or "morning" or "noon" or "evening" or "night" or "bedtime";
    private static bool ValidMealRelation(string? value) => value is null or "fasting" or "with_food" or "after_food" or "before_food";
    private static Guid Actor(HttpContext context) => Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static string? Name(IReadOnlyDictionary<Guid, string> names, Guid? id) => id is not null && names.TryGetValue(id.Value, out var name) ? name : null;
    private static string MedicationSnapshot(Medication medication) => JsonSerializer.Serialize(new { medication.PersonId, medication.Name, medication.Form, medication.Strength, medication.ActiveIngredient, medication.Notes, medication.Category, medication.Tags, medication.IsActive });
    private static string RegimenSnapshot(Regimen regimen, RegimenVersion version) => JsonSerializer.Serialize(new { regimen.PersonId, regimen.MedicationId, version.ValidFrom, version.ValidTo, version.DoseNumerator, version.DoseDenominator, version.LocalTime, version.TimeZoneId, version.ScheduleType, version.DayPeriod, version.MealRelation, version.MinimumIntervalMinutes });
    private static Task<bool> IsMember(MedicationTrackerDbContext db, Guid householdId, HttpContext context, CancellationToken ct)
    {
        var id = Actor(context); var now = DateTimeOffset.UtcNow;
        return db.HouseholdMemberships.AnyAsync(x => x.HouseholdId == householdId && x.AccountId == id && x.ValidFrom <= now && (x.ValidTo == null || x.ValidTo > now), ct);
    }
}

public sealed record InventoryRequest(string IdempotencyKey, string Kind, long Numerator, long Denominator);
public sealed record AddPackageRequest(string IdempotencyKey, long CapacityNumerator, long CapacityDenominator, long RemainingNumerator, long RemainingDenominator, Guid? PersonId = null, bool FromExistingStock = false);
public sealed record AssignPackageRequest(string IdempotencyKey, Guid? PersonId);
public sealed record CreatePackageLoanRequest(string IdempotencyKey, Guid BorrowerPersonId);
public sealed record ReturnPackageLoanRequest(string IdempotencyKey);
public sealed record MedicationSettingsRequest(string? Category, string[]? Tags, bool IsActive);
public sealed record MedicationUpdateRequest(Guid? PersonId, string Name, string Form, string? Strength, string? ActiveIngredient, string? Notes, string? Category, string[]? Tags, bool IsActive);
public sealed record RegimenUpdateRequest(Guid PersonId, Guid MedicationId, DateOnly? ValidFrom, DateOnly? ValidTo, long DoseNumerator, long DoseDenominator, TimeOnly? LocalTime, string TimeZoneId, string ScheduleType = "scheduled", string? DayPeriod = null, string? MealRelation = null, int? MinimumIntervalMinutes = null);
public sealed record InventoryCountLineRequest(Guid MedicationId, long ObservedNumerator, long ObservedDenominator);
public sealed record BulkInventoryCountRequest(string IdempotencyKey, List<InventoryCountLineRequest> Items);
public sealed record ActivityRow(string Id, string Kind, Guid MedicationId, string? MedicationName, Guid? PersonId, string? PersonName, long? QuantityNumerator, long? QuantityDenominator, DateTimeOffset OccurredAt, DateTimeOffset RecordedAt);
