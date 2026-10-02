using MedicationTracker.Api.Domain.Quantities;

namespace MedicationTracker.Api.Domain.Inventory;

/// <summary>
/// A physical package as the consumption policy sees it. Projected from package rows
/// and their ledger balances; the policy itself touches no database.
/// </summary>
/// <param name="PackageId">Identity of the physical container.</param>
/// <param name="Ordinal">Stable per-medication number used for friendly labels.</param>
/// <param name="State">Lifecycle state. Disposed, lost and archived are never chosen.</param>
/// <param name="Balance">Remaining amount, derived from the ledger.</param>
/// <param name="ExpiresOn">Optional expiry, used for first-expiry-first ordering.</param>
/// <param name="AcquiredOn">Optional acquisition date, used as a tie-break.</param>
/// <param name="CreatedAt">Record creation instant, used as a later tie-break.</param>
/// <param name="HolderPersonId">
/// Who physically holds the package. A package held by another person is never drawn
/// from implicitly, which is what makes lending safe.
/// </param>
/// <param name="IsPinned">The package the user explicitly chose to use next.</param>
public sealed record PackageCandidate(
    Guid PackageId,
    int Ordinal,
    PackageState State,
    ExactQuantity Balance,
    DateOnly? ExpiresOn,
    DateOnly? AcquiredOn,
    DateTimeOffset CreatedAt,
    Guid? HolderPersonId,
    bool IsPinned);

/// <summary>
/// One draw from one stock source. A null <see cref="PackageId"/> means
/// package-independent (loose) stock.
/// </summary>
/// <param name="RequiresOpening">
/// True when a sealed package had to be opened to satisfy the dose. The caller moves
/// it to <see cref="PackageState.Opened"/>; the policy stays pure.
/// </param>
public sealed record AllocationStep(Guid? PackageId, ExactQuantity Quantity, bool RequiresOpening);

/// <summary>
/// The ordered result of planning one dose against available stock.
/// </summary>
public sealed record ConsumptionPlan(
    IReadOnlyList<AllocationStep> Steps,
    bool IsSatisfiable,
    ExactQuantity Available,
    ExactQuantity Shortfall)
{
    public static ConsumptionPlan Unsatisfiable(ExactQuantity available, ExactQuantity shortfall) =>
        new([], false, available, shortfall);
}

/// <summary>
/// Decides which physical package a dose comes from.
/// </summary>
/// <remarks>
/// <para>
/// Pure and deterministic: the same inputs always produce the same plan, so the
/// choice is testable and reviewable instead of being an emergent property of an
/// endpoint. The order is:
/// </para>
/// <list type="number">
/// <item>the package the user pinned, if it still has stock;</item>
/// <item>already-opened packages before sealed ones, so a household finishes what it
/// has started rather than opening a new box;</item>
/// <item>earliest expiry first, so stock is used before it expires;</item>
/// <item>earliest acquisition, then earliest record creation, then ordinal, as fully
/// deterministic tie-breaks;</item>
/// <item>package-independent (loose) stock last, because the product is
/// package-first.</item>
/// </list>
/// <para>
/// This policy organises stock. It never decides whether a medication should be
/// taken, and it never alters a dose.
/// </para>
/// </remarks>
public static class PackageConsumptionPolicy
{
    /// <summary>
    /// Plans how to cover <paramref name="required"/> from the given sources.
    /// </summary>
    /// <param name="candidates">All packages of one medication, in any order.</param>
    /// <param name="looseBalance">Package-independent balance for the same medication.</param>
    /// <param name="required">The amount actually being taken. Must be positive.</param>
    /// <param name="consumingPersonId">
    /// Who the dose is for. Packages held by a different person are excluded.
    /// </param>
    /// <returns>
    /// A plan whose step quantities sum exactly to <paramref name="required"/>, or an
    /// unsatisfiable plan naming the shortfall. Never a partial plan: a caller cannot
    /// accidentally consume half a dose.
    /// </returns>
    public static ConsumptionPlan Plan(
        IEnumerable<PackageCandidate> candidates,
        ExactQuantity looseBalance,
        ExactQuantity required,
        Guid? consumingPersonId)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        if (!required.IsPositive)
        {
            throw new ArgumentOutOfRangeException(nameof(required), "A dose must be a positive amount.");
        }

        var eligible = candidates
            .Where(candidate => IsEligible(candidate, consumingPersonId))
            .OrderBy(candidate => candidate, PreferenceComparer.Instance)
            .ToList();

