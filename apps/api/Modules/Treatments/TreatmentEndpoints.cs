using System.Text.Json;
using MedicationTracker.Api.Application;
using MedicationTracker.Api.Domain.Quantities;
using MedicationTracker.Api.Domain.Scheduling;
using MedicationTracker.Api.Modules.Audit;
using MedicationTracker.Api.Modules.Sync;
using MedicationTracker.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedicationTracker.Api.Modules.Treatments;

/// <summary>
/// Treatment plans and their effective-dated versions.
/// </summary>
/// <remarks>
/// Editing a plan appends a version rather than changing one, so a change today cannot
/// rewrite what the plan said when a past dose was recorded.
/// </remarks>
public static class TreatmentEndpoints
{
    public static IEndpointRouteBuilder MapTreatmentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api/households/{householdId:guid}/plans");

        api.MapPost("/", async (
            Guid householdId,
            TreatmentPlanRequest request,
            HttpContext context,
            MedicationTrackerDbContext db,
            CancellationToken ct) =>
        {
            if (!await HouseholdAccess.IsMemberAsync(db, householdId, context, ct))
            {
                return ApiResults.Forbidden();
            }

            if (!TryValidate(request, out var parsed, out var field))
            {
                return ApiResults.Invalid(field, "invalid");
            }

            if (!await db.People.AsNoTracking().AnyAsync(
                    person => person.Id == request.PersonId && person.HouseholdId == householdId, ct))
            {
                return ApiResults.Invalid("personId", "unknown_person");
            }

            if (!await db.MedicationDefinitions.AsNoTracking().AnyAsync(
                    definition => definition.Id == request.MedicationDefinitionId
                                  && definition.HouseholdId == householdId
                                  && definition.ArchivedAt == null, ct))
            {
                return ApiResults.Invalid("medicationDefinitionId", "unknown_medication");
            }

            if (!SyncReplay.IsValidKey(request.IdempotencyKey))
            {
                return ApiResults.Invalid("idempotencyKey", "invalid");
            }

            var accountId = HouseholdAccess.RequireAccountId(context);
            var now = DateTimeOffset.UtcNow;

            // Created offline on a phone under its own id and sent later, perhaps twice:
            // the receipt answers the second send with the plan already created.
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await InventoryReader.LockHouseholdAsync(db, householdId, ct);

            if (await SyncReplay.PriorAsync(db, householdId, request.IdempotencyKey, ct) is { } prior)
            {
                var priorVersion = await db.TreatmentPlanVersions.AsNoTracking()
                    .Where(candidate => candidate.TreatmentPlanId == prior.ResultEntityId)
                    .OrderByDescending(candidate => candidate.VersionNumber)
                    .Select(candidate => (Guid?)candidate.Id)
                    .FirstOrDefaultAsync(ct);

                return Results.Ok(new { id = prior.ResultEntityId, versionId = priorVersion, replayed = true });
            }

            var planId = SyncReplay.ClientId(request.Id) ?? Guid.CreateVersion7();
            if (await db.TreatmentPlans.AsNoTracking().AnyAsync(candidate => candidate.Id == planId, ct))
            {
                return ApiResults.Conflict("id_in_use");
            }

            var plan = new TreatmentPlan(planId, householdId, request.PersonId, request.MedicationDefinitionId, now);

            var version = new TreatmentPlanVersion(
                Guid.CreateVersion7(), plan.Id, 1, parsed.Dose, parsed.Recurrence, request.LocalTime,
                request.TimeZoneId, now, accountId, parsed.DayPeriod, parsed.MealRelation,
                request.MinimumIntervalMinutes, request.Instructions);

            db.TreatmentPlans.Add(plan);
            db.TreatmentPlanVersions.Add(version);
            db.TreatmentPlanChangeEvents.Add(new TreatmentPlanChangeEvent(
                Guid.CreateVersion7(), householdId, plan.Id, accountId,
                ChangeKind.Created, null, Snapshot(plan, version), now));
            SyncReplay.Record(db, householdId, accountId, request.IdempotencyKey, "plans.create", plan.Id, now);

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return Results.Created(
                $"/api/households/{householdId}/plans/{plan.Id}",
                new { plan.Id, versionId = version.Id, replayed = false });
        });

