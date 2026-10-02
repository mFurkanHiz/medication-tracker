using MedicationTracker.Api.Domain.Inventory;
using MedicationTracker.Api.Domain.Quantities;

namespace MedicationTracker.Api.Tests;

/// <summary>
/// Proves the deterministic package-selection order and the exactness of the
/// allocation amounts it produces. These are ADR 0014 invariants 3, 6 and 9.
/// </summary>
public sealed class PackageConsumptionPolicyTests
{
    private static readonly DateTimeOffset Created = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void An_opened_package_is_used_before_a_sealed_one()
    {
        var boxA = Package(1, PackageState.Sealed, 20);
        var boxB = Package(2, PackageState.Sealed, 20);
        var boxC = Package(3, PackageState.Opened, 8);

        var plan = PackageConsumptionPolicy.Plan([boxA, boxB, boxC], ExactQuantity.Zero, One, null);

        var step = Assert.Single(plan.Steps);
        Assert.Equal(boxC.PackageId, step.PackageId);
        Assert.Equal(One, step.Quantity);
        Assert.False(step.RequiresOpening);
    }

    [Fact]
    public void A_pinned_package_outranks_opened_state_and_expiry_order()
    {
        var opened = Package(1, PackageState.Opened, 20, expiresOn: new DateOnly(2026, 2, 1));
        var pinned = Package(2, PackageState.Sealed, 20, expiresOn: new DateOnly(2030, 1, 1), isPinned: true);

        var plan = PackageConsumptionPolicy.Plan([opened, pinned], ExactQuantity.Zero, One, null);

        var step = Assert.Single(plan.Steps);
        Assert.Equal(pinned.PackageId, step.PackageId);
        Assert.True(step.RequiresOpening);
    }

    [Fact]
    public void Earliest_expiry_is_used_first_and_a_package_without_expiry_sorts_last()
    {
        var noExpiry = Package(1, PackageState.Opened, 5);
        var expiresLater = Package(2, PackageState.Opened, 5, expiresOn: new DateOnly(2027, 6, 1));
        var expiresSoon = Package(3, PackageState.Opened, 5, expiresOn: new DateOnly(2026, 6, 1));

        var plan = PackageConsumptionPolicy.Plan(
            [noExpiry, expiresLater, expiresSoon],
            ExactQuantity.Zero,
            new ExactQuantity(15),
            null);

        Assert.Equal(
            [expiresSoon.PackageId, expiresLater.PackageId, noExpiry.PackageId],
            plan.Steps.Select(step => step.PackageId));
    }

    [Fact]
    public void Acquisition_date_then_creation_then_ordinal_break_remaining_ties()
    {
        var later = Package(9, PackageState.Opened, 5, acquiredOn: new DateOnly(2026, 5, 2));
        var earlier = Package(8, PackageState.Opened, 5, acquiredOn: new DateOnly(2026, 5, 1));
        var unknownAcquisition = Package(1, PackageState.Opened, 5);

        var plan = PackageConsumptionPolicy.Plan(
            [unknownAcquisition, later, earlier],
            ExactQuantity.Zero,
            new ExactQuantity(15),
            null);

        Assert.Equal(
            [earlier.PackageId, later.PackageId, unknownAcquisition.PackageId],
            plan.Steps.Select(step => step.PackageId));
    }

    [Fact]
    public void One_dose_spans_several_packages_and_the_steps_sum_exactly_to_the_dose()
    {
        // Half a tablet left in the opened box, a one-and-a-half tablet dose.
        var opened = Package(1, PackageState.Opened, numerator: 1, denominator: 2);
        var sealedBox = Package(2, PackageState.Sealed, 20);

        var dose = new ExactQuantity(3, 2);
        var plan = PackageConsumptionPolicy.Plan([opened, sealedBox], ExactQuantity.Zero, dose, null);

        Assert.Equal(2, plan.Steps.Count);
        Assert.Equal(new ExactQuantity(1, 2), plan.Steps[0].Quantity);
        Assert.Equal(opened.PackageId, plan.Steps[0].PackageId);
        Assert.Equal(ExactQuantity.One, plan.Steps[1].Quantity);
        Assert.Equal(sealedBox.PackageId, plan.Steps[1].PackageId);
        Assert.True(plan.Steps[1].RequiresOpening);

        Assert.Equal(dose, ExactQuantity.Sum(plan.Steps.Select(step => step.Quantity)));
    }

    [Fact]
    public void Thirds_remain_exact_when_a_dose_is_split()
    {
        var first = Package(1, PackageState.Opened, numerator: 1, denominator: 3);
        var second = Package(2, PackageState.Opened, numerator: 1, denominator: 3);
        var third = Package(3, PackageState.Opened, numerator: 1, denominator: 3);

        var plan = PackageConsumptionPolicy.Plan([first, second, third], ExactQuantity.Zero, ExactQuantity.One, null);

        var total = ExactQuantity.Sum(plan.Steps.Select(step => step.Quantity));
        Assert.Equal(ExactQuantity.One, total);
        Assert.Equal(1, total.Numerator);
        Assert.Equal(1, total.Denominator);
    }

