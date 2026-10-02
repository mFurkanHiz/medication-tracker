using MedicationTracker.Api.Domain.Inventory;
using MedicationTracker.Api.Domain.Quantities;

namespace MedicationTracker.Api.Tests;

/// <summary>
/// The owner-specified mandatory acceptance scenario from <c>docs/v1-acceptance.md</c>,
/// executed against the pure domain. The same scenario is also asserted end to end
/// against PostgreSQL; this version isolates the decision logic so a failure says
/// which half is wrong.
/// </summary>
/// <remarks>
/// Synthetic data only. "Parol 500 mg Tablet" is used as a generic tablet stand-in
/// with invented quantities; no real person or prescription is represented.
/// </remarks>
public sealed class PackageFirstAcceptanceScenarioTests
{
    private static readonly DateTimeOffset Created = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly Guid BoxA = Guid.Parse("0000a001-0000-0000-0000-000000000001");
    private static readonly Guid BoxB = Guid.Parse("0000b002-0000-0000-0000-000000000002");
    private static readonly Guid BoxC = Guid.Parse("0000c003-0000-0000-0000-000000000003");

    [Fact]
    public void Three_packages_of_a_twenty_tablet_medication_total_forty_eight()
    {
        var stock = InitialStock();

        var total = ExactQuantity.Sum(stock.Select(package => package.Balance));

        Assert.Equal(new ExactQuantity(48), total);
        Assert.Equal(3, stock.Count);
        Assert.Equal(3, stock.Select(package => package.PackageId).Distinct().Count());
    }

    [Fact]
    public void A_normal_dose_draws_from_the_opened_package_and_leaves_twenty_twenty_seven()
    {
        var stock = InitialStock();

        var plan = PackageConsumptionPolicy.Plan(stock, ExactQuantity.Zero, ExactQuantity.One, null);

        var step = Assert.Single(plan.Steps);
        Assert.Equal(BoxC, step.PackageId);

        var after = Apply(stock, plan);
        Assert.Equal(new ExactQuantity(20), BalanceOf(after, BoxA));
        Assert.Equal(new ExactQuantity(20), BalanceOf(after, BoxB));
        Assert.Equal(new ExactQuantity(7), BalanceOf(after, BoxC));
        Assert.Equal(new ExactQuantity(47), ExactQuantity.Sum(after.Select(package => package.Balance)));
    }

    [Fact]
    public void The_next_dose_can_be_taken_explicitly_from_box_b()
    {
        var afterFirstDose = Apply(
            InitialStock(),
            PackageConsumptionPolicy.Plan(InitialStock(), ExactQuantity.Zero, ExactQuantity.One, null));

        var boxB = afterFirstDose.Single(package => package.PackageId == BoxB);
        var plan = PackageConsumptionPolicy.PlanFromChosenSource(boxB, boxB.Balance, ExactQuantity.One);

        var after = Apply(afterFirstDose, plan);

        Assert.Equal(new ExactQuantity(19), BalanceOf(after, BoxB));
        Assert.Equal(new ExactQuantity(7), BalanceOf(after, BoxC));
        Assert.Equal(new ExactQuantity(20), BalanceOf(after, BoxA));
    }

    [Fact]
    public void Correcting_an_auto_charged_dose_from_box_c_to_box_b_restores_c_and_keeps_the_total()
    {
        var stock = InitialStock();
        var autoPlan = PackageConsumptionPolicy.Plan(stock, ExactQuantity.Zero, ExactQuantity.One, null);
        var afterAutoCharge = Apply(stock, autoPlan);

        var totalBeforeCorrection = ExactQuantity.Sum(afterAutoCharge.Select(package => package.Balance));
        Assert.Equal(new ExactQuantity(47), totalBeforeCorrection);
        Assert.Equal(new ExactQuantity(7), BalanceOf(afterAutoCharge, BoxC));

        // The user says the tablet actually came out of Box B.
        var chargedAllocation = new ActiveAllocation(Guid.NewGuid(), BoxC, ExactQuantity.One, IsActive: true);
        var boxB = afterAutoCharge.Single(package => package.PackageId == BoxB);

        var refusal = AllocationCorrection.TryPlan(
            [chargedAllocation],
            chargedAllocation.AllocationId,
            boxB,
            BalanceOf(afterAutoCharge, BoxB),
            consumingPersonId: null,
            out var correction);

        Assert.Equal(AllocationCorrectionRefusal.None, refusal);

        var afterCorrection = ApplyCorrection(afterAutoCharge, correction!);

        // Box C returns to the 8 it held before the wrong charge, Box B pays instead.
        Assert.Equal(new ExactQuantity(8), BalanceOf(afterCorrection, BoxC));
        Assert.Equal(new ExactQuantity(19), BalanceOf(afterCorrection, BoxB));
        Assert.Equal(new ExactQuantity(20), BalanceOf(afterCorrection, BoxA));

        // The decisive assertion: correcting the source must not change the total.
        Assert.Equal(
            totalBeforeCorrection,
            ExactQuantity.Sum(afterCorrection.Select(package => package.Balance)));
    }

