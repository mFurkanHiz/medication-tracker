using System.Globalization;
using System.Text.Json;
using MedicationTracker.Api.Application;
using MedicationTracker.Api.Domain.Quantities;
using MedicationTracker.Api.Modules.Catalog;
using MedicationTracker.Api.Modules.Inventory;
using MedicationTracker.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedicationTracker.Api.Modules.Reports;

/// <summary>
/// Gives the household its own care data back as a file it can keep.
/// </summary>
/// <remarks>
/// <para>
/// Acceptance row 32. The product should never be the only place a household's
/// medication history exists, so the export is deliberately complete rather than a
/// summary: definitions, every physical package, the whole inventory ledger, every plan
/// version, every recorded dose with the packages that paid for it, corrections, and
/// counts. Read the file back and the audit trail is still there.
/// </para>
/// <para>
/// Two things are excluded on purpose, and <c>ReportApiTests</c> holds them excluded:
/// </para>
/// <list type="bullet">
/// <item>
/// Anything belonging to another household. Every query is filtered by the household in
/// the route, the route is reachable only by a current member, and the rows that hang
/// off a plan are reached by joining that household's plans rather than by trusting a
/// second household column.
/// </item>
/// <item>
/// Account identity — e-mail addresses, password hashes, sessions, membership rows. A
/// file that leaves the product should not carry the credentials or the contact details
/// of the people who share the household, so an actor appears as the opaque account
/// identifier the activity feed already shows and nothing more.
/// </item>
/// </list>
/// <para>
/// Every projection below materialises plain columns and is shaped afterwards in
/// memory. Converting an enum to text inside the query would make the export depend on
/// the provider translating <c>ToString</c>, which is not worth risking for a read whose
/// row counts are small.
/// </para>
/// </remarks>
public static class ExportEndpoints
{
    /// <summary>
    /// Bumped whenever the shape changes, so a file read back later can be understood.
    /// </summary>
    public const int SchemaVersion = 3;

    /// <summary>
    /// Rows taken from any one collection. A household holds orders of magnitude less
    /// than this, but an export must not be a way to ask the server for an unbounded
    /// response; a collection that hits the cap is named in <c>truncatedCollections</c>
    /// rather than quietly cut short.
    /// </summary>
    public const int MaximumRowsPerCollection = 20_000;

    /// <summary>Collections deliberately absent from the file.</summary>
    public static IReadOnlyList<string> ExcludedCollections { get; } =
        ["accounts", "credentials", "sessions", "householdMemberships"];

    /// <summary>
    /// The file is written by hand rather than returned through the usual result, so it
    /// has to restate the web defaults the rest of the API gets for free. Without the
    /// camel-case policy a projected column such as <c>person.Id</c> would land in the
    /// file as <c>Id</c> while every other endpoint calls it <c>id</c>, and a reader that
    /// handles both shapes is a reader nobody should have to write.
    /// </summary>
    private static readonly JsonSerializerOptions FileOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static IEndpointRouteBuilder MapExportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api/households/{householdId:guid}");

        api.MapGet("/export", async (
            Guid householdId,
            HttpContext context,
            MedicationTrackerDbContext db,
            CancellationToken ct) =>
        {
            if (!await HouseholdAccess.IsMemberAsync(db, householdId, context, ct))
            {
                return ApiResults.Forbidden();
            }

            var export = await BuildAsync(db, householdId, ct);
            var payload = JsonSerializer.SerializeToUtf8Bytes(export, FileOptions);

            // Downloaded rather than rendered: this is the household's file, and a
            // browser that opens a long JSON document in a tab is not what was asked for.
            var stamp = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return Results.File(payload, "application/json", $"medication-tracker-export-{stamp}.json");
        });

