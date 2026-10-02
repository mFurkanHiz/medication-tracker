using MedicationTracker.Api.Domain.Inventory;
using MedicationTracker.Api.Domain.Quantities;

namespace MedicationTracker.Api.Tests;

/// <summary>
/// Proves the owner-critical correction path: the system charged the wrong box and
/// the user says so afterwards. ADR 0014 invariant 7 — the medication total must not
/// move, and the historical entry must not be rewritten.
/// </summary>
public sealed class AllocationCorrectionTests
{
    [Fact]
    public void A_correction_credits_the_wrong_package_and_debits_the_right_one_equally()
    {
        var boxOne = Package(1, PackageState.Opened, 7);
        var boxTwo = Package(2, PackageState.Opened, 20);
        var charged = Allocation(boxOne.PackageId, ExactQuantity.One);

        var refusal = AllocationCorrection.TryPlan(
            [charged],
            charged.AllocationId,
            boxTwo,
            targetBalance: new ExactQuantity(20),
            consumingPersonId: null,
            out var plan);

        Assert.Equal(AllocationCorrectionRefusal.None, refusal);
        Assert.NotNull(plan);
        Assert.Equal(boxOne.PackageId, plan.FromPackageId);
        Assert.Equal(boxTwo.PackageId, plan.ToPackageId);

        // Equal and opposite by construction: this is what conserves the total.
        Assert.Equal(plan.ReversalQuantity, plan.ConsumeQuantity);
        Assert.Equal(ExactQuantity.One, plan.ReversalQuantity);

        var netChange = plan.ReversalQuantity - plan.ConsumeQuantity;
        Assert.True(netChange.IsZero);
    }

    [Fact]
    public void The_corrected_balances_move_by_exactly_the_allocated_amount()
    {
        var charged = Allocation(Guid.NewGuid(), ExactQuantity.One);
        var target = Package(2, PackageState.Opened, 20);

        AllocationCorrection.TryPlan(
            [charged],
            charged.AllocationId,
            target,
            new ExactQuantity(20),
            null,
            out var plan);

        var boxOneBefore = new ExactQuantity(7);
        var boxTwoBefore = new ExactQuantity(20);

        var boxOneAfter = boxOneBefore + plan!.ReversalQuantity;
        var boxTwoAfter = boxTwoBefore - plan.ConsumeQuantity;

        Assert.Equal(new ExactQuantity(8), boxOneAfter);
        Assert.Equal(new ExactQuantity(19), boxTwoAfter);
        Assert.Equal(boxOneBefore + boxTwoBefore, boxOneAfter + boxTwoAfter);
    }

    [Fact]
    public void A_correction_to_a_package_without_enough_stock_is_refused()
    {
        var charged = Allocation(Guid.NewGuid(), ExactQuantity.One);
        var emptyTarget = Package(2, PackageState.Opened, 0);

        var refusal = AllocationCorrection.TryPlan(
            [charged],
            charged.AllocationId,
            emptyTarget,
            targetBalance: ExactQuantity.Zero,
            consumingPersonId: null,
            out var plan);

        Assert.Equal(AllocationCorrectionRefusal.TargetHasInsufficientStock, refusal);
        Assert.Null(plan);
    }

    [Fact]
    public void Correcting_an_allocation_to_its_existing_source_is_refused()
    {
        var samePackage = Package(1, PackageState.Opened, 7);
        var charged = Allocation(samePackage.PackageId, ExactQuantity.One);

        var refusal = AllocationCorrection.TryPlan(
            [charged],
            charged.AllocationId,
            samePackage,
            new ExactQuantity(7),
            null,
            out _);

        Assert.Equal(AllocationCorrectionRefusal.SameSource, refusal);
    }

    [Fact]
    public void An_allocation_superseded_by_an_earlier_correction_cannot_be_corrected_again()
    {
        var superseded = Allocation(Guid.NewGuid(), ExactQuantity.One, isActive: false);
        var target = Package(2, PackageState.Opened, 20);

        var refusal = AllocationCorrection.TryPlan(
            [superseded],
            superseded.AllocationId,
            target,
            new ExactQuantity(20),
            null,
            out _);

        Assert.Equal(AllocationCorrectionRefusal.AllocationNotActive, refusal);
    }