        api.MapPut("/{planId:guid}", async (
            Guid householdId,
            Guid planId,
            TreatmentPlanRequest request,
            HttpContext context,
            MedicationTrackerDbContext db,
            CancellationToken ct) =>
        {
            if (!await HouseholdAccess.IsMemberAsync(db, householdId, context, ct))
            {
                return ApiResults.Forbidden();
            }

            if (!TryValidate(request, out var parsed, out var field))
            {
                return ApiResults.Invalid(field, "invalid");
            }

            var plan = await db.TreatmentPlans.SingleOrDefaultAsync(
                candidate => candidate.Id == planId
                             && candidate.HouseholdId == householdId
                             && candidate.DeletedAt == null, ct);

            if (plan is null)
            {
                return Results.NotFound();
            }

            var latest = await db.TreatmentPlanVersions.AsNoTracking()
                .Where(version => version.TreatmentPlanId == planId)
                .OrderByDescending(version => version.VersionNumber)
                .FirstAsync(ct);

            // A new version may not start before the one it replaces, or history would
            // change retroactively.
            if (parsed.Recurrence.EffectiveFrom is { } from
                && latest.EffectiveFrom is { } previousFrom
                && from < previousFrom)
            {
                return ApiResults.Invalid("effectiveFrom", "before_current_version");
            }

            var accountId = HouseholdAccess.RequireAccountId(context);
            var now = DateTimeOffset.UtcNow;
            var before = Snapshot(plan, latest);

            plan.Reassign(request.PersonId, request.MedicationDefinitionId);

            // The pause rides forward. Editing the dose of a plan you have set aside is not
            // a statement that you have started taking it again — resuming is its own act.
            var version = new TreatmentPlanVersion(
                Guid.CreateVersion7(), plan.Id, latest.VersionNumber + 1, parsed.Dose, parsed.Recurrence,
                request.LocalTime, request.TimeZoneId, now, accountId, parsed.DayPeriod, parsed.MealRelation,
                request.MinimumIntervalMinutes, request.Instructions, latest.IsPaused);

            db.TreatmentPlanVersions.Add(version);
            db.TreatmentPlanChangeEvents.Add(new TreatmentPlanChangeEvent(
                Guid.CreateVersion7(), householdId, plan.Id, accountId,
                ChangeKind.VersionAppended, before, Snapshot(plan, version), now));

            await db.SaveChangesAsync(ct);
            return Results.Ok(new { versionId = version.Id, versionNumber = version.VersionNumber });
        });