        var usableLoose = looseBalance.IsPositive ? looseBalance : ExactQuantity.Zero;
        var available = ExactQuantity.Sum(eligible.Select(candidate => candidate.Balance)) + usableLoose;

        if (available < required)
        {
            return ConsumptionPlan.Unsatisfiable(available, required - available);
        }

        var steps = new List<AllocationStep>();
        var remaining = required;

        foreach (var candidate in eligible)
        {
            if (!remaining.IsPositive)
            {
                break;
            }

            var drawn = ExactQuantity.Min(candidate.Balance, remaining);
            steps.Add(new AllocationStep(candidate.PackageId, drawn, candidate.State == PackageState.Sealed));
            remaining -= drawn;
        }

        if (remaining.IsPositive)
        {
            // Covered by the availability check above; loose stock closes the gap.
            steps.Add(new AllocationStep(null, remaining, false));
            remaining = ExactQuantity.Zero;
        }

        return new ConsumptionPlan(steps, true, available, ExactQuantity.Zero);
    }

    /// <summary>
    /// Plans a dose the user insisted comes from one specific source.
    /// </summary>
    /// <param name="candidate">
    /// The chosen package, or null for package-independent stock.
    /// </param>
    /// <param name="sourceBalance">Remaining amount in that source.</param>
    /// <remarks>
    /// A manual choice is never silently spread across other packages: if the chosen
    /// source cannot cover the dose the caller is told so, and may then correct the
    /// amount, pick another source, or record an untracked administration.
    /// </remarks>
    public static ConsumptionPlan PlanFromChosenSource(
        PackageCandidate? candidate,
        ExactQuantity sourceBalance,
        ExactQuantity required)
    {
        if (!required.IsPositive)
        {
            throw new ArgumentOutOfRangeException(nameof(required), "A dose must be a positive amount.");
        }

        var available = sourceBalance.IsPositive ? sourceBalance : ExactQuantity.Zero;
        if (available < required)
        {
            return ConsumptionPlan.Unsatisfiable(available, required - available);
        }

        var requiresOpening = candidate is { State: PackageState.Sealed };
        return new ConsumptionPlan(
            [new AllocationStep(candidate?.PackageId, required, requiresOpening)],
            true,
            available,
            ExactQuantity.Zero);
    }

    /// <summary>
    /// A package may be drawn from implicitly only when it exists physically, holds
    /// stock, and is not in someone else's hands.
    /// </summary>
    public static bool IsEligible(PackageCandidate candidate, Guid? consumingPersonId)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (candidate.State is not (PackageState.Sealed or PackageState.Opened))
        {
            return false;
        }

        if (!candidate.Balance.IsPositive)
        {
            return false;
        }

        return candidate.HolderPersonId is null || candidate.HolderPersonId == consumingPersonId;
    }

    private sealed class PreferenceComparer : IComparer<PackageCandidate>
    {
        public static readonly PreferenceComparer Instance = new();

        public int Compare(PackageCandidate? left, PackageCandidate? right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            ArgumentNullException.ThrowIfNull(left);
            ArgumentNullException.ThrowIfNull(right);

            // 1. An explicitly pinned package wins outright.
            var pinned = Rank(right.IsPinned).CompareTo(Rank(left.IsPinned));
            if (pinned != 0)
            {
                return pinned;
            }

            // 2. Finish an opened package before breaking the seal on a new one.
            var opened = StateRank(left.State).CompareTo(StateRank(right.State));
            if (opened != 0)
            {
                return opened;
            }

            // 3. First expiry first; a package with no expiry sorts after one with.
            var expiry = NullableRank(left.ExpiresOn).CompareTo(NullableRank(right.ExpiresOn));
            if (expiry != 0)
            {
                return expiry;
            }

            // 4. Deterministic tie-breaks, oldest stock first.
            var acquired = NullableRank(left.AcquiredOn).CompareTo(NullableRank(right.AcquiredOn));
            if (acquired != 0)
            {
                return acquired;
            }

            var created = left.CreatedAt.CompareTo(right.CreatedAt);
            return created != 0 ? created : left.Ordinal.CompareTo(right.Ordinal);
        }

        private static int Rank(bool value) => value ? 1 : 0;

        private static int StateRank(PackageState state) => state == PackageState.Opened ? 0 : 1;

        // Sorts present values ahead of absent ones while keeping the natural order
        // of the values themselves.
        private static (int IsMissing, DateOnly Value) NullableRank(DateOnly? value) =>
            value is { } present ? (0, present) : (1, default);
    }
}
