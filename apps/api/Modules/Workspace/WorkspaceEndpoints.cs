using MedicationTracker.Api.Application;
using MedicationTracker.Api.Domain.Inventory;
using MedicationTracker.Api.Domain.Quantities;
using MedicationTracker.Api.Modules.Inventory;
using MedicationTracker.Api.Modules.Sync;
using MedicationTracker.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedicationTracker.Api.Modules.Workspace;

/// <summary>
/// One aggregated read for a household, plus the counting and activity surfaces.
/// </summary>
/// <remarks>
/// Clients render the whole management view from a single response rather than issuing a
/// request per medication, which also keeps the mobile client's offline snapshot simple.
/// </remarks>
public static class WorkspaceEndpoints
{
    public const int ActivityPageSize = 200;

    public static IEndpointRouteBuilder MapWorkspaceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api/households/{householdId:guid}");

        api.MapGet("/workspace", async (
            Guid householdId,
            HttpContext context,
            MedicationTrackerDbContext db,
            CancellationToken ct) =>
        {
            if (!await HouseholdAccess.IsMemberAsync(db, householdId, context, ct))
            {
                return ApiResults.Forbidden();
            }

            var people = await db.People.AsNoTracking()
                .Where(person => person.HouseholdId == householdId)
                .OrderBy(person => person.Name)
                .Select(person => new { person.Id, person.Name, isArchived = person.ArchivedAt != null })
                .ToListAsync(ct);

            var definitions = await db.MedicationDefinitions.AsNoTracking()
                .Where(definition => definition.HouseholdId == householdId)
                .OrderBy(definition => definition.Name)
                .ToListAsync(ct);

            var packages = await db.Packages.AsNoTracking()
                .Where(package => package.HouseholdId == householdId)
                .OrderBy(package => package.Ordinal)
                .ToListAsync(ct);

            var balances = await PackageBalancesAsync(db, householdId, ct);
            var packageBalances = balances
                .Where(pair => pair.Key.PackageId is not null)
                .ToDictionary(pair => pair.Key.PackageId!.Value, pair => pair.Value);

            var loans = await db.PackageLoans.AsNoTracking()
                .Where(loan => loan.HouseholdId == householdId && loan.ReturnedAt == null)
                .ToDictionaryAsync(loan => loan.PackageId, loan => loan.Id, ct);

            var policies = await db.RefillPolicies.AsNoTracking()
                .Where(policy => policy.HouseholdId == householdId)
                .ToDictionaryAsync(policy => policy.MedicationDefinitionId, ct);

            var plans = await (from plan in db.TreatmentPlans.AsNoTracking()
                              join version in db.TreatmentPlanVersions.AsNoTracking()
                                  on plan.Id equals version.TreatmentPlanId
                              where plan.HouseholdId == householdId && plan.DeletedAt == null
                              select new { plan, version }).ToListAsync(ct);

            var currentPlans = plans
                .GroupBy(row => row.plan.Id)
                .Select(group => group.OrderByDescending(row => row.version.VersionNumber).First())
                .Select(row => new
                {
                    id = row.plan.Id,
                    versionId = row.version.Id,
                    versionNumber = row.version.VersionNumber,
                    personId = row.plan.PersonId,
                    medicationDefinitionId = row.plan.MedicationDefinitionId,
                    dose = InventoryEndpoints.Quantity(row.version.Dose),
                    kind = row.version.Kind.ToString(),
                    pattern = row.version.Pattern.ToString(),
                    weekdayMask = row.version.WeekdayMask,
                    intervalDays = row.version.IntervalDays,
                    effectiveFrom = row.version.EffectiveFrom,
                    effectiveTo = row.version.EffectiveTo,
                    localTime = row.version.LocalTime,
                    timeZoneId = row.version.TimeZoneId,
                    dayPeriod = row.version.DayPeriod?.ToString(),
                    mealRelation = row.version.MealRelation?.ToString(),
                    minimumIntervalMinutes = row.version.MinimumIntervalMinutes,
                    instructions = row.version.Instructions,
                })
                .ToList();

            var medications = definitions.Select(definition =>
            {
                var own = packages.Where(package => package.MedicationDefinitionId == definition.Id).ToList();
                var packageTotal = ExactQuantity.Sum(own.Select(package => Balance(packageBalances, package.Id)));
                var loose = balances.TryGetValue((definition.Id, null), out var looseBalance)
                    ? looseBalance
                    : ExactQuantity.Zero;

                policies.TryGetValue(definition.Id, out var policy);

                return new
                {
                    id = definition.Id,
                    name = definition.Name,
                    strength = definition.Strength,
                    brand = definition.Brand,
                    manufacturer = definition.Manufacturer,
                    form = definition.Form.ToString(),
                    unit = definition.Unit.ToString(),
                    activeIngredients = definition.ActiveIngredients,
                    defaultPackageCapacity = definition.DefaultPackageCapacity is { } capacity
                        ? InventoryEndpoints.Quantity(capacity)
                        : null,
                    category = definition.Category,
                    tags = definition.Tags,
                    notes = definition.Notes,
                    isArchived = definition.IsArchived,

                    // The default list shows a total and a package count; the packages
                    // themselves are there for the expanded view.
                    total = InventoryEndpoints.Quantity(packageTotal + loose),
                    packageCount = own.Count(package => package.IsAvailable),
                    loose = InventoryEndpoints.Quantity(loose),
                    packages = own.Select(package => new
                    {
                        view = InventoryEndpoints.PackageView(package, Balance(packageBalances, package.Id)),
                        activeLoanId = loans.TryGetValue(package.Id, out var loanId) ? loanId : (Guid?)null,
                    }),
                    refillPolicy = policy is null
                        ? null
                        : new
                        {
                            lowStockThreshold = policy.LowStockThreshold is { } threshold
                                ? InventoryEndpoints.Quantity(threshold)
                                : null,
                            lowStockDays = policy.LowStockDays,
                            nextEligibleRefillOn = policy.NextEligibleRefillOn,
                            note = policy.Note,
                        },
                };
            }).ToList();

            return Results.Ok(new { householdId, people, medications, plans = currentPlans });
        });

