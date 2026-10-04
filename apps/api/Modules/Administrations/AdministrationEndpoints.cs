using MedicationTracker.Api.Application;
using MedicationTracker.Api.Domain.Administrations;
using MedicationTracker.Api.Domain.Inventory;
using MedicationTracker.Api.Domain.Quantities;
using MedicationTracker.Api.Domain.Scheduling;
using MedicationTracker.Api.Modules.Inventory;
using MedicationTracker.Api.Modules.Treatments;
using MedicationTracker.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedicationTracker.Api.Modules.Administrations;

/// <summary>
/// The daily flow: what is due, recording what happened, and correcting which package
/// paid for it.
/// </summary>
public static class AdministrationEndpoints
{
    public static IEndpointRouteBuilder MapAdministrationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api/households/{householdId:guid}");

        api.MapGet("/today", async (
            Guid householdId,
            DateOnly? date,
            HttpContext context,
            MedicationTrackerDbContext db,
            CancellationToken ct) =>
        {
            if (!await HouseholdAccess.IsMemberAsync(db, householdId, context, ct))
            {
                return ApiResults.Forbidden();
            }

            var day = date ?? DateOnly.FromDateTime(DateTime.UtcNow);
            var versions = await CurrentVersionsAsync(db, householdId, day, ct);
            var totals = await InventoryReader.TotalsAsync(db, householdId, ct);

            var due = new List<object>();
            foreach (var row in versions)
            {
                if (!row.Version.IsDueOn(day))
                {
                    continue;
                }

                var zone = TimeZoneInfo.FindSystemTimeZoneById(row.Version.TimeZoneId);
                var scheduledFor = row.Version.Kind == TreatmentKind.Scheduled
                    ? RecurrenceRule.ScheduledInstant(day, row.Version.LocalTime, zone)
                    : (DateTimeOffset?)null;

                var recorded = scheduledFor is { } slot
                    ? await db.AdministrationEvents.AsNoTracking().FirstOrDefaultAsync(
                        e => e.HouseholdId == householdId
                             && e.TreatmentPlanVersionId == row.Version.Id
                             && e.ScheduledFor == slot
                             && e.Outcome != AdministrationOutcome.ExtraDose, ct)
                    : null;

                var available = totals.TryGetValue(row.Plan.MedicationDefinitionId, out var total)
                    ? total
                    : ExactQuantity.Zero;

                due.Add(new
                {
                    planId = row.Plan.Id,
                    planVersionId = row.Version.Id,
                    personId = row.Plan.PersonId,
                    medicationDefinitionId = row.Plan.MedicationDefinitionId,
                    dose = InventoryEndpoints.Quantity(row.Version.Dose),
                    kind = row.Version.Kind.ToString(),
                    localTime = row.Version.LocalTime,
                    dayPeriod = row.Version.DayPeriod?.ToString(),
                    mealRelation = row.Version.MealRelation?.ToString(),
                    scheduledFor,
                    availableTotal = InventoryEndpoints.Quantity(available),

                    // Enough to show a warning before the user taps, without making the
                    // user think about packages.
                    hasEnoughStock = available >= row.Version.Dose,
                    recordedOutcome = recorded?.Outcome.ToString(),
                    recordedAdministrationId = recorded?.Id,
                });
            }

            return Results.Ok(new { date = day, due });
        });

