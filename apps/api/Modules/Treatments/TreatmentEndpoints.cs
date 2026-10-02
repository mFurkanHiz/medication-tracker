using System.Text.Json;
using MedicationTracker.Api.Application;
using MedicationTracker.Api.Domain.Quantities;
using MedicationTracker.Api.Domain.Scheduling;
using MedicationTracker.Api.Modules.Audit;
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

            var accountId = HouseholdAccess.RequireAccountId(context);
            var now = DateTimeOffset.UtcNow;

            var plan = new TreatmentPlan(
                Guid.CreateVersion7(), householdId, request.PersonId, request.MedicationDefinitionId, now);

            var version = new TreatmentPlanVersion(
                Guid.CreateVersion7(), plan.Id, 1, parsed.Dose, parsed.Recurrence, request.LocalTime,
                request.TimeZoneId, now, accountId, parsed.DayPeriod, parsed.MealRelation,
                request.MinimumIntervalMinutes, request.Instructions);

            db.TreatmentPlans.Add(plan);
            db.TreatmentPlanVersions.Add(version);
            db.TreatmentPlanChangeEvents.Add(new TreatmentPlanChangeEvent(
                Guid.CreateVersion7(), householdId, plan.Id, accountId,
                ChangeKind.Created, null, Snapshot(plan, version), now));

            await db.SaveChangesAsync(ct);

            return Results.Created(
                $"/api/households/{householdId}/plans/{plan.Id}",
                new { plan.Id, versionId = version.Id });
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

            var version = new TreatmentPlanVersion(
                Guid.CreateVersion7(), plan.Id, latest.VersionNumber + 1, parsed.Dose, parsed.Recurrence,
                request.LocalTime, request.TimeZoneId, now, accountId, parsed.DayPeriod, parsed.MealRelation,
                request.MinimumIntervalMinutes, request.Instructions);

            db.TreatmentPlanVersions.Add(version);
            db.TreatmentPlanChangeEvents.Add(new TreatmentPlanChangeEvent(
                Guid.CreateVersion7(), householdId, plan.Id, accountId,
                ChangeKind.VersionAppended, before, Snapshot(plan, version), now));

            await db.SaveChangesAsync(ct);
            return Results.Ok(new { versionId = version.Id, versionNumber = version.VersionNumber });
        });

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
            kind, pattern, request.WeekdayMask, request.IntervalDays, request.EffectiveFrom, request.EffectiveTo);

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
        version.EffectiveFrom,
        version.EffectiveTo,
        version.LocalTime,
        version.TimeZoneId,
        DayPeriod = version.DayPeriod?.ToString(),
        MealRelation = version.MealRelation?.ToString(),
        version.MinimumIntervalMinutes,
        version.VersionNumber,
    });

    internal readonly record struct ParsedPlan(
        ExactQuantity Dose,
        RecurrenceSpecification Recurrence,
        DayPeriod? DayPeriod,
        MealRelation? MealRelation);
}

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
    DateOnly? EffectiveFrom = null,
    DateOnly? EffectiveTo = null,
    TimeOnly? LocalTime = null,
    string? DayPeriod = null,
    string? MealRelation = null,
    int? MinimumIntervalMinutes = null,
    string? Instructions = null);
