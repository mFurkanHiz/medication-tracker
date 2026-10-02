using MedicationTracker.Api.Domain.Inventory;
using MedicationTracker.Api.Domain.Quantities;

namespace MedicationTracker.Api.Tests;

public sealed class ExactQuantityTests
{
    [Fact]
    public void Fractions_are_reduced_and_added_exactly()
    {
        var quantity = new ExactQuantity(1, 2) + new ExactQuantity(1, 4);

        Assert.Equal(new ExactQuantity(3, 4), quantity);
    }

    [Fact]
    public void Count_reconciliation_preserves_the_observed_difference()
    {
        var reconciliation = InventoryCountReconciliation.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            calculatedBeforeCount: new ExactQuantity(25),
            observed: new ExactQuantity(23));

        Assert.Equal(new ExactQuantity(-2), reconciliation.Adjustment);
        Assert.Equal(1, reconciliation.Revision);
    }

    [Fact]
    public void Repeated_addition_of_fractions_stays_exact()
    {
        var third = new ExactQuantity(1, 3);
        Assert.Equal(ExactQuantity.One, third + third + third);

        var tenth = new ExactQuantity(1, 10);
        var tenths = ExactQuantity.Sum(Enumerable.Repeat(tenth, 10));

        Assert.Equal(ExactQuantity.One, tenths);
        Assert.Equal(1, tenths.Numerator);
        Assert.Equal(1, tenths.Denominator);
    }

    [Fact]
    public void Binary_floating_point_would_drift_on_the_same_sum()
    {
        // Ten 0.1 ml doses are a realistic ledger sum, and in double precision they
        // do not add up to 1. This is why medication amounts are rational here.
        var drifted = Enumerable.Repeat(0.1d, 10).Sum();

        Assert.NotEqual(1.0d, drifted);
        Assert.Equal(1.0d, drifted, 0.000_000_000_1d);
    }

    [Fact]
    public void A_negative_denominator_is_normalised_onto_the_numerator()
    {
        var quantity = new ExactQuantity(1, -2);

        Assert.Equal(-1, quantity.Numerator);
        Assert.Equal(2, quantity.Denominator);
        Assert.True(quantity.IsNegative);
    }

    [Fact]
    public void Ordering_is_exact_rather_than_decimal_approximate()
    {
        var smaller = new ExactQuantity(333_333, 1_000_000);
        var larger = new ExactQuantity(1, 3);

        Assert.True(smaller < larger);
        Assert.True(larger > smaller);
        Assert.Equal(larger, ExactQuantity.Max(smaller, larger));
        Assert.Equal(smaller, ExactQuantity.Min(smaller, larger));
    }

    [Fact]
    public void Comparison_of_extreme_in_range_values_does_not_overflow()
    {
        var left = new ExactQuantity(ExactQuantity.MaximumNumerator, ExactQuantity.MaximumDenominator);
        var right = new ExactQuantity(ExactQuantity.MaximumNumerator, 1);

        Assert.True(left < right);
        Assert.Equal(0, left.CompareTo(left));
    }

    [Fact]
    public void Scaling_a_dose_by_whole_days_stays_exact()
    {
        var halfTablet = new ExactQuantity(1, 2);

        Assert.Equal(new ExactQuantity(15), halfTablet * 30);
    }

    [Fact]
    public void Whole_multiples_counts_only_complete_doses()
    {
        var balance = new ExactQuantity(7, 2);

        Assert.Equal(3, balance.WholeMultiplesOf(ExactQuantity.One));
        Assert.Equal(7, balance.WholeMultiplesOf(new ExactQuantity(1, 2)));
        Assert.Equal(0, balance.WholeMultiplesOf(new ExactQuantity(4)));
        Assert.Equal(0, ExactQuantity.Zero.WholeMultiplesOf(ExactQuantity.One));
        Assert.Equal(0, balance.WholeMultiplesOf(ExactQuantity.Zero));
    }

    [Theory]
    [InlineData(0, 1, false)]
    [InlineData(-1, 1, false)]
    [InlineData(1, 0, false)]
    [InlineData(1, -2, false)]
    [InlineData(ExactQuantity.MaximumNumerator + 1, 1, false)]
    [InlineData(1, ExactQuantity.MaximumDenominator + 1, false)]
    [InlineData(1, 2, true)]
    [InlineData(ExactQuantity.MaximumNumerator, ExactQuantity.MaximumDenominator, true)]
    public void Positive_input_parsing_rejects_zero_negative_and_out_of_range_values(
        long numerator,
        long denominator,
        bool expected)
    {
        Assert.Equal(expected, ExactQuantity.TryCreatePositive(numerator, denominator, out _));
    }

    [Fact]
    public void Non_negative_input_parsing_accepts_zero_but_not_a_negative_amount()
    {
        Assert.True(ExactQuantity.TryCreateNonNegative(0, 1, out var zero));
        Assert.True(zero.IsZero);
        Assert.False(ExactQuantity.TryCreateNonNegative(-1, 1, out _));
    }

    [Fact]
    public void An_amount_beyond_the_representable_range_throws_instead_of_wrapping()
    {
        var large = new ExactQuantity(long.MaxValue / 2, 3);

        Assert.Throws<OverflowException>(() => large * long.MaxValue);
    }

    [Fact]
    public void A_zero_denominator_is_rejected_at_construction()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExactQuantity(1, 0));
    }

    [Fact]
    public void Rendering_is_culture_invariant_and_exact()
    {
        Assert.Equal("7", new ExactQuantity(7).ToString());
        Assert.Equal("1/2", new ExactQuantity(2, 4).ToString());
        Assert.Equal("-3/4", new ExactQuantity(-3, 4).ToString());
    }

    [Fact]
    public void Summing_an_empty_sequence_is_zero_rather_than_an_error()
    {
        Assert.Equal(ExactQuantity.Zero, ExactQuantity.Sum([]));
    }
}
