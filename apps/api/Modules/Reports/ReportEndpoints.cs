using MedicationTracker.Api.Application;
using MedicationTracker.Api.Domain.Inventory;
using MedicationTracker.Api.Domain.Quantities;
using MedicationTracker.Api.Domain.Reports;
using MedicationTracker.Api.Domain.Scheduling;
using MedicationTracker.Api.Modules.Inventory;
using MedicationTracker.Api.Modules.Refill;
using MedicationTracker.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedicationTracker.Api.Modules.Reports;

/// <summary>
/// Household-scoped adherence and inventory reporting.
/// </summary>
/// <remarks>
/// <para>
/// Both reports are reads over data the household already owns. Nothing here writes,
/// and nothing here is a clinical statement: the adherence report says what was planned
/// and what was recorded, and the inventory report says how much is left and when it
/// runs out. Neither interprets those numbers, scores the household, or suggests a
/// change to a dose.
/// </para>
/// <para>
/// Acceptance row 31.
/// </para>
/// </remarks>
public static class ReportEndpoints
{
    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api/households/{householdId:guid}/reports");

        api.MapGet("/adherence", async (
            Guid householdId,
            DateOnly? from,
            DateOnly? to,
            string? timeZoneId,
            HttpContext context,
            MedicationTrackerDbContext db,
            CancellationToken ct) =>
        {
            if (!await HouseholdAccess.IsMemberAsync(db, householdId, context, ct))
            {
                return ApiResults.Forbidden();
            }

            if (!TryResolveZone(timeZoneId, out var zone))
            {
                return ApiResults.Invalid("timeZoneId", "unknown_time_zone");
            }

            if (!ReportPeriod.TryResolve(from, to, zone, out var period))
            {
                return ApiResults.Invalid("from", "invalid_period");
            }

            return Results.Ok(await AdherenceAsync(db, householdId, period, ct));
        });

        api.MapGet("/inventory", async (
            Guid householdId,
            DateOnly? asOf,
            HttpContext context,
            MedicationTrackerDbContext db,
            CancellationToken ct) =>
        {
            if (!await HouseholdAccess.IsMemberAsync(db, householdId, context, ct))
            {
                return ApiResults.Forbidden();
            }

            return Results.Ok(await InventoryAsync(db, householdId, asOf, ct));
        });