    [Fact]
    public void When_box_c_runs_out_the_next_eligible_package_is_opened()
    {
        var stock = InitialStock();

        // Eight doses empty Box C; the ninth must open a sealed package.
        for (var dose = 0; dose < 8; dose++)
        {
            var plan = PackageConsumptionPolicy.Plan(stock, ExactQuantity.Zero, ExactQuantity.One, null);
            Assert.Equal(BoxC, Assert.Single(plan.Steps).PackageId);
            stock = Apply(stock, plan);
        }

        Assert.Equal(ExactQuantity.Zero, BalanceOf(stock, BoxC));

        var ninth = PackageConsumptionPolicy.Plan(stock, ExactQuantity.Zero, ExactQuantity.One, null);
        var step = Assert.Single(ninth.Steps);

        Assert.Equal(BoxA, step.PackageId);
        Assert.True(step.RequiresOpening);

        var after = Apply(stock, ninth);
        Assert.Equal(new ExactQuantity(39), ExactQuantity.Sum(after.Select(package => package.Balance)));
    }

    [Fact]
    public void Editing_the_default_package_size_cannot_change_an_existing_package()
    {
        // Capacity is snapshotted on the package, so the catalog's new default of 30
        // is irrelevant to boxes that were created as 20.
        var stock = InitialStock();
        const long newCatalogDefaultCapacity = 30;

        var capacities = stock.Select(_ => NominalCapacity).Distinct().ToList();

        Assert.Equal([new ExactQuantity(20)], capacities);
        Assert.NotEqual(new ExactQuantity(newCatalogDefaultCapacity), Assert.Single(capacities));
    }

    private static ExactQuantity NominalCapacity => new(20);

    /// <summary>Box A 20/20 sealed, Box B 20/20 sealed, Box C 8/20 opened.</summary>
    private static List<PackageCandidate> InitialStock() =>
    [
        Package(BoxA, 1, PackageState.Sealed, 20),
        Package(BoxB, 2, PackageState.Sealed, 20),
        Package(BoxC, 3, PackageState.Opened, 8),
    ];

    private static List<PackageCandidate> Apply(IEnumerable<PackageCandidate> stock, ConsumptionPlan plan)
    {
        Assert.True(plan.IsSatisfiable);
        var drawn = plan.Steps
            .Where(step => step.PackageId is not null)
            .GroupBy(step => step.PackageId!.Value)
            .ToDictionary(group => group.Key, group => ExactQuantity.Sum(group.Select(step => step.Quantity)));

        return stock
            .Select(package => drawn.TryGetValue(package.PackageId, out var amount)
                ? package with
                {
                    Balance = package.Balance - amount,
                    State = package.State == PackageState.Sealed ? PackageState.Opened : package.State,
                }
                : package)
            .ToList();
    }

    private static List<PackageCandidate> ApplyCorrection(
        IEnumerable<PackageCandidate> stock,
        AllocationCorrectionPlan correction) =>
        stock
            .Select(package =>
            {
                if (package.PackageId == correction.FromPackageId)
                {
                    return package with { Balance = package.Balance + correction.ReversalQuantity };
                }

                return package.PackageId == correction.ToPackageId
                    ? package with
                    {
                        Balance = package.Balance - correction.ConsumeQuantity,
                        State = package.State == PackageState.Sealed ? PackageState.Opened : package.State,
                    }
                    : package;
            })
            .ToList();

    private static ExactQuantity BalanceOf(IEnumerable<PackageCandidate> stock, Guid packageId) =>
        stock.Single(package => package.PackageId == packageId).Balance;

    private static PackageCandidate Package(Guid id, int ordinal, PackageState state, long balance) =>
        new(id, ordinal, state, new ExactQuantity(balance), null, null, Created.AddMinutes(ordinal), null, false);
}