    [Fact]
    public void An_unknown_allocation_is_refused_rather_than_silently_ignored()
    {
        var charged = Allocation(Guid.NewGuid(), ExactQuantity.One);
        var target = Package(2, PackageState.Opened, 20);

        var refusal = AllocationCorrection.TryPlan(
            [charged],
            Guid.NewGuid(),
            target,
            new ExactQuantity(20),
            null,
            out _);

        Assert.Equal(AllocationCorrectionRefusal.UnknownAllocation, refusal);
    }

    [Fact]
    public void An_untracked_administration_has_no_allocation_to_correct()
    {
        var target = Package(2, PackageState.Opened, 20);

        var refusal = AllocationCorrection.TryPlan(
            [],
            Guid.NewGuid(),
            target,
            new ExactQuantity(20),
            null,
            out _);

        Assert.Equal(AllocationCorrectionRefusal.AdministrationIsUntracked, refusal);
    }

    [Theory]
    [InlineData(PackageState.Disposed)]
    [InlineData(PackageState.Lost)]
    [InlineData(PackageState.Archived)]
    public void A_correction_onto_a_retired_package_is_refused(PackageState state)
    {
        var charged = Allocation(Guid.NewGuid(), ExactQuantity.One);
        var retired = Package(2, state, 20);

        var refusal = AllocationCorrection.TryPlan(
            [charged],
            charged.AllocationId,
            retired,
            new ExactQuantity(20),
            null,
            out _);

        Assert.Equal(AllocationCorrectionRefusal.TargetNotEligible, refusal);
    }

    [Fact]
    public void A_correction_onto_a_package_held_by_another_person_is_refused()
    {
        var charged = Allocation(Guid.NewGuid(), ExactQuantity.One);
        var heldByBorrower = Package(2, PackageState.Opened, 20, holderPersonId: Guid.NewGuid());

        var refusal = AllocationCorrection.TryPlan(
            [charged],
            charged.AllocationId,
            heldByBorrower,
            new ExactQuantity(20),
            consumingPersonId: Guid.NewGuid(),
            out _);

        Assert.Equal(AllocationCorrectionRefusal.TargetNotEligible, refusal);
    }

    [Fact]
    public void A_tracked_allocation_can_be_corrected_onto_loose_stock()
    {
        var charged = Allocation(Guid.NewGuid(), new ExactQuantity(1, 2));

        var refusal = AllocationCorrection.TryPlan(
            [charged],
            charged.AllocationId,
            target: null,
            targetBalance: new ExactQuantity(4),
            consumingPersonId: null,
            out var plan);

        Assert.Equal(AllocationCorrectionRefusal.None, refusal);
        Assert.Null(plan!.ToPackageId);
        Assert.False(plan.TargetRequiresOpening);
        Assert.Equal(new ExactQuantity(1, 2), plan.ConsumeQuantity);
    }

    [Fact]
    public void Correcting_a_split_dose_moves_only_the_named_allocation()
    {
        var fromOpened = Allocation(Guid.NewGuid(), new ExactQuantity(1, 2));
        var fromSealed = Allocation(Guid.NewGuid(), ExactQuantity.One);
        var target = Package(3, PackageState.Opened, 20);

        var refusal = AllocationCorrection.TryPlan(
            [fromOpened, fromSealed],
            fromSealed.AllocationId,
            target,
            new ExactQuantity(20),
            null,
            out var plan);

        Assert.Equal(AllocationCorrectionRefusal.None, refusal);
        Assert.Equal(fromSealed.AllocationId, plan!.SupersededAllocationId);
        Assert.Equal(ExactQuantity.One, plan.ConsumeQuantity);
    }

    private static ActiveAllocation Allocation(Guid packageId, ExactQuantity quantity, bool isActive = true) =>
        new(Guid.NewGuid(), packageId, quantity, isActive);

    private static PackageCandidate Package(
        int ordinal,
        PackageState state,
        long balance,
        Guid? holderPersonId = null) =>
        new(
            Guid.NewGuid(),
            ordinal,
            state,
            new ExactQuantity(balance),
            null,
            null,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(ordinal),
            holderPersonId,
            false);
}
