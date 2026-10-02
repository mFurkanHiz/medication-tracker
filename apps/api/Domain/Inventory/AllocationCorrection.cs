using MedicationTracker.Api.Domain.Quantities;

namespace MedicationTracker.Api.Domain.Inventory;

/// <summary>
/// Why a requested allocation correction cannot be applied.
/// </summary>
public enum AllocationCorrectionRefusal
{
    None = 0,

    /// <summary>The allocation named does not belong to the administration.</summary>
    UnknownAllocation = 1,

    /// <summary>The allocation was already superseded by an earlier correction.</summary>
    AllocationNotActive = 2,

    /// <summary>Source and target are the same, so there is nothing to correct.</summary>
    SameSource = 3,

    /// <summary>
    /// The target source does not hold enough stock. Reality disagreeing with the
    /// ledger is a counting problem, not something a correction may paper over by
    /// driving a package negative.
    /// </summary>
    TargetHasInsufficientStock = 4,

    /// <summary>The target package is disposed, lost, archived, or held by someone else.</summary>
    TargetNotEligible = 5,

    /// <summary>The administration drew no tracked stock, so there is nothing to move.</summary>
    AdministrationIsUntracked = 6,
}

/// <summary>
/// An existing allocation, as the correction planner sees it.
/// </summary>
public sealed record ActiveAllocation(Guid AllocationId, Guid? PackageId, ExactQuantity Quantity, bool IsActive);

/// <summary>
/// The append-only consequences of moving one allocation to a different source.
/// </summary>
/// <param name="ReversalQuantity">
/// Positive credit written back to <paramref name="FromPackageId"/>.
/// </param>
/// <param name="ConsumeQuantity">
/// Negative-signed-at-write debit taken from <paramref name="ToPackageId"/>. Equal in
/// magnitude to <paramref name="ReversalQuantity"/>, which is what keeps the
/// medication total unchanged.
/// </param>
public sealed record AllocationCorrectionPlan(
    Guid SupersededAllocationId,
    Guid? FromPackageId,
    Guid? ToPackageId,
    ExactQuantity ReversalQuantity,
    ExactQuantity ConsumeQuantity,
    bool TargetRequiresOpening);

/// <summary>
/// Plans the correction of "the system charged the wrong box".
/// </summary>
/// <remarks>
/// <para>
/// The historical ledger entry is never updated. The plan is always a matched pair of
/// new entries under one correlation: a credit to the package that was charged
/// wrongly and an equal debit against the package that was really used. Because the
/// two amounts are equal and opposite, the medication's net total is unchanged by
/// construction rather than by arithmetic that could drift.
/// </para>
/// <para>
/// The superseded allocation row is marked inactive and a replacement is inserted, so
/// "which box did this dose come from" has a current answer and a full history of
/// previous answers.
/// </para>
/// </remarks>
public static class AllocationCorrection
{
    /// <summary>
    /// Validates and plans a correction.
    /// </summary>
    /// <param name="allocations">Every allocation recorded for the administration.</param>
    /// <param name="allocationId">The allocation the user says is wrong.</param>
    /// <param name="target">
    /// The package the user really used, or null for package-independent stock.
    /// </param>
    /// <param name="targetBalance">Current balance of the target source.</param>
    /// <param name="consumingPersonId">Who the dose was for, for eligibility checks.</param>
    /// <param name="plan">The resulting append-only plan when the correction is allowed.</param>
    /// <returns><see cref="AllocationCorrectionRefusal.None"/> when planning succeeded.</returns>
    public static AllocationCorrectionRefusal TryPlan(
        IReadOnlyCollection<ActiveAllocation> allocations,
        Guid allocationId,
        PackageCandidate? target,
        ExactQuantity targetBalance,
        Guid? consumingPersonId,
        out AllocationCorrectionPlan? plan)
    {
        ArgumentNullException.ThrowIfNull(allocations);
        plan = null;

        if (allocations.Count == 0)
        {
            return AllocationCorrectionRefusal.AdministrationIsUntracked;
        }

        var allocation = allocations.SingleOrDefault(candidate => candidate.AllocationId == allocationId);
        if (allocation is null)
        {
            return AllocationCorrectionRefusal.UnknownAllocation;
        }

        if (!allocation.IsActive)
        {
            return AllocationCorrectionRefusal.AllocationNotActive;
        }

        if (allocation.PackageId == target?.PackageId)
        {
            return AllocationCorrectionRefusal.SameSource;
        }

        if (target is not null && !PackageConsumptionPolicy.IsEligible(
                target with { Balance = ExactQuantity.Max(target.Balance, ExactQuantity.One) },
                consumingPersonId))
        {
            // Eligibility is checked for state and holder only. A zero balance is
            // reported separately below so the user sees the real reason.
            return AllocationCorrectionRefusal.TargetNotEligible;
        }

        if (targetBalance < allocation.Quantity)
        {
            return AllocationCorrectionRefusal.TargetHasInsufficientStock;
        }

        plan = new AllocationCorrectionPlan(
            allocation.AllocationId,
            allocation.PackageId,
            target?.PackageId,
            allocation.Quantity,
            allocation.Quantity,
            target is { State: PackageState.Sealed });

        return AllocationCorrectionRefusal.None;
    }
}