        api.MapPost("/administrations", async (
            Guid householdId,
            RecordAdministrationRequest request,
            HttpContext context,
            MedicationTrackerDbContext db,
            CancellationToken ct) =>
        {
            if (!await HouseholdAccess.IsMemberAsync(db, householdId, context, ct))
            {
                return ApiResults.Forbidden();
            }

            if (request.IdempotencyKey is { Length: > 100 })
            {
                return ApiResults.Invalid("idempotencyKey", "too_long");
            }

            if (!Enum.TryParse<AdministrationOutcome>(request.Outcome, ignoreCase: true, out var outcome))
            {
                return ApiResults.Invalid("outcome", "invalid");
            }

            if (!Enum.TryParse<DoseSourceSelection>(request.Source, ignoreCase: true, out var source))
            {
                return ApiResults.Invalid("source", "invalid");
            }

            var resolved = await ResolveTargetAsync(db, householdId, request, ct);
            if (resolved.Error is { } error)
            {
                return error;
            }

            var consumes = AdministrationOutcomes.ConsumesStock(outcome);
            ExactQuantity? actual = null;

            if (consumes)
            {
                // The amount defaults to the plan's dose, so the one-tap path sends no
                // quantity at all; a partial or extra dose sends its own.
                if (request.ActualQuantityNumerator is { } numerator)
                {
                    if (!ExactQuantity.TryCreatePositive(
                            numerator, request.ActualQuantityDenominator ?? 1, out var parsed))
                    {
                        return ApiResults.Invalid("actualQuantity", "invalid");
                    }

                    actual = parsed;
                }
                else if (resolved.Version is { } planned)
                {
                    actual = planned.Dose;
                }
                else
                {
                    return ApiResults.Invalid("actualQuantity", "required");
                }
            }

            var accountId = HouseholdAccess.RequireAccountId(context);

            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await InventoryReader.LockHouseholdAsync(db, householdId, ct);

            var command = new RecordDoseCommand(
                householdId,
                resolved.PersonId,
                resolved.MedicationDefinitionId,
                resolved.Version?.Id,
                outcome,
                resolved.Version?.Dose,
                actual,
                resolved.ScheduledFor,
                request.OccurredAt ?? DateTimeOffset.UtcNow,
                source,
                request.PackageId,
                accountId,
                request.Note,
                request.AdministrationId);

            var (refusal, result) = await AdministrationService.RecordAsync(
                db, command, request.IdempotencyKey, ct);

            if (refusal != RecordDoseRefusal.None)
            {
                return ApiResults.Conflict(Code(refusal));
            }

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return Results.Ok(new
            {
                administrationEventId = result!.AdministrationEventId,
                replayed = result.Replayed,
                stockSource = result.StockSource.ToString(),
                allocations = result.Allocations.Select(allocation => new
                {
                    allocation.AllocationId,
                    allocation.PackageId,
                    packageLabel = allocation.PackageOrdinal,
                    quantity = InventoryEndpoints.Quantity(allocation.Quantity),
                }),
            });
        });

        api.MapGet("/administrations/{administrationId:guid}/allocations", async (
            Guid householdId,
            Guid administrationId,
            HttpContext context,
            MedicationTrackerDbContext db,
            CancellationToken ct) =>
        {
            if (!await HouseholdAccess.IsMemberAsync(db, householdId, context, ct))
            {
                return ApiResults.Forbidden();
            }

            var administration = await db.AdministrationEvents.AsNoTracking().SingleOrDefaultAsync(
                e => e.Id == administrationId && e.HouseholdId == householdId, ct);

            if (administration is null)
            {
                return Results.NotFound();
            }

            var allocations = await db.AdministrationAllocations.AsNoTracking()
                .Where(allocation => allocation.AdministrationEventId == administrationId)
                .OrderBy(allocation => allocation.CreatedAt)
                .ToListAsync(ct);

            var ordinals = await db.Packages.AsNoTracking()
                .Where(package => package.MedicationDefinitionId == administration.MedicationDefinitionId)
                .Select(package => new { package.Id, package.Ordinal })
                .ToDictionaryAsync(package => package.Id, package => package.Ordinal, ct);

            var corrections = await db.AllocationCorrections.AsNoTracking()
                .Where(correction => correction.AdministrationEventId == administrationId)
                .OrderBy(correction => correction.RecordedAt)
                .ToListAsync(ct);

            return Results.Ok(new
            {
                administrationId,
                stockSource = administration.StockSource.ToString(),
                actualQuantity = administration.ActualQuantity is { } actual
                    ? InventoryEndpoints.Quantity(actual)
                    : null,
                allocations = allocations.Select(allocation => new
                {
                    allocation.Id,
                    allocation.PackageId,
                    packageLabel = Label(ordinals, allocation.PackageId),
                    quantity = InventoryEndpoints.Quantity(allocation.Quantity),
                    allocation.IsActive,
                    allocation.SupersededByAllocationId,
                }),
                corrections = corrections.Select(correction => new
                {
                    correction.Id,
                    fromPackageLabel = Label(ordinals, correction.FromPackageId),
                    toPackageLabel = Label(ordinals, correction.ToPackageId),
                    quantity = InventoryEndpoints.Quantity(correction.Quantity),
                    correction.Reason,
                    correction.RecordedAt,
                }),
            });
        });