        // Pausing and resuming are the same operation with a different answer, so they
        // share one route. Both append a version rather than mutating one: the days before
        // a pause were really governed by the schedule, and the days inside it really were
        // not, and an effective-dated chain is how this product records that distinction
        // everywhere else.
        api.MapPost("/{planId:guid}/paused", async (
            Guid householdId,
            Guid planId,
            SetPlanPausedRequest request,
            HttpContext context,
            MedicationTrackerDbContext db,
            CancellationToken ct) =>
        {
            if (!await HouseholdAccess.IsMemberAsync(db, householdId, context, ct))
            {
                return ApiResults.Forbidden();
            }

            var plan = await db.TreatmentPlans.SingleOrDefaultAsync(
                candidate => candidate.Id == planId
                             && candidate.HouseholdId == householdId
                             && candidate.DeletedAt == null, ct);

            if (plan is null)
            {
                return Results.NotFound();
            }

            var latest = await db.TreatmentPlanVersions.AsNoTracking()
                .Where(version => version.TreatmentPlanId == planId)
                .OrderByDescending(version => version.VersionNumber)
                .FirstAsync(ct);

            // Pausing twice is not an error and must not append a second identical version,
            // which would make the history read as two separate decisions.
            if (latest.IsPaused == request.IsPaused)
            {
                return Results.Ok(new
                {
                    versionId = latest.Id,
                    versionNumber = latest.VersionNumber,
                    isPaused = latest.IsPaused,
                });
            }

            var accountId = HouseholdAccess.RequireAccountId(context);
            var now = DateTimeOffset.UtcNow;
            var before = Snapshot(plan, latest);

            // Everything else is copied verbatim. A pause says nothing about the dose, the
            // days or the times, and resuming must bring back exactly the plan that was set
            // aside rather than some reconstruction of it.
            // The pause starts on the day it was decided. Copying the current version's
            // start as well would let the paused version govern the days before the pause
            // too, and the adherence replay would then read those days as having asked for
            // nothing — a pause that quietly erased the doses the household did take.
            var version = new TreatmentPlanVersion(
                Guid.CreateVersion7(),
                plan.Id,
                latest.VersionNumber + 1,
                latest.Dose,
                latest.Recurrence with { EffectiveFrom = VersionStartFor(latest, now) },
                latest.LocalTime,
                latest.TimeZoneId,
                now,
                accountId,
                latest.DayPeriod,
                latest.MealRelation,
                latest.MinimumIntervalMinutes,
                latest.Instructions,
                request.IsPaused);

            db.TreatmentPlanVersions.Add(version);
            db.TreatmentPlanChangeEvents.Add(new TreatmentPlanChangeEvent(
                Guid.CreateVersion7(), householdId, plan.Id, accountId,
                ChangeKind.VersionAppended, before, Snapshot(plan, version), now));

            await db.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                versionId = version.Id,
                versionNumber = version.VersionNumber,
                isPaused = version.IsPaused,
            });
        });

        // Ending a plan is a decision with a date, recorded the way every other decision
        // about a plan is: as an appended version. This one copies the current version and
        // closes it on the last day of doses, so the days up to and including that day keep
        // exactly what they asked for and the days after it ask for nothing. The plan
        // itself stays — listed under past plans, and restartable. The old "end" was a soft
        // delete with no way back, which the owner met on the live site as a plan that had
        // simply vanished.
        api.MapPost("/{planId:guid}/end", async (
            Guid householdId,
            Guid planId,
            EndPlanRequest? request,
            HttpContext context,
            MedicationTrackerDbContext db,
            CancellationToken ct) =>
        {
            if (!await HouseholdAccess.IsMemberAsync(db, householdId, context, ct))
            {
                return ApiResults.Forbidden();
            }

            var plan = await db.TreatmentPlans.SingleOrDefaultAsync(
                candidate => candidate.Id == planId
                             && candidate.HouseholdId == householdId
                             && candidate.DeletedAt == null, ct);

            if (plan is null)
            {
                return Results.NotFound();
            }

            var latest = await db.TreatmentPlanVersions.AsNoTracking()
                .Where(version => version.TreatmentPlanId == planId)
                .OrderByDescending(version => version.VersionNumber)
                .FirstAsync(ct);

            var now = DateTimeOffset.UtcNow;
            var endsOn = request?.EndsOn ?? TodayIn(latest.TimeZoneId, now);

            if (latest.EffectiveFrom is { } from && endsOn < from)
            {
                return ApiResults.Invalid("endsOn", "before_current_version");
            }

            // Ending on the same day twice is one decision, not two.
            if (latest.EffectiveTo == endsOn)
            {
                return Results.Ok(new
                {
                    versionId = latest.Id,
                    versionNumber = latest.VersionNumber,
                    effectiveTo = latest.EffectiveTo,
                });
            }

            var accountId = HouseholdAccess.RequireAccountId(context);
            var before = Snapshot(plan, latest);

            // Everything is copied verbatim, the pause included: a plan ended while paused
            // must not have its paused days re-read as doses the household skipped.
            var version = new TreatmentPlanVersion(
                Guid.CreateVersion7(),
                plan.Id,
                latest.VersionNumber + 1,
                latest.Dose,
                latest.Recurrence with { EffectiveTo = endsOn },
                latest.LocalTime,
                latest.TimeZoneId,
                now,
                accountId,
                latest.DayPeriod,
                latest.MealRelation,
                latest.MinimumIntervalMinutes,
                latest.Instructions,
                latest.IsPaused);

            db.TreatmentPlanVersions.Add(version);
            db.TreatmentPlanChangeEvents.Add(new TreatmentPlanChangeEvent(
                Guid.CreateVersion7(), householdId, plan.Id, accountId,
                ChangeKind.VersionAppended, before, Snapshot(plan, version), now));

            await db.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                versionId = version.Id,
                versionNumber = version.VersionNumber,
                effectiveTo = version.EffectiveTo,
            });
        });

        // Restarting appends a version that begins on the restart day, open-ended and not
        // paused, with the same dose and pattern. The days between the end and the restart
        // stay governed by the ended version and ask for nothing: they were never owed, and
        // bringing the plan back must not turn them into missed doses after the fact.
        api.MapPost("/{planId:guid}/restart", async (
            Guid householdId,
            Guid planId,
            RestartPlanRequest? request,
            HttpContext context,
            MedicationTrackerDbContext db,
            CancellationToken ct) =>
        {
            if (!await HouseholdAccess.IsMemberAsync(db, householdId, context, ct))
            {
                return ApiResults.Forbidden();
            }

            var plan = await db.TreatmentPlans.SingleOrDefaultAsync(
                candidate => candidate.Id == planId
                             && candidate.HouseholdId == householdId
                             && candidate.DeletedAt == null, ct);

            if (plan is null)
            {
                return Results.NotFound();
            }

            var latest = await db.TreatmentPlanVersions.AsNoTracking()
                .Where(version => version.TreatmentPlanId == planId)
                .OrderByDescending(version => version.VersionNumber)
                .FirstAsync(ct);

            var now = DateTimeOffset.UtcNow;
            var startsOn = request?.StartsOn ?? TodayIn(latest.TimeZoneId, now);

            // A phone replaying a restart whose answer never arrived finds the plan already
            // restarted on that day: one decision, not two, and not a refusal.
            if (latest.EffectiveTo is null && latest.EffectiveFrom == startsOn && !latest.IsPaused)
            {
                return Results.Ok(new
                {
                    versionId = latest.Id,
                    versionNumber = latest.VersionNumber,
                    effectiveFrom = latest.EffectiveFrom,
                });
            }

            if (latest.EffectiveTo is not { } endedOn)
            {
                return ApiResults.Conflict("plan_not_ended");
            }

            if (startsOn <= endedOn)
            {
                return ApiResults.Invalid("startsOn", "before_end");
            }

            var accountId = HouseholdAccess.RequireAccountId(context);
            var before = Snapshot(plan, latest);

            var version = new TreatmentPlanVersion(
                Guid.CreateVersion7(),
                plan.Id,
                latest.VersionNumber + 1,
                latest.Dose,
                latest.Recurrence with { EffectiveFrom = startsOn, EffectiveTo = null },
                latest.LocalTime,
                latest.TimeZoneId,
                now,
                accountId,
                latest.DayPeriod,
                latest.MealRelation,
                latest.MinimumIntervalMinutes,
                latest.Instructions,
                isPaused: false);

            db.TreatmentPlanVersions.Add(version);
            db.TreatmentPlanChangeEvents.Add(new TreatmentPlanChangeEvent(
                Guid.CreateVersion7(), householdId, plan.Id, accountId,
                ChangeKind.VersionAppended, before, Snapshot(plan, version), now));

            await db.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                versionId = version.Id,
                versionNumber = version.VersionNumber,
                effectiveFrom = version.EffectiveFrom,
            });
        });

        // Deleting remains for a plan created by mistake. It is not how a plan ends.
        api.MapDelete("/{planId:guid}", async (
            Guid householdId,
            Guid planId,
            HttpContext context,
            MedicationTrackerDbContext db,
            CancellationToken ct) =>
        {
            if (!await HouseholdAccess.IsMemberAsync(db, householdId, context, ct))
            {
                return ApiResults.Forbidden();
            }

            var plan = await db.TreatmentPlans.SingleOrDefaultAsync(
                candidate => candidate.Id == planId
                             && candidate.HouseholdId == householdId
                             && candidate.DeletedAt == null, ct);

            if (plan is null)
            {
                return Results.NotFound();
            }

            var now = DateTimeOffset.UtcNow;
            plan.Delete(now);

            db.TreatmentPlanChangeEvents.Add(new TreatmentPlanChangeEvent(
                Guid.CreateVersion7(), householdId, plan.Id, HouseholdAccess.RequireAccountId(context),
                ChangeKind.Deleted, null, null, now));

            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        return endpoints;
    }

    /// <summary>
    /// The first day a version appended now governs: today in the plan's own zone, or the
    /// current version's start when that is still ahead. A pause, an end or a restart is
    /// a decision made on a day, and must not reach back over the days before it.
    /// </summary>
    internal static DateOnly VersionStartFor(TreatmentPlanVersion latest, DateTimeOffset now)
    {
        var today = TodayIn(latest.TimeZoneId, now);
        return latest.EffectiveFrom is { } from && from > today ? from : today;
    }

    /// <summary>Today's calendar date in a plan's zone; UTC when the zone is unknown here.</summary>
    internal static DateOnly TodayIn(string timeZoneId, DateTimeOffset now)
    {
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        }
        catch (TimeZoneNotFoundException)
        {
            return DateOnly.FromDateTime(now.UtcDateTime);
        }
    }

    internal static bool TryValidate(TreatmentPlanRequest request, out ParsedPlan parsed, out string field)
    {
        parsed = default;
        field = "dose";

        if (request is null
            || !ExactQuantity.TryCreatePositive(request.DoseNumerator, request.DoseDenominator, out var dose))
        {
            return false;
        }

        field = "timeZoneId";
        if (string.IsNullOrWhiteSpace(request.TimeZoneId))
        {
            return false;
        }

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(request.TimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }

        field = "kind";
        if (!Enum.TryParse<TreatmentKind>(request.Kind, ignoreCase: true, out var kind))
        {
            return false;
        }

        field = "pattern";
        if (!Enum.TryParse<RecurrencePattern>(request.Pattern, ignoreCase: true, out var pattern))
        {
            return false;
        }

        field = "dayPeriod";
        DayPeriod? dayPeriod = null;
        if (request.DayPeriod is not null)
        {
            if (!Enum.TryParse<DayPeriod>(request.DayPeriod, ignoreCase: true, out var parsedPeriod))
            {
                return false;
            }

            dayPeriod = parsedPeriod;
        }

        field = "mealRelation";
        MealRelation? mealRelation = null;
        if (request.MealRelation is not null)
        {
            if (!Enum.TryParse<MealRelation>(request.MealRelation, ignoreCase: true, out var parsedMeal))
            {
                return false;
            }

            mealRelation = parsedMeal;
        }

        var recurrence = new RecurrenceSpecification(
            kind, pattern, request.WeekdayMask, request.IntervalDays, request.EffectiveFrom, request.EffectiveTo,
            request.DayOfMonth, request.IntervalMonths);

        field = "recurrence";
        if (!RecurrenceRule.IsValid(recurrence))
        {
            return false;
        }

        field = "localTime";
        if (kind == TreatmentKind.Scheduled && request.LocalTime is null && dayPeriod is null)
        {
            return false;
        }

        field = "minimumIntervalMinutes";
        if (request.MinimumIntervalMinutes is < 0 or > 60 * 24 * 30)
        {
            return false;
        }

        field = string.Empty;
        parsed = new ParsedPlan(dose, recurrence, dayPeriod, mealRelation);
        return true;
    }

    internal static string Snapshot(TreatmentPlan plan, TreatmentPlanVersion version) => JsonSerializer.Serialize(new
    {
        plan.PersonId,
        plan.MedicationDefinitionId,
        Dose = version.Dose.ToString(),
        Kind = version.Kind.ToString(),
        Pattern = version.Pattern.ToString(),
        version.WeekdayMask,
        version.IntervalDays,
        version.DayOfMonth,
        version.IntervalMonths,
        version.EffectiveFrom,
        version.EffectiveTo,
        version.LocalTime,
        version.TimeZoneId,
        DayPeriod = version.DayPeriod?.ToString(),
        MealRelation = version.MealRelation?.ToString(),
        version.MinimumIntervalMinutes,

        // Instructions were missing from this projection, so an edit to the instruction
        // note audited as a no-op. Added here rather than left for later: the file was
        // already open and the hole is one line wide.
        version.Instructions,
        version.IsPaused,
        version.VersionNumber,
    });

    internal readonly record struct ParsedPlan(
        ExactQuantity Dose,
        RecurrenceSpecification Recurrence,
        DayPeriod? DayPeriod,
        MealRelation? MealRelation);
}

