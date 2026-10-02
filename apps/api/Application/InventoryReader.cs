using MedicationTracker.Api.Domain.Inventory;
using MedicationTracker.Api.Domain.Quantities;
using MedicationTracker.Api.Modules.Inventory;
using MedicationTracker.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedicationTracker.Api.Application;

/// <summary>
/// Every balance for one medication, projected from the ledger in a single read.
/// </summary>
/// <param name="PackageBalances">Balance per physical package, including empty ones.</param>
/// <param name="LooseBalance">Balance not attributed to any package.</param>
public sealed record MedicationStock(
    Guid MedicationDefinitionId,
    IReadOnlyList<MedicationPackage> Packages,
    IReadOnlyDictionary<Guid, ExactQuantity> PackageBalances,
    ExactQuantity LooseBalance)
{
    /// <summary>
    /// Everything the household has of this medication: all package balances plus loose
    /// stock. ADR 0014 invariant 5.
    /// </summary>
    public ExactQuantity Total =>
        ExactQuantity.Sum(PackageBalances.Values) + LooseBalance;

    public ExactQuantity BalanceOf(Guid packageId) =>
        PackageBalances.TryGetValue(packageId, out var balance) ? balance : ExactQuantity.Zero;

    /// <summary>Projects the packages into the shape the consumption policy expects.</summary>
    public IReadOnlyList<PackageCandidate> Candidates() =>
        Packages.Select(package => package.ToCandidate(BalanceOf(package.Id))).ToList();

    public PackageCandidate? CandidateFor(Guid packageId)
    {
        var package = Packages.SingleOrDefault(candidate => candidate.Id == packageId);
        return package?.ToCandidate(BalanceOf(package.Id));
    }
}

/// <summary>
/// Reads stock positions. Balances are always derived here and never cached on a row,
/// so a package's remaining amount cannot disagree with its ledger.
/// </summary>
public static class InventoryReader
{
    /// <summary>
    /// Loads one medication's packages and ledger-derived balances.
    /// </summary>
    /// <param name="tracked">
    /// True when the caller is about to modify the packages, so EF must track them.
    /// Read-only callers pass false.
    /// </param>
    public static async Task<MedicationStock> LoadAsync(
        MedicationTrackerDbContext db,
        Guid householdId,
        Guid medicationDefinitionId,
        bool tracked,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);

        var packageQuery = db.Packages
            .Where(package => package.HouseholdId == householdId
                              && package.MedicationDefinitionId == medicationDefinitionId);

        var packages = await (tracked ? packageQuery : packageQuery.AsNoTracking())
            .OrderBy(package => package.Ordinal)
            .ToListAsync(ct);

        var entries = await db.LedgerEntries.AsNoTracking()
            .Where(entry => entry.HouseholdId == householdId
                            && entry.MedicationDefinitionId == medicationDefinitionId)
            .Select(entry => new { entry.PackageId, entry.QuantityNumerator, entry.QuantityDenominator })
            .ToListAsync(ct);

        var balances = new Dictionary<Guid, ExactQuantity>(packages.Count);
        foreach (var package in packages)
        {
            balances[package.Id] = ExactQuantity.Zero;
        }

        var loose = ExactQuantity.Zero;
        foreach (var entry in entries)
        {
            var amount = new ExactQuantity(entry.QuantityNumerator, entry.QuantityDenominator);

            if (entry.PackageId is { } packageId)
            {
                // A package may have been retired and filtered out above; its entries
                // still belong to this medication's history but not to any live box.
                balances[packageId] = balances.TryGetValue(packageId, out var current)
                    ? current + amount
                    : amount;
            }
            else
            {
                loose += amount;
            }
        }

        foreach (var orphaned in balances.Keys.Where(id => packages.All(package => package.Id != id)).ToList())
        {
            balances.Remove(orphaned);
        }

        return new MedicationStock(medicationDefinitionId, packages, balances, loose);
    }

    /// <summary>
    /// Totals for several medications at once, for list and dashboard reads. Avoids the
    /// per-medication query a list view would otherwise issue.
    /// </summary>
    public static async Task<Dictionary<Guid, ExactQuantity>> TotalsAsync(
        MedicationTrackerDbContext db,
        Guid householdId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);

        var entries = await db.LedgerEntries.AsNoTracking()
            .Where(entry => entry.HouseholdId == householdId)
            .Select(entry => new
            {
                entry.MedicationDefinitionId,
                entry.QuantityNumerator,
                entry.QuantityDenominator,
            })
            .ToListAsync(ct);

        var totals = new Dictionary<Guid, ExactQuantity>();
        foreach (var entry in entries)
        {
            var amount = new ExactQuantity(entry.QuantityNumerator, entry.QuantityDenominator);
            totals[entry.MedicationDefinitionId] = totals.TryGetValue(entry.MedicationDefinitionId, out var current)
                ? current + amount
                : amount;
        }

        return totals;
    }

    /// <summary>
    /// Serialises all stock-mutating work for one household.
    /// </summary>
    /// <remarks>
    /// Taken inside the ambient transaction and released when it ends. Two concurrent
    /// doses in the same household therefore cannot both read the same balance and both
    /// decide there is enough — ADR 0014 invariant 12. The lock is per household, so
    /// unrelated households never block each other.
    /// </remarks>
    public static Task LockHouseholdAsync(
        MedicationTrackerDbContext db,
        Guid householdId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var key = householdId.ToString();
        return db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))",
            ct);
    }

    /// <summary>
    /// The next friendly ordinal for a medication's packages. Called while the household
    /// lock is held, so two simultaneous additions cannot claim the same number.
    /// </summary>
    public static async Task<int> NextOrdinalAsync(
        MedicationTrackerDbContext db,
        Guid medicationDefinitionId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);

        var highest = await db.Packages.AsNoTracking()
            .Where(package => package.MedicationDefinitionId == medicationDefinitionId)
            .MaxAsync(package => (int?)package.Ordinal, ct);

        return (highest ?? 0) + 1;
    }
}
