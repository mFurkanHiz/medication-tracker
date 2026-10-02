using MedicationTracker.Api.Application;
using MedicationTracker.Api.Domain.Quantities;
using MedicationTracker.Api.Domain.Refill;
using MedicationTracker.Api.Domain.Scheduling;
using MedicationTracker.Api.Modules.Inventory;
using MedicationTracker.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedicationTracker.Api.Modules.Refill;

/// <summary>
/// Low-stock settings, prescription eligibility, and the forecast that compares them.
/// </summary>
public static class RefillEndpoints
{
    public static IEndpointRouteBuilder MapRefillEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api/households/{householdId:guid}/medication-definitions/{definitionId:guid}");

        api.MapPut("/refill-policy", async (
            Guid householdId,
            Guid definitionId,
            RefillPolicyRequest request,
            HttpContext context,
            MedicationTrackerDbContext db,
            CancellationToken ct) =>
        {
            if (!await HouseholdAccess.IsMemberAsync(db, householdId, context, ct))
            {
                return ApiResults.Forbidden();
            }

            if (!await db.MedicationDefinitions.AsNoTracking().AnyAsync(
                    definition => definition.Id == definitionId && definition.HouseholdId == householdId, ct))
            {
                return Results.NotFound();
            }

            ExactQuantity? threshold = null;
            if (request.LowStockThresholdNumerator is { } numerator)
            {
                if (!ExactQuantity.TryCreateNonNegative(
                        numerator, request.LowStockThresholdDenominator ?? 1, out var parsed))
                {
                    return ApiResults.Invalid("lowStockThreshold", "invalid");
                }

                threshold = parsed;
            }

            if (request.LowStockDays is < 0 or > 365)
            {
                return ApiResults.Invalid("lowStockDays", "invalid");
            }

            var accountId = HouseholdAccess.RequireAccountId(context);
            var now = DateTimeOffset.UtcNow;

            var policy = await db.RefillPolicies.SingleOrDefaultAsync(
                candidate => candidate.MedicationDefinitionId == definitionId, ct);

            if (policy is null)
            {
                policy = new MedicationRefillPolicy(Guid.CreateVersion7(), householdId, definitionId, now, accountId);
                db.RefillPolicies.Add(policy);
            }

            policy.Update(threshold, request.LowStockDays, request.NextEligibleRefillOn, request.Note, now, accountId);

            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        api.MapGet("/forecast", async (
            Guid householdId,
            Guid definitionId,
            DateOnly? from,
            HttpContext context,
            MedicationTrackerDbContext db,
            CancellationToken ct) =>
        {
            if (!await HouseholdAccess.IsMemberAsync(db, householdId, context, ct))
            {
                return ApiResults.Forbidden();
            }

            if (!await db.MedicationDefinitions.AsNoTracking().AnyAsync(
                    definition => definition.Id == definitionId && definition.HouseholdId == householdId, ct))
            {
                return Results.NotFound();
            }

            var forecast = await ProjectAsync(db, householdId, definitionId, from, ct);

            return Results.Ok(new
            {
                medicationDefinitionId = definitionId,
                balance = InventoryEndpoints.Quantity(forecast.Balance),
                isForecastable = forecast.IsForecastable,
                projectedDepletionOn = forecast.ProjectedDepletionOn,
                daysOfStockRemaining = forecast.DaysOfStockRemaining,
                isLowStock = forecast.IsLowStock,
                lowStockReason = forecast.LowStockTrigger.ToString(),
                nextEligibleRefillOn = forecast.NextEligibleRefillOn,

                // The gap warning only exists because physical depletion and official
                // eligibility are separate facts.
                hasRefillGap = forecast.HasRefillGap,
                refillGapDays = forecast.RefillGapDays,
            });
        });

        return endpoints;
    }

    /// <summary>
    /// Projects one medication forward from its ledger balance over the real due days of
    /// every active plan that consumes it.
    /// </summary>
    internal static async Task<DepletionForecast> ProjectAsync(
        MedicationTrackerDbContext db,
        Guid householdId,
        Guid definitionId,
        DateOnly? from,
        CancellationToken ct)
    {
        var stock = await InventoryReader.LoadAsync(db, householdId, definitionId, tracked: false, ct);
        var day = from ?? DateOnly.FromDateTime(DateTime.UtcNow);

        var versions = await (from plan in db.TreatmentPlans.AsNoTracking()
                              join version in db.TreatmentPlanVersions.AsNoTracking()
                                  on plan.Id equals version.TreatmentPlanId
                              where plan.HouseholdId == householdId
                                    && plan.MedicationDefinitionId == definitionId
                                    && plan.DeletedAt == null
                              select new { plan.Id, version }).ToListAsync(ct);

        // Only the newest version of each plan describes future consumption; older
        // versions describe the past and must not be counted again.
        var plans = versions
            .GroupBy(row => row.Id)
            .Select(group => group.OrderByDescending(row => row.version.VersionNumber).First().version)
            .Select(version => new PlannedConsumption(version.Recurrence, version.Dose))
            .ToList();

        var policy = await db.RefillPolicies.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.MedicationDefinitionId == definitionId, ct);

        var settings = policy is null
            ? RefillSettings.None
            : new RefillSettings(policy.LowStockThreshold, policy.LowStockDays, policy.NextEligibleRefillOn);

        return RefillForecast.Project(stock.Total, plans, day, settings);
    }
}

public sealed record RefillPolicyRequest(
    long? LowStockThresholdNumerator = null,
    long? LowStockThresholdDenominator = null,
    int? LowStockDays = null,
    DateOnly? NextEligibleRefillOn = null,
    string? Note = null);