        api.MapGet("/activity", async (
            Guid householdId,
            HttpContext context,
            MedicationTrackerDbContext db,
            CancellationToken ct) =>
        {
            if (!await HouseholdAccess.IsMemberAsync(db, householdId, context, ct))
            {
                return ApiResults.Forbidden();
            }

            var ordinals = await db.Packages.AsNoTracking()
                .Where(package => package.HouseholdId == householdId)
                .Select(package => new { package.Id, package.Ordinal })
                .ToDictionaryAsync(package => package.Id, package => package.Ordinal, ct);

            var ledger = await db.LedgerEntries.AsNoTracking()
                .Where(entry => entry.HouseholdId == householdId)
                .OrderByDescending(entry => entry.RecordedAt)
                .Take(ActivityPageSize)
                .Select(entry => new
                {
                    kind = "inventory",
                    entry.Id,
                    entry.MedicationDefinitionId,
                    entryType = entry.EntryType,
                    entry.PackageId,
                    entry.QuantityNumerator,
                    entry.QuantityDenominator,
                    entry.OccurredAt,
                    entry.RecordedAt,
                    entry.ActorAccountId,
                    entry.Reason,
                })
                .ToListAsync(ct);

            var corrections = await db.AllocationCorrections.AsNoTracking()
                .Where(correction => correction.HouseholdId == householdId)
                .OrderByDescending(correction => correction.RecordedAt)
                .Take(ActivityPageSize)
                .ToListAsync(ct);

            var administrations = await db.AdministrationEvents.AsNoTracking()
                .Where(e => e.HouseholdId == householdId)
                .OrderByDescending(e => e.RecordedAt)
                .Take(ActivityPageSize)
                .ToListAsync(ct);

            return Results.Ok(new
            {
                inventory = ledger.Select(entry => new
                {
                    entry.Id,
                    entry.MedicationDefinitionId,
                    entryType = entry.entryType.ToString(),
                    packageLabel = entry.PackageId is { } id && ordinals.TryGetValue(id, out var ordinal)
                        ? ordinal
                        : (int?)null,
                    quantity = InventoryEndpoints.Quantity(
                        new ExactQuantity(entry.QuantityNumerator, entry.QuantityDenominator)),
                    entry.OccurredAt,
                    entry.RecordedAt,
                    entry.ActorAccountId,
                    entry.Reason,
                }),
                administrations = administrations.Select(e => new
                {
                    e.Id,
                    e.PersonId,
                    e.MedicationDefinitionId,
                    outcome = e.Outcome.ToString(),
                    stockSource = e.StockSource.ToString(),
                    actualQuantity = e.ActualQuantity is { } actual
                        ? InventoryEndpoints.Quantity(actual)
                        : null,
                    e.ScheduledFor,
                    e.OccurredAt,
                    e.RecordedAt,
                    latenessMinutes = e.LatenessMinutes,
                    e.ActorAccountId,
                }),

                // Rendered as "stock source corrected from Box 1 to Box 2".
                allocationCorrections = corrections.Select(correction => new
                {
                    correction.Id,
                    correction.AdministrationEventId,
                    fromPackageLabel = correction.FromPackageId is { } from
                                       && ordinals.TryGetValue(from, out var fromOrdinal)
                        ? fromOrdinal
                        : (int?)null,
                    toPackageLabel = correction.ToPackageId is { } to && ordinals.TryGetValue(to, out var toOrdinal)
                        ? toOrdinal
                        : (int?)null,
                    quantity = InventoryEndpoints.Quantity(correction.Quantity),
                    correction.Reason,
                    correction.ActorAccountId,
                    correction.RecordedAt,
                }),
            });
        });