    [Fact]
    public void Insufficient_stock_yields_an_unsatisfiable_plan_naming_the_shortfall()
    {
        var opened = Package(1, PackageState.Opened, 2);

        var plan = PackageConsumptionPolicy.Plan([opened], ExactQuantity.Zero, new ExactQuantity(5), null);

        Assert.False(plan.IsSatisfiable);
        Assert.Empty(plan.Steps);
        Assert.Equal(new ExactQuantity(2), plan.Available);
        Assert.Equal(new ExactQuantity(3), plan.Shortfall);
    }

    [Fact]
    public void A_package_held_by_another_person_is_never_drawn_from_implicitly()
    {
        var borrower = Guid.NewGuid();
        var lentOut = Package(1, PackageState.Opened, 20, holderPersonId: borrower);
        var owner = Guid.NewGuid();

        var refused = PackageConsumptionPolicy.Plan([lentOut], ExactQuantity.Zero, One, owner);
        Assert.False(refused.IsSatisfiable);

        // The holder may still take from the package they are physically holding.
        var allowed = PackageConsumptionPolicy.Plan([lentOut], ExactQuantity.Zero, One, borrower);
        Assert.True(allowed.IsSatisfiable);
    }

    [Theory]
    [InlineData(PackageState.Disposed)]
    [InlineData(PackageState.Lost)]
    [InlineData(PackageState.Archived)]
    public void Disposed_lost_and_archived_packages_are_never_selected(PackageState state)
    {
        var excluded = Package(1, state, 20);

        var plan = PackageConsumptionPolicy.Plan([excluded], ExactQuantity.Zero, One, null);

        Assert.False(plan.IsSatisfiable);
        Assert.Equal(ExactQuantity.Zero, plan.Available);
    }

    [Fact]
    public void An_empty_package_contributes_nothing_and_the_next_package_is_opened()
    {
        var emptied = Package(1, PackageState.Opened, 0);
        var nextBox = Package(2, PackageState.Sealed, 20);

        var plan = PackageConsumptionPolicy.Plan([emptied, nextBox], ExactQuantity.Zero, One, null);

        var step = Assert.Single(plan.Steps);
        Assert.Equal(nextBox.PackageId, step.PackageId);
        Assert.True(step.RequiresOpening);
    }

    [Fact]
    public void Loose_stock_is_only_used_after_every_eligible_package()
    {
        var opened = Package(1, PackageState.Opened, 1);

        var plan = PackageConsumptionPolicy.Plan([opened], new ExactQuantity(10), new ExactQuantity(3), null);

        Assert.Equal(2, plan.Steps.Count);
        Assert.Equal(opened.PackageId, plan.Steps[0].PackageId);
        Assert.Equal(ExactQuantity.One, plan.Steps[0].Quantity);
        Assert.Null(plan.Steps[1].PackageId);
        Assert.Equal(new ExactQuantity(2), plan.Steps[1].Quantity);
    }

    [Fact]
    public void A_chosen_source_is_never_silently_spread_across_other_packages()
    {
        var chosen = Package(2, PackageState.Sealed, 1);

        var refused = PackageConsumptionPolicy.PlanFromChosenSource(chosen, new ExactQuantity(1), new ExactQuantity(3));

        Assert.False(refused.IsSatisfiable);
        Assert.Equal(new ExactQuantity(2), refused.Shortfall);
    }

    [Fact]
    public void A_chosen_sealed_package_reports_that_it_must_be_opened()
    {
        var chosen = Package(2, PackageState.Sealed, 20);

        var plan = PackageConsumptionPolicy.PlanFromChosenSource(chosen, new ExactQuantity(20), One);

        var step = Assert.Single(plan.Steps);
        Assert.Equal(chosen.PackageId, step.PackageId);
        Assert.True(step.RequiresOpening);
    }

    [Fact]
    public void Choosing_loose_stock_explicitly_produces_a_package_independent_step()
    {
        var plan = PackageConsumptionPolicy.PlanFromChosenSource(null, new ExactQuantity(4), One);

        var step = Assert.Single(plan.Steps);
        Assert.Null(step.PackageId);
        Assert.False(step.RequiresOpening);
    }

    [Fact]
    public void A_non_positive_dose_is_a_programming_error_rather_than_a_refusal()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PackageConsumptionPolicy.Plan([], ExactQuantity.Zero, ExactQuantity.Zero, null));
    }

    private static ExactQuantity One => ExactQuantity.One;

    private static PackageCandidate Package(
        int ordinal,
        PackageState state,
        long numerator,
        long denominator = 1,
        DateOnly? expiresOn = null,
        DateOnly? acquiredOn = null,
        Guid? holderPersonId = null,
        bool isPinned = false) =>
        new(
            Guid.NewGuid(),
            ordinal,
            state,
            new ExactQuantity(numerator, denominator),
            expiresOn,
            acquiredOn,
            Created.AddMinutes(ordinal),
            holderPersonId,
            isPinned);
}