/// <summary>Whether the household is setting this plan aside, or picking it back up.</summary>
/// <summary>The last day of doses. Defaults to today in the plan's zone.</summary>
public sealed record EndPlanRequest(DateOnly? EndsOn = null);

/// <summary>The first day the plan asks again. Defaults to today in the plan's zone.</summary>
public sealed record RestartPlanRequest(DateOnly? StartsOn = null);

public sealed record SetPlanPausedRequest(bool IsPaused);

public sealed record TreatmentPlanRequest(
    Guid PersonId,
    Guid MedicationDefinitionId,
    long DoseNumerator,
    long DoseDenominator,
    string TimeZoneId,
    string Kind = "Scheduled",
    string Pattern = "Daily",
    int? WeekdayMask = null,
    int? IntervalDays = null,
    int? DayOfMonth = null,
    int? IntervalMonths = null,
    DateOnly? EffectiveFrom = null,
    DateOnly? EffectiveTo = null,
    TimeOnly? LocalTime = null,
    string? DayPeriod = null,
    string? MealRelation = null,
    int? MinimumIntervalMinutes = null,
    string? Instructions = null,
    /// <summary>The phone may name the id and carry a key on a create; the web sends neither.</summary>
    Guid? Id = null,
    string? IdempotencyKey = null);