        // The owner-critical correction: "I actually used Box 2".
        api.MapPost("/administrations/{administrationId:guid}/allocations/{allocationId:guid}/correction", async (
            Guid householdId,
            Guid administrationId,
            Guid allocationId,
            CorrectAllocationRequest request,
            HttpContext context,
            MedicationTrackerDbContext db,
            CancellationToken ct) =>
        {
            if (!await HouseholdAccess.IsMemberAsync(db, householdId, context, ct))
            {
                return ApiResults.Forbidden();
            }

            if (!Enum.TryParse<DoseSourceSelection>(request.Target, ignoreCase: true, out var target)
                || target == DoseSourceSelection.Automatic
                || target == DoseSourceSelection.UntrackedExternal)
            {
                return ApiResults.Invalid("target", "invalid");
            }

            var accountId = HouseholdAccess.RequireAccountId(context);

            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await InventoryReader.LockHouseholdAsync(db, householdId, ct);

            var (outcome, refusal, result) = await AdministrationService.CorrectAllocationAsync(
                db,
                new CorrectAllocationCommand(
                    householdId, administrationId, allocationId, target,
                    request.PackageId, accountId, request.Reason),
                ct);

            if (outcome != CorrectAllocationOutcome.None)
            {
                return Results.NotFound();
            }

            if (refusal != AllocationCorrectionRefusal.None)
            {
                return ApiResults.Conflict(Code(refusal));
            }

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return Results.Ok(new
            {
                allocationId = result!.AllocationId,
                result.PackageId,
                packageLabel = result.PackageOrdinal,
                quantity = InventoryEndpoints.Quantity(result.Quantity),
            });
        });