        MapCountEndpoints(api);

        return endpoints;
    }

    private static void MapCountEndpoints(RouteGroupBuilder api)
    {
        api.MapPost("/inventory/count-sessions", async (
            Guid householdId,
            BulkInventoryCountRequest request,
            HttpContext context,
            MedicationTrackerDbContext db,
            CancellationToken ct) =>
        {
            if (!await HouseholdAccess.IsMemberAsync(db, householdId, context, ct))
            {
                return ApiResults.Forbidden();
            }

            return await AcceptCountAsync(db, householdId, request, null, context, ct);
        });

        // A correction never edits an accepted count; it adds a revision pointing at it.
        api.MapPost("/inventory/count-sessions/{batchId:guid}/revisions", async (
            Guid householdId,
            Guid batchId,
            BulkInventoryCountRequest request,
            HttpContext context,
            MedicationTrackerDbContext db,
            CancellationToken ct) =>
        {
            if (!await HouseholdAccess.IsMemberAsync(db, householdId, context, ct))
            {
                return ApiResults.Forbidden();
            }

            return await AcceptCountAsync(db, householdId, request, batchId, context, ct);
        });
    }

    private static async Task<IResult> AcceptCountAsync(
        MedicationTrackerDbContext db,
        Guid householdId,
        BulkInventoryCountRequest request,
        Guid? previousBatchId,
        HttpContext context,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey)
            || request.IdempotencyKey.Length > 100
            || request.Lines is null or { Count: 0 or > 200 })
        {
            return ApiResults.Invalid("lines", "invalid");
        }

        var observed = new List<(Guid DefinitionId, Guid? PackageId, ExactQuantity Observed)>(request.Lines.Count);
        foreach (var line in request.Lines)
        {
            if (!ExactQuantity.TryCreateNonNegative(
                    line.ObservedNumerator, line.ObservedDenominator, out var amount))
            {
                return ApiResults.Invalid("lines", "invalid_observed");
            }

            observed.Add((line.MedicationDefinitionId, line.PackageId, amount));
        }

        if (observed.Select(line => (line.DefinitionId, line.PackageId)).Distinct().Count() != observed.Count)
        {
            return ApiResults.Invalid("lines", "duplicate_target");
        }

        var accountId = HouseholdAccess.RequireAccountId(context);
        var now = DateTimeOffset.UtcNow;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await InventoryReader.LockHouseholdAsync(db, householdId, ct);

        var prior = await db.SyncCommandReceipts.AsNoTracking().SingleOrDefaultAsync(
            receipt => receipt.HouseholdId == householdId && receipt.IdempotencyKey == request.IdempotencyKey, ct);

        if (prior is not null)
        {
            return Results.Ok(new { batchId = prior.ResultEntityId, replayed = true });
        }

        var revisionNumber = 1;
        if (previousBatchId is { } previousId)
        {
            var previous = await db.InventoryCountBatches.AsNoTracking().SingleOrDefaultAsync(
                batch => batch.Id == previousId && batch.HouseholdId == householdId, ct);

            if (previous is null)
            {
                return Results.NotFound();
            }

            // Only the newest revision may be revised, so the chain stays linear.
            if (await db.InventoryCountBatches.AsNoTracking().AnyAsync(
                    batch => batch.PreviousBatchId == previousId, ct))
            {
                return ApiResults.Conflict("stale_revision");
            }

            revisionNumber = previous.RevisionNumber + 1;
        }

        var batch = new InventoryCountBatch(
            Guid.CreateVersion7(), householdId, accountId, previousBatchId, revisionNumber, now);

        db.InventoryCountBatches.Add(batch);

        foreach (var line in observed)
        {
            var legacyItemId = await db.LegacyInventoryItems.AsNoTracking()
                .Where(item => item.MedicationDefinitionId == line.DefinitionId
                               && item.HouseholdId == householdId)
                .Select(item => item.Id)
                .SingleOrDefaultAsync(ct);

            if (legacyItemId == Guid.Empty)
            {
                return Results.NotFound();
            }

            var stock = await InventoryReader.LoadAsync(db, householdId, line.DefinitionId, tracked: false, ct);

            // A default count is medication-level; an advanced one reconciles one package.
            var before = line.PackageId is { } packageId ? stock.BalanceOf(packageId) : stock.Total;
            var adjustment = line.Observed - before;

            // A zero adjustment is still recorded: "we counted this and it matched" is
            // information the household and the audit trail both want.
            var entry = InventoryLedgerEntry.Adjustment(
                householdId, line.DefinitionId, legacyItemId, line.PackageId, adjustment,
                LedgerEntryType.CountAdjustment, batch.Id, accountId, now, now, request.Note);

            db.LedgerEntries.Add(entry);
            db.InventoryCounts.Add(new InventoryCount(
                Guid.CreateVersion7(), householdId, batch.Id, legacyItemId, accountId,
                before, line.Observed, entry.Id, now, line.PackageId));
        }

        db.SyncCommandReceipts.Add(new SyncCommandReceipt(
            Guid.CreateVersion7(), householdId, accountId, request.IdempotencyKey,
            "inventory.count", batch.Id, now));

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return Results.Ok(new { batchId = batch.Id, revisionNumber, replayed = false });
    }

    private static async Task<Dictionary<(Guid DefinitionId, Guid? PackageId), ExactQuantity>> PackageBalancesAsync(
        MedicationTrackerDbContext db,
        Guid householdId,
        CancellationToken ct)
    {
        var entries = await db.LedgerEntries.AsNoTracking()
            .Where(entry => entry.HouseholdId == householdId)
            .Select(entry => new
            {
                entry.MedicationDefinitionId,
                entry.PackageId,
                entry.QuantityNumerator,
                entry.QuantityDenominator,
            })
            .ToListAsync(ct);

        var balances = new Dictionary<(Guid, Guid?), ExactQuantity>();
        foreach (var entry in entries)
        {
            var key = (entry.MedicationDefinitionId, entry.PackageId);
            var amount = new ExactQuantity(entry.QuantityNumerator, entry.QuantityDenominator);
            balances[key] = balances.TryGetValue(key, out var current) ? current + amount : amount;
        }

        return balances;
    }

    /// <summary>
    /// Package identifiers are globally unique, so the definition half of the key is not
    /// needed to find a package's balance. Looked up through a flattened index rather
    /// than scanned, so a household with many packages stays a single pass.
    /// </summary>
    private static ExactQuantity Balance(
        IReadOnlyDictionary<Guid, ExactQuantity> packageBalances,
        Guid packageId) =>
        packageBalances.TryGetValue(packageId, out var balance) ? balance : ExactQuantity.Zero;
}

public sealed record BulkInventoryCountRequest(
    string IdempotencyKey,
    List<InventoryCountLine> Lines,
    string? Note = null);

/// <summary>
/// One counted target. <paramref name="PackageId"/> is null for the default
/// medication-level count and set for advanced package-level reconciliation.
/// </summary>
public sealed record InventoryCountLine(
    Guid MedicationDefinitionId,
    long ObservedNumerator,
    long ObservedDenominator = 1,
    Guid? PackageId = null);
