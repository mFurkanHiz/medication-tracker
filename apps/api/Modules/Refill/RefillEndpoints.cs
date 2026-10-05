using MedicationTracker.Api.Application;
using MedicationTracker.Api.Domain.Catalog;
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

            policy.Update(
                threshold, request.LowStockDays, request.NextEligibleRefillOn, request.ExpectedDepletionOn,
                request.Note, now, accountId);

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
            var suggestions = await SuggestAsync(db, householdId, definitionId, from, ct);
            var policy = await db.RefillPolicies.AsNoTracking().SingleOrDefaultAsync(
                candidate => candidate.MedicationDefinitionId == definitionId, ct);

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

                // The two dates the household may fill with one press, and the stock the
                // official one was computed from. Suggestions, not inferences: nothing is
                // stored until the household saves it.
                canSuggest = suggestions.IsComputable,
                suggestedNextEligibleRefillOn = suggestions.OfficialRunsOutOn,
                suggestedDepletionOn = suggestions.ActualRunsOutOn,
                coveredBalance = InventoryEndpoints.Quantity(suggestions.CoveredBalance),
                expectedDepletionOn = policy?.ExpectedDepletionOn,
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
        var plans = await PlannedConsumptionAsync(db, householdId, definitionId, ct);

        var policy = await db.RefillPolicies.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.MedicationDefinitionId == definitionId, ct);

        var settings = policy is null
            ? RefillSettings.None
            : new RefillSettings(policy.LowStockThreshold, policy.LowStockDays, policy.NextEligibleRefillOn);

        return RefillForecast.Project(stock.Total, plans, day, settings);
    }

    /// <summary>
    /// The two dates the household may fill with one press: when the insurance-covered
    /// stock runs out, and when all stock does — both at the planned use, with an
    /// as-needed plan counted as one dose a day (<see cref="RefillForecast.SupplyRunsOutOn"/>).
    /// </summary>
    internal static async Task<SupplySuggestions> SuggestAsync(
        MedicationTrackerDbContext db,
        Guid householdId,
        Guid definitionId,
        DateOnly? from,
        CancellationToken ct)
    {
        var definition = await db.MedicationDefinitions.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == definitionId && candidate.HouseholdId == householdId, ct);
        var stock = await InventoryReader.LoadAsync(db, householdId, definitionId, tracked: false, ct);
        var day = from ?? DateOnly.FromDateTime(DateTime.UtcNow);

        // A box may say who paid for it; loose stock has no box and follows the medicine.
        var covered = ExactQuantity.Sum(stock.Packages
                .Where(package => package.IsCoveredGiven(definition.Coverage))
                .Select(package => stock.BalanceOf(package.Id)))
            + (definition.Coverage == Coverage.SelfPaid ? ExactQuantity.Zero : stock.LooseBalance);

        var plans = await PlannedConsumptionAsync(db, householdId, definitionId, ct);

        return new SupplySuggestions(
            IsComputable: plans.Count > 0,
            OfficialRunsOutOn: RefillForecast.SupplyRunsOutOn(covered, plans, day),
            ActualRunsOutOn: RefillForecast.SupplyRunsOutOn(stock.Total, plans, day),
            CoveredBalance: covered);
    }

    /// <summary>
    /// What every running plan for this medication will consume, as the newest version
    /// of each. Older versions describe the past and must not be counted again; a paused
    /// plan is not consuming anything, and is dropped after the newest version is chosen
    /// for the same reason the Today list applies it there.
    /// </summary>
    private static async Task<List<PlannedConsumption>> PlannedConsumptionAsync(
        MedicationTrackerDbContext db,
        Guid householdId,
        Guid definitionId,
        CancellationToken ct)
    {
        var versions = await (from plan in db.TreatmentPlans.AsNoTracking()
                              join version in db.TreatmentPlanVersions.AsNoTracking()
                                  on plan.Id equals version.TreatmentPlanId
                              where plan.HouseholdId == householdId
                                    && plan.MedicationDefinitionId == definitionId
                                    && plan.DeletedAt == null
                              select new { plan.Id, version }).ToListAsync(ct);

        return versions
            .GroupBy(row => row.Id)
            .Select(group => group.OrderByDescending(row => row.version.VersionNumber).First().version)
            .Where(version => !version.IsPaused)
            .Select(version => new PlannedConsumption(version.Recurrence, version.Dose))
            .ToList();
    }
}

/// <summary>
/// Suggested dates for the refill settings. <see cref="IsComputable"/> is false when no
/// plan consumes the medication; a null date with a computable plan means the stock
/// outlasts the projection horizon.
/// </summary>
internal sealed record SupplySuggestions(
    bool IsComputable,
    DateOnly? OfficialRunsOutOn,
    DateOnly? ActualRunsOutOn,
    ExactQuantity CoveredBalance);

public sealed record RefillPolicyRequest(
    long? LowStockThresholdNumerator = null,
    long? LowStockThresholdDenominator = null,
    int? LowStockDays = null,
    DateOnly? NextEligibleRefillOn = null,
    DateOnly? ExpectedDepletionOn = null,
    string? Note = null);