        return endpoints;
    }

    /// <summary>
    /// Counts planned and recorded doses per person and medication over the period.
    /// </summary>
    /// <remarks>
    /// A dose is placed in the period by when it happened, never by when it was written
    /// down, so one taken on the last day and synced from a phone two days later still
    /// lands in the period it belongs to. The slot it answers is placed separately, for
    /// the reason given on <see cref="AdherenceRecord"/>.
    /// </remarks>
    internal static async Task<object> AdherenceAsync(
        MedicationTrackerDbContext db,
        Guid householdId,
        ReportPeriod period,
        CancellationToken ct)
    {
        var plans = await (from plan in db.TreatmentPlans.AsNoTracking()
                           join version in db.TreatmentPlanVersions.AsNoTracking()
                               on plan.Id equals version.TreatmentPlanId
                           where plan.HouseholdId == householdId
                           select new
                           {
                               plan.Id,
                               plan.PersonId,
                               plan.MedicationDefinitionId,
                               plan.DeletedAt,
                               version.VersionNumber,
                               version.Kind,
                               version.Pattern,
                               version.WeekdayMask,
                               version.IntervalDays,
                               version.DayOfMonth,
                               version.IntervalMonths,
                               version.EffectiveFrom,
                               version.EffectiveTo,
                               version.LocalTime,
                               version.TimeZoneId,
                               version.IsPaused,
                           }).ToListAsync(ct);

        // Either half can put a record in the period: the dose, for the outcome counts,
        // or the slot it answers, so a dose recorded after midnight does not leave the
        // slot it was for looking missed.
        var records = await db.AdministrationEvents.AsNoTracking()
            .Where(e => e.HouseholdId == householdId
                        && ((e.OccurredAt >= period.WindowStart && e.OccurredAt < period.WindowEnd)
                            || (e.ScheduledFor != null
                                && e.ScheduledFor >= period.WindowStart
                                && e.ScheduledFor < period.WindowEnd)))
            .Select(e => new
            {
                e.PersonId,
                e.MedicationDefinitionId,
                e.Outcome,
                e.ScheduledFor,
                e.OccurredAt,
            })
            .ToListAsync(ct);

        // A plan's time zone is stored as text, so an identifier this host does not know
        // must not take the whole report down with it. Such a plan contributes no slots
        // and is reported, which is visible rather than silently wrong.
        var unknownZones = new SortedSet<string>(StringComparer.Ordinal);

        // Deleted plans are included deliberately: a plan stopped yesterday still asked
        // for doses last week, and dropping it would make a finished course look like it
        // was never prescribed.
        var slotsByPair = new Dictionary<(Guid PersonId, Guid MedicationDefinitionId), int>();

        foreach (var group in plans.GroupBy(row => row.Id))
        {
            var first = group.First();
            var slices = new List<PlanVersionSlice>();

            foreach (var row in group)
            {
                if (!TryFindZone(row.TimeZoneId, out var planZone))
                {
                    unknownZones.Add(row.TimeZoneId);
                    continue;
                }

                slices.Add(new PlanVersionSlice(
                    row.VersionNumber,
                    new RecurrenceSpecification(
                        row.Kind, row.Pattern, row.WeekdayMask, row.IntervalDays,
                        row.EffectiveFrom, row.EffectiveTo, row.DayOfMonth, row.IntervalMonths),
                    row.LocalTime,
                    planZone,
                    row.IsPaused));
            }

            // Padded by a day on each side so a plan kept in a different time zone from
            // the report still gets its boundary days considered.
            var slots = ScheduledSlots.Within(
                slices,
                period.From.AddDays(-1),
                period.To.AddDays(1),
                period.WindowStart,
                period.WindowEnd,
                first.DeletedAt);

            if (slots.Count == 0)
            {
                continue;
            }

            var key = (first.PersonId, first.MedicationDefinitionId);
            slotsByPair[key] = slotsByPair.GetValueOrDefault(key) + slots.Count;
        }

        var recordsByPair = records
            .GroupBy(record => (record.PersonId, record.MedicationDefinitionId))
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(record => new AdherenceRecord(
                        record.Outcome,
                        record.ScheduledFor is { } slot && period.Contains(slot) ? slot : null,
                        period.Contains(record.OccurredAt)))
                    .ToList());

        var pairs = slotsByPair.Keys.Union(recordsByPair.Keys).ToList();

        var rows = pairs
            .Select(pair => new
            {
                pair.PersonId,
                pair.MedicationDefinitionId,
                Tally = AdherenceReport.Tally(
                    slotsByPair.GetValueOrDefault(pair),
                    recordsByPair.TryGetValue(pair, out var pairRecords)
                        ? pairRecords
                        : []),
            })
            .Where(row => !row.Tally.IsEmpty)
            .OrderByDescending(row => row.Tally.ScheduledDoses)
            .ThenByDescending(row => row.Tally.RecordedDoses)

            // Tie-broken on the identifiers so reloading the same report cannot reshuffle
            // rows that happen to carry the same counts.
            .ThenBy(row => row.PersonId)
            .ThenBy(row => row.MedicationDefinitionId)
            .ToList();

        return new
        {
            from = period.From,
            to = period.To,
            timeZoneId = period.Zone.Id,
            rows = rows.Select(row => new
            {
                personId = row.PersonId,
                medicationDefinitionId = row.MedicationDefinitionId,
                tally = TallyView(row.Tally),
            }),
            total = TallyView(AdherenceReport.Total(rows.Select(row => row.Tally))),

            // Empty in every normal case; non-empty means a plan's stored zone is not
            // installed on this host and its slots are missing from the numbers above.
            unknownTimeZoneIds = unknownZones,
        };
    }

    /// <summary>
    /// Current holdings per medication, with the depletion forecast each one already
    /// knows how to produce.
    /// </summary>
    internal static async Task<object> InventoryAsync(
        MedicationTrackerDbContext db,
        Guid householdId,
        DateOnly? asOf,
        CancellationToken ct)
    {
        var day = asOf ?? DateOnly.FromDateTime(DateTime.UtcNow);

        var definitions = await db.MedicationDefinitions.AsNoTracking()
            .Where(definition => definition.HouseholdId == householdId)
            .OrderBy(definition => definition.Name)
            .Select(definition => new
            {
                definition.Id,
                definition.Name,
                definition.Strength,
                definition.Unit,
                definition.ArchivedAt,
            })
            .ToListAsync(ct);

        var totals = await InventoryReader.TotalsAsync(db, householdId, ct);

        var packageCounts = await db.Packages.AsNoTracking()
            .Where(package => package.HouseholdId == householdId)
            .Select(package => new { package.MedicationDefinitionId, package.State })
            .ToListAsync(ct);

        var rows = new List<object>(definitions.Count);
        var lowStock = 0;
        var refillGaps = 0;

        foreach (var definition in definitions)
        {
            // One projection per medication. A household holds tens of medications, not
            // thousands, so this stays a small number of reads; if that ever stops being
            // true the forecast is the thing to batch, not the report to narrow.
            var forecast = await RefillEndpoints.ProjectAsync(db, householdId, definition.Id, day, ct);

            if (forecast.IsLowStock)
            {
                lowStock++;
            }

            if (forecast.HasRefillGap)
            {
                refillGaps++;
            }

            rows.Add(new
            {
                medicationDefinitionId = definition.Id,
                name = definition.Name,
                strength = definition.Strength,
                unit = definition.Unit.ToString(),
                isArchived = definition.ArchivedAt != null,
                total = InventoryEndpoints.Quantity(
                    totals.TryGetValue(definition.Id, out var total) ? total : ExactQuantity.Zero),
                packageCount = packageCounts.Count(package =>
                    package.MedicationDefinitionId == definition.Id
                    && package.State is PackageState.Sealed or PackageState.Opened),
                isForecastable = forecast.IsForecastable,
                projectedDepletionOn = forecast.ProjectedDepletionOn,
                daysOfStockRemaining = forecast.DaysOfStockRemaining,
                isLowStock = forecast.IsLowStock,
                lowStockReason = forecast.LowStockTrigger.ToString(),
                nextEligibleRefillOn = forecast.NextEligibleRefillOn,
                hasRefillGap = forecast.HasRefillGap,
                refillGapDays = forecast.RefillGapDays,
            });
        }

        return new
        {
            asOf = day,
            rows,
            lowStockCount = lowStock,
            refillGapCount = refillGaps,
        };
    }

    internal static object TallyView(AdherenceTally tally) => new
    {
        scheduledDoses = tally.ScheduledDoses,
        recordedSlots = tally.RecordedSlots,
        onScheduleDoses = tally.OnScheduleDoses,
        missedDoses = tally.MissedDoses,
        taken = tally.Taken,
        skipped = tally.Skipped,
        partialDoses = tally.PartialDoses,
        extraDoses = tally.ExtraDoses,
        recordedDoses = tally.RecordedDoses,

        // An exact pair rather than a rounded percentage: the client decides how to
        // render it, and null genuinely means "nothing was scheduled" rather than zero.
        onScheduleRatio = tally.OnScheduleRatio is { } ratio
            ? new { numerator = ratio.Numerator, denominator = ratio.Denominator }
            : null,
    };

    private static bool TryResolveZone(string? timeZoneId, out TimeZoneInfo zone)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            zone = TimeZoneInfo.Utc;
            return true;
        }

        return TryFindZone(timeZoneId, out zone);
    }

    private static bool TryFindZone(string timeZoneId, out TimeZoneInfo zone)
    {
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return true;
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            zone = TimeZoneInfo.Utc;
            return false;
        }
    }
}
