using System.Linq.Expressions;
using MedicationTracker.Api.Modules.Audit;
using MedicationTracker.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedicationTracker.Api.Modules.Treatments;

/// <summary>
/// Stops the plans that depend on something being put away — a medication, a person —
/// without destroying them.
/// </summary>
/// <remarks>
/// <para>
/// Archiving used to soft-delete every active plan for the thing being archived, and
/// nothing could revive a deleted plan: no route un-deletes one, and restoring the
/// medication cleared only its own archive flag. So "archive" quietly meant "retype your
/// doses, times, weekdays and instruction notes from memory if you ever come back".
/// </para>
/// <para>
/// Pausing says the same thing truthfully. The plan is set aside, its whole shape is
/// kept, the adherence replay stops counting it, and picking it up again is one press.
/// Deleting was only ever the available verb, not the right one.
/// </para>
/// <para>
/// One rule for both cascades, in one place, because a medication and a person being put
/// away are the same event from the plan's point of view and they must not drift apart.
/// </para>
/// </remarks>
internal static class PlanDeactivation
{
    /// <summary>
    /// Appends a paused version to every active plan the selector matches, and audits each
    /// one. Plans already paused are left exactly as they are: pausing them again would
    /// record a decision the household did not make.
    /// </summary>
    /// <returns>How many plans this actually paused.</returns>
    public static async Task<int> PauseAsync(
        MedicationTrackerDbContext db,
        Guid householdId,
        Expression<Func<TreatmentPlan, bool>> match,
        Guid accountId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(match);

        var plans = await db.TreatmentPlans
            .Where(plan => plan.HouseholdId == householdId && plan.DeletedAt == null)
            .Where(match)
            .ToListAsync(ct);

        if (plans.Count == 0)
        {
            return 0;
        }

        var planIds = plans.ConvertAll(plan => plan.Id);

        // The governing version is the highest-numbered one, the same rule every other
        // reader uses. Grouped in memory rather than per-plan so this stays one query.
        var latest = (await db.TreatmentPlanVersions.AsNoTracking()
                .Where(version => planIds.Contains(version.TreatmentPlanId))
                .ToListAsync(ct))
            .GroupBy(version => version.TreatmentPlanId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(version => version.VersionNumber).First());

        var paused = 0;

        foreach (var plan in plans)
        {
            if (!latest.TryGetValue(plan.Id, out var current) || current.IsPaused)
            {
                continue;
            }

            // Everything is copied verbatim. Putting a medicine away says nothing about
            // the dose or the days, and bringing it back must return the plan that was
            // set aside rather than a reconstruction of it.
            var version = new TreatmentPlanVersion(
                Guid.CreateVersion7(),
                plan.Id,
                current.VersionNumber + 1,
                current.Dose,
                current.Recurrence with { EffectiveFrom = TreatmentEndpoints.VersionStartFor(current, now) },
                current.LocalTime,
                current.TimeZoneId,
                now,
                accountId,
                current.DayPeriod,
                current.MealRelation,
                current.MinimumIntervalMinutes,
                current.Instructions,
                isPaused: true);

            db.TreatmentPlanVersions.Add(version);

            db.TreatmentPlanChangeEvents.Add(new TreatmentPlanChangeEvent(
                Guid.CreateVersion7(),
                householdId,
                plan.Id,
                accountId,
                ChangeKind.CascadeDeactivated,
                TreatmentEndpoints.Snapshot(plan, current),
                TreatmentEndpoints.Snapshot(plan, version),
                now));

            paused++;
        }

        return paused;
    }
}