        return endpoints;
    }

    internal static async Task<object> BuildAsync(
        MedicationTrackerDbContext db,
        Guid householdId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);

        var truncated = new SortedSet<string>(StringComparer.Ordinal);

        var household = await db.Households.AsNoTracking()
            .Where(row => row.Id == householdId)
            .Select(row => new { row.Id, row.Name })
            .SingleOrDefaultAsync(ct);

        var people = await Take(
            db.People.AsNoTracking()
                .Where(person => person.HouseholdId == householdId)
                .OrderBy(person => person.Name),
            "people", truncated, ct);

        var definitions = await Take(
            db.MedicationDefinitions.AsNoTracking()
                .Where(definition => definition.HouseholdId == householdId)
                .OrderBy(definition => definition.Name),
            "medicationDefinitions", truncated, ct);

        var packages = await Take(
            db.Packages.AsNoTracking()
                .Where(package => package.HouseholdId == householdId)
                .OrderBy(package => package.Ordinal),
            "packages", truncated, ct);

        var ledger = await Take(
            db.LedgerEntries.AsNoTracking()
                .Where(entry => entry.HouseholdId == householdId)
                .OrderBy(entry => entry.RecordedAt).ThenBy(entry => entry.Id),
            "inventoryLedger", truncated, ct);

        var plans = await Take(
            db.TreatmentPlans.AsNoTracking()
                .Where(plan => plan.HouseholdId == householdId)
                .OrderBy(plan => plan.CreatedAt),
            "treatmentPlans", truncated, ct);

        var versions = await Take(
            from plan in db.TreatmentPlans.AsNoTracking()
            join version in db.TreatmentPlanVersions.AsNoTracking()
                on plan.Id equals version.TreatmentPlanId
            where plan.HouseholdId == householdId
            orderby version.TreatmentPlanId, version.VersionNumber
            select version,
            "treatmentPlanVersions", truncated, ct);

        var administrations = await Take(
            db.AdministrationEvents.AsNoTracking()
                .Where(e => e.HouseholdId == householdId)
                .OrderBy(e => e.OccurredAt).ThenBy(e => e.Id),
            "administrations", truncated, ct);

        var allocations = await Take(
            db.AdministrationAllocations.AsNoTracking()
                .Where(allocation => allocation.HouseholdId == householdId)
                .OrderBy(allocation => allocation.CreatedAt).ThenBy(allocation => allocation.Id),
            "administrationAllocations", truncated, ct);

        var corrections = await Take(
            db.AllocationCorrections.AsNoTracking()
                .Where(correction => correction.HouseholdId == householdId)
                .OrderBy(correction => correction.RecordedAt).ThenBy(correction => correction.Id),
            "allocationCorrections", truncated, ct);

        var countBatches = await Take(
            db.InventoryCountBatches.AsNoTracking()
                .Where(batch => batch.HouseholdId == householdId)
                .OrderBy(batch => batch.AcceptedAt).ThenBy(batch => batch.Id),
            "inventoryCountBatches", truncated, ct);

        // A count row points at the legacy inventory item rather than the definition, so
        // the medication is joined in here; a file nobody can read back is not an export.
        var counts = await Take(
            from count in db.InventoryCounts.AsNoTracking()
            join item in db.LegacyInventoryItems.AsNoTracking()
                on count.LegacyInventoryItemId equals item.Id
            where count.HouseholdId == householdId
            orderby count.AcceptedAt, count.Id
            select new { count, item.MedicationDefinitionId },
            "inventoryCounts", truncated, ct);

        var refillPolicies = await Take(
            db.RefillPolicies.AsNoTracking()
                .Where(policy => policy.HouseholdId == householdId)
                .OrderBy(policy => policy.MedicationDefinitionId),
            "refillPolicies", truncated, ct);

        // Derived, not stored. Included so a reader of the file does not have to replay
        // the whole ledger to answer "how much was there".
        var totals = await InventoryReader.TotalsAsync(db, householdId, ct);

        return new
        {
            schemaVersion = SchemaVersion,
            exportedAt = DateTimeOffset.UtcNow,
            householdId,
            householdName = household?.Name,

            // Named so the absence of account identity in this file reads as a decision
            // rather than as something that was forgotten.
            excluded = ExcludedCollections,
            truncatedCollections = truncated,

            people = people.Select(person => new
            {
                person.Id,
                person.Name,
                isArchived = person.ArchivedAt != null,
                person.ArchivedAt,
            }),

            medicationDefinitions = definitions.Select(definition => new
            {
                definition.Id,
                definition.Name,
                definition.Strength,
                definition.Brand,
                definition.Manufacturer,
                form = definition.Form.ToString(),
                unit = definition.Unit.ToString(),
                definition.ActiveIngredients,
                definition.Category,
                definition.Tags,
                definition.Notes,
                coverage = definition.Coverage.ToString(),
                cautions = CautionView.Of(definition),
                definition.IsArchived,
                total = InventoryEndpoints.Quantity(
                    totals.TryGetValue(definition.Id, out var total) ? total : ExactQuantity.Zero),
            }),

            packages = packages.Select(package => new
            {
                package.Id,
                package.MedicationDefinitionId,
                package.Ordinal,
                state = package.State.ToString(),
                nominalCapacity = InventoryEndpoints.Quantity(package.NominalCapacity),
                unit = package.Unit.ToString(),
                package.OpenedAt,
                package.ExpiresOn,
                package.AcquiredOn,
                package.LotNumber,
                package.Barcode,
                package.Source,
                package.StorageLocation,
                package.Note,
                package.OwnerPersonId,
                package.HolderPersonId,
            }),

            inventoryLedger = ledger.Select(entry => new
            {
                entry.Id,
                entry.MedicationDefinitionId,
                entry.PackageId,
                entryType = entry.EntryType.ToString(),
                quantity = InventoryEndpoints.Quantity(
                    new ExactQuantity(entry.QuantityNumerator, entry.QuantityDenominator)),
                entry.CorrelationId,
                entry.AdministrationEventId,
                entry.ReversesEntryId,
                entry.ActorAccountId,
                entry.Reason,
                entry.OccurredAt,
                entry.RecordedAt,
            }),

            refillPolicies = refillPolicies.Select(policy => new
            {
                policy.MedicationDefinitionId,
                lowStockThreshold = policy.LowStockThreshold is { } threshold
                    ? InventoryEndpoints.Quantity(threshold)
                    : null,
                policy.LowStockDays,
                policy.NextEligibleRefillOn,
                policy.ExpectedDepletionOn,
                policy.Note,
            }),

            treatmentPlans = plans.Select(plan => new
            {
                plan.Id,
                plan.PersonId,
                plan.MedicationDefinitionId,
                plan.CreatedAt,
                plan.DeletedAt,
            }),

            treatmentPlanVersions = versions.Select(version => new
            {
                version.Id,
                version.TreatmentPlanId,
                version.VersionNumber,
                dose = InventoryEndpoints.Quantity(version.Dose),
                kind = version.Kind.ToString(),
                pattern = version.Pattern.ToString(),
                version.WeekdayMask,
                version.IntervalDays,
                version.DayOfMonth,
                version.IntervalMonths,
                version.EffectiveFrom,
                version.EffectiveTo,
                version.LocalTime,
                version.TimeZoneId,
                dayPeriod = version.DayPeriod?.ToString(),
                mealRelation = version.MealRelation?.ToString(),
                version.MinimumIntervalMinutes,
                version.Instructions,
                version.IsPaused,
                version.CreatedAt,
            }),

            administrations = administrations.Select(e => new
            {
                e.Id,
                e.PersonId,
                e.MedicationDefinitionId,
                e.TreatmentPlanVersionId,
                outcome = e.Outcome.ToString(),
                stockSource = e.StockSource.ToString(),
                plannedQuantity = e.PlannedQuantity is { } planned
                    ? InventoryEndpoints.Quantity(planned)
                    : null,
                actualQuantity = e.ActualQuantity is { } actual
                    ? InventoryEndpoints.Quantity(actual)
                    : null,
                e.ScheduledFor,
                e.OccurredAt,
                e.RecordedAt,
                e.LatenessMinutes,
                e.ActorAccountId,
                e.Note,
            }),

            administrationAllocations = allocations.Select(allocation => new
            {
                allocation.Id,
                allocation.AdministrationEventId,
                allocation.PackageId,
                quantity = InventoryEndpoints.Quantity(
                    new ExactQuantity(allocation.QuantityNumerator, allocation.QuantityDenominator)),
                allocation.LedgerEntryId,
                allocation.CorrelationId,

                // False once a correction moved the draw elsewhere. The row stays, so the
                // exported file shows the same history the product does.
                allocation.IsActive,
                allocation.SupersededByAllocationId,
                allocation.SupersededAt,
                allocation.CreatedAt,
            }),

            allocationCorrections = corrections.Select(correction => new
            {
                correction.Id,
                correction.AdministrationEventId,
                correction.SupersededAllocationId,
                correction.ReplacementAllocationId,
                correction.FromPackageId,
                correction.ToPackageId,
                quantity = InventoryEndpoints.Quantity(
                    new ExactQuantity(correction.QuantityNumerator, correction.QuantityDenominator)),
                correction.CorrelationId,
                correction.ReversalLedgerEntryId,
                correction.ConsumeLedgerEntryId,
                correction.ActorAccountId,
                correction.Reason,
                correction.RecordedAt,
            }),

            inventoryCountBatches = countBatches.Select(batch => new
            {
                batch.Id,
                batch.PreviousBatchId,
                batch.RevisionNumber,
                batch.AccountId,
                batch.AcceptedAt,
            }),

            inventoryCounts = counts.Select(row => new
            {
                row.count.Id,
                row.count.BatchId,
                row.MedicationDefinitionId,
                row.count.PackageId,
                before = InventoryEndpoints.Quantity(row.count.Before),
                observed = InventoryEndpoints.Quantity(row.count.Observed),
                adjustment = InventoryEndpoints.Quantity(row.count.Adjustment),
                row.count.LedgerEntryId,
                row.count.AccountId,
                row.count.AcceptedAt,
            }),
        };
    }

    /// <summary>
    /// Reads at most <see cref="MaximumRowsPerCollection"/> rows and records the
    /// collection's name when there were more.
    /// </summary>
    private static async Task<List<T>> Take<T>(
        IQueryable<T> query,
        string name,
        ISet<string> truncated,
        CancellationToken ct)
    {
        // One row past the cap, so hitting it is detectable without a second count query.
        var rows = await query.Take(MaximumRowsPerCollection + 1).ToListAsync(ct);

        if (rows.Count > MaximumRowsPerCollection)
        {
            rows.RemoveAt(rows.Count - 1);
            truncated.Add(name);
        }

        return rows;
    }
}