        return endpoints;
    }

    /// <summary>
    /// Resolves what the dose is for. A plan version supplies the person, medication,
    /// dose and scheduled slot; a fully unplanned dose names the person and medication
    /// directly.
    /// </summary>
    private static async Task<ResolvedTarget> ResolveTargetAsync(
        MedicationTrackerDbContext db,
        Guid householdId,
        RecordAdministrationRequest request,
        CancellationToken ct)
    {
        if (request.PlanVersionId is { } versionId)
        {
            var row = await (from version in db.TreatmentPlanVersions.AsNoTracking()
                             join plan in db.TreatmentPlans.AsNoTracking()
                                 on version.TreatmentPlanId equals plan.Id
                             join definition in db.MedicationDefinitions.AsNoTracking()
                                 on plan.MedicationDefinitionId equals definition.Id
                             where version.Id == versionId
                                   && plan.HouseholdId == householdId
                                   && plan.DeletedAt == null
                                   && definition.ArchivedAt == null
                             select new { version, plan }).SingleOrDefaultAsync(ct);

            if (row is null)
            {
                return new ResolvedTarget(Error: Results.NotFound());
            }

            var zone = TimeZoneInfo.FindSystemTimeZoneById(row.version.TimeZoneId);
            DateTimeOffset? scheduledFor = null;

            if (row.version.Kind == TreatmentKind.Scheduled && request.ScheduledFor is { } requested)
            {
                var localDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(requested, zone).DateTime);

                // A scheduled dose must land on a day the plan is actually due, and on
                // the slot the plan defines for that day, so a client cannot invent one.
                if (!row.version.IsDueOn(localDay)
                    || RecurrenceRule.ScheduledInstant(localDay, row.version.LocalTime, zone)
                    != requested.ToUniversalTime())
                {
                    return new ResolvedTarget(Error: ApiResults.Invalid("scheduledFor", "not_due"));
                }

                // A newer version covering that day supersedes this one for writes too.
                var superseded = await db.TreatmentPlanVersions.AsNoTracking().AnyAsync(
                    candidate => candidate.TreatmentPlanId == row.plan.Id
                                 && candidate.VersionNumber > row.version.VersionNumber
                                 && (candidate.EffectiveFrom == null || candidate.EffectiveFrom <= localDay)
                                 && (candidate.EffectiveTo == null || candidate.EffectiveTo >= localDay),
                    ct);

                if (superseded)
                {
                    return new ResolvedTarget(Error: ApiResults.Invalid("planVersionId", "superseded"));
                }

                scheduledFor = requested.ToUniversalTime();
            }

            return new ResolvedTarget(
                row.plan.PersonId, row.plan.MedicationDefinitionId, row.version, scheduledFor);
        }

        if (request.PersonId is not { } personId || request.MedicationDefinitionId is not { } definitionId)
        {
            return new ResolvedTarget(Error: ApiResults.Invalid("planVersionId", "required"));
        }

        var exists = await db.People.AsNoTracking().AnyAsync(
                         person => person.Id == personId && person.HouseholdId == householdId, ct)
                     && await db.MedicationDefinitions.AsNoTracking().AnyAsync(
                         definition => definition.Id == definitionId && definition.HouseholdId == householdId, ct);

        return exists
            ? new ResolvedTarget(personId, definitionId, null, null)
            : new ResolvedTarget(Error: Results.NotFound());
    }

    private static async Task<List<(TreatmentPlan Plan, TreatmentPlanVersion Version)>> CurrentVersionsAsync(
        MedicationTrackerDbContext db,
        Guid householdId,
        DateOnly day,
        CancellationToken ct)
    {
        var rows = await (from plan in db.TreatmentPlans.AsNoTracking()
                          join version in db.TreatmentPlanVersions.AsNoTracking()
                              on plan.Id equals version.TreatmentPlanId
                          join definition in db.MedicationDefinitions.AsNoTracking()
                              on plan.MedicationDefinitionId equals definition.Id
                          join person in db.People.AsNoTracking()
                              on plan.PersonId equals person.Id
                          where plan.HouseholdId == householdId
                                && plan.DeletedAt == null
                                && definition.ArchivedAt == null

                                // Archiving a person pauses their plans, so this is belt
                                // and braces — but it is the half that works on data
                                // archived before that cascade existed, with no backfill.
                                && person.ArchivedAt == null
                                && (version.EffectiveFrom == null || version.EffectiveFrom <= day)
                                && (version.EffectiveTo == null || version.EffectiveTo >= day)
                          select new { plan, version }).ToListAsync(ct);

        // The highest version number covering the day wins, so an edit takes effect
        // without disturbing versions that governed earlier days.
        //
        // The pause is applied AFTER that choice, never as a filter in the query. Filtering
        // it out earlier would let an older, unpaused version win the group and quietly
        // resurrect the schedule the household just set aside.
        return rows
            .GroupBy(row => row.plan.Id)
            .Select(group => group.OrderByDescending(row => row.version.VersionNumber).First())
            .Where(row => !row.version.IsPaused)
            .Select(row => (row.plan, row.version))
            .ToList();
    }

    private static int? Label(IReadOnlyDictionary<Guid, int> ordinals, Guid? packageId) =>
        packageId is { } id && ordinals.TryGetValue(id, out var ordinal) ? ordinal : null;

    private static string Code(RecordDoseRefusal refusal) => refusal switch
    {
        RecordDoseRefusal.InsufficientStock => "insufficient_stock",
        RecordDoseRefusal.PackageNotFound => "package_not_found",
        RecordDoseRefusal.ChosenSourceInsufficient => "chosen_source_insufficient",
        RecordDoseRefusal.PackageNotSpecified => "package_not_specified",
        RecordDoseRefusal.PackageNotEligible => "package_not_eligible",
        RecordDoseRefusal.SlotAlreadyRecorded => "slot_already_recorded",
        _ => "refused",
    };

    private static string Code(AllocationCorrectionRefusal refusal) => refusal switch
    {
        AllocationCorrectionRefusal.UnknownAllocation => "unknown_allocation",
        AllocationCorrectionRefusal.AllocationNotActive => "allocation_not_active",
        AllocationCorrectionRefusal.SameSource => "same_source",
        AllocationCorrectionRefusal.TargetHasInsufficientStock => "target_insufficient_stock",
        AllocationCorrectionRefusal.TargetNotEligible => "target_not_eligible",
        AllocationCorrectionRefusal.AdministrationIsUntracked => "administration_untracked",
        _ => "refused",
    };

    private sealed record ResolvedTarget(
        Guid PersonId = default,
        Guid MedicationDefinitionId = default,
        TreatmentPlanVersion? Version = null,
        DateTimeOffset? ScheduledFor = null,
        IResult? Error = null);
}

/// <summary>
/// Recording a dose.
/// </summary>
/// <remarks>
/// The everyday call is a plan version, an outcome of <c>Taken</c>, and nothing else:
/// the amount comes from the plan and the package comes from the policy. Every other
/// field exists for the advanced paths — a partial amount, an extra unplanned dose, a
/// specific package, loose stock, or a dose taken from stock the household does not
/// track.
/// </remarks>
public sealed record RecordAdministrationRequest(
    Guid? PlanVersionId = null,
    Guid? PersonId = null,
    Guid? MedicationDefinitionId = null,
    string Outcome = "Taken",
    string Source = "Automatic",
    Guid? PackageId = null,
    long? ActualQuantityNumerator = null,
    long? ActualQuantityDenominator = null,
    DateTimeOffset? ScheduledFor = null,
    DateTimeOffset? OccurredAt = null,
    string? IdempotencyKey = null,
    Guid? AdministrationId = null,
    string? Note = null);

public sealed record CorrectAllocationRequest(
    string Target = "SpecificPackage",
    Guid? PackageId = null,
    string? Reason = null);
