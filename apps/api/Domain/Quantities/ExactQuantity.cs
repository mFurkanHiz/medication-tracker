namespace MedicationTracker.Api.Domain.Quantities;

/// <summary>
/// An exact medication amount held as a normalised rational number.
/// Medication amounts are never stored or computed in binary floating point:
/// a half tablet is 1/2, not 0.5, and repeated addition of thirds must not drift.
/// </summary>
/// <remarks>
/// Arithmetic widens to <see cref="Int128"/> internally so that cross-multiplying
/// two in-range amounts cannot overflow silently. The reduced result must still fit
/// in <see cref="long"/>; if it does not, the operation throws rather than wrapping,
/// because a wrapped medication amount is worse than a failed request.
/// </remarks>
public readonly record struct ExactQuantity : IComparable<ExactQuantity>
{
    /// <summary>Largest numerator magnitude accepted from an external caller.</summary>
    public const long MaximumNumerator = 1_000_000;

    /// <summary>Largest denominator accepted from an external caller.</summary>
    public const long MaximumDenominator = 10_000;

    public static ExactQuantity Zero => new(0);
    public static ExactQuantity One => new(1);

    public long Numerator { get; }

    /// <summary>Always strictly positive; the sign lives on the numerator.</summary>
    public long Denominator { get; }

    public ExactQuantity(long numerator, long denominator = 1)
    {
        if (denominator == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(denominator), "Denominator cannot be zero.");
        }

        if (denominator == long.MinValue || numerator == long.MinValue)
        {
            throw new ArgumentOutOfRangeException(nameof(numerator), "Quantity is out of range.");
        }

        if (denominator < 0)
        {
            numerator = -numerator;
            denominator = -denominator;
        }

        var divisor = GreatestCommonDivisor(Math.Abs(numerator), denominator);
        Numerator = numerator / divisor;
        Denominator = denominator / divisor;
    }

    public bool IsZero => Numerator == 0;

    public bool IsPositive => Numerator > 0;

    public bool IsNegative => Numerator < 0;

    /// <summary>True when this amount came from inside the accepted input range.</summary>
    public bool IsWithinInputRange =>
        Math.Abs(Numerator) <= MaximumNumerator && Denominator <= MaximumDenominator;

    /// <summary>
    /// Parses a caller-supplied numerator/denominator pair, rejecting zero, negative
    /// and out-of-range values. Use for any quantity that must be strictly positive.
    /// </summary>
    public static bool TryCreatePositive(long numerator, long denominator, out ExactQuantity quantity)
    {
        quantity = Zero;
        if (numerator is <= 0 or > MaximumNumerator || denominator is <= 0 or > MaximumDenominator)
        {
            return false;
        }

        quantity = new ExactQuantity(numerator, denominator);
        return true;
    }

    /// <summary>
    /// Parses a caller-supplied pair that is allowed to be zero but not negative.
    /// Use for remaining amounts, thresholds and observed counts.
    /// </summary>
    public static bool TryCreateNonNegative(long numerator, long denominator, out ExactQuantity quantity)
    {
        quantity = Zero;
        if (numerator is < 0 or > MaximumNumerator || denominator is <= 0 or > MaximumDenominator)
        {
            return false;
        }

        quantity = new ExactQuantity(numerator, denominator);
        return true;
    }

    public static ExactQuantity operator +(ExactQuantity left, ExactQuantity right) =>
        FromWide(
            (Int128)left.Numerator * right.Denominator + (Int128)right.Numerator * left.Denominator,
            (Int128)left.Denominator * right.Denominator);

    public static ExactQuantity operator -(ExactQuantity left, ExactQuantity right) =>
        FromWide(
            (Int128)left.Numerator * right.Denominator - (Int128)right.Numerator * left.Denominator,
            (Int128)left.Denominator * right.Denominator);

    public static ExactQuantity operator -(ExactQuantity value) => new(-value.Numerator, value.Denominator);

    /// <summary>Scales the amount by a whole number, for example one dose times N days.</summary>
    public static ExactQuantity operator *(ExactQuantity left, long factor) =>
        FromWide((Int128)left.Numerator * factor, left.Denominator);

    public static bool operator <(ExactQuantity left, ExactQuantity right) => left.CompareTo(right) < 0;

    public static bool operator <=(ExactQuantity left, ExactQuantity right) => left.CompareTo(right) <= 0;

    public static bool operator >(ExactQuantity left, ExactQuantity right) => left.CompareTo(right) > 0;

    public static bool operator >=(ExactQuantity left, ExactQuantity right) => left.CompareTo(right) >= 0;

    /// <summary>
    /// Exact ordering by cross-multiplication. Widening to <see cref="Int128"/> keeps
    /// this total and overflow-free for every representable amount, so sorting a
    /// package list can never throw.
    /// </summary>
    public int CompareTo(ExactQuantity other) =>
        ((Int128)Numerator * other.Denominator).CompareTo((Int128)other.Numerator * Denominator);

    public static ExactQuantity Min(ExactQuantity left, ExactQuantity right) => left <= right ? left : right;

    public static ExactQuantity Max(ExactQuantity left, ExactQuantity right) => left >= right ? left : right;

    public static ExactQuantity Sum(IEnumerable<ExactQuantity> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var total = Zero;
        foreach (var value in values)
        {
            total += value;
        }

        return total;
    }

    /// <summary>
    /// How many whole <paramref name="divisor"/> amounts fit inside this amount.
    /// Used by depletion forecasting, which may only count complete doses.
    /// </summary>
    public long WholeMultiplesOf(ExactQuantity divisor)
    {
        if (!IsPositive || !divisor.IsPositive)
        {
            return 0;
        }

        return (long)(((Int128)Numerator * divisor.Denominator) / ((Int128)Denominator * divisor.Numerator));
    }

    /// <summary>
    /// Culture-invariant exact rendering: <c>7</c> or <c>1/2</c> or <c>3/4</c>.
    /// Clients format for display; this is for logs, audit values and tests.
    /// </summary>
    public override string ToString() =>
        Denominator == 1
            ? Numerator.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{Numerator}/{Denominator}");

    private static ExactQuantity FromWide(Int128 numerator, Int128 denominator)
    {
        if (denominator == Int128.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(denominator), "Denominator cannot be zero.");
        }

        if (denominator < Int128.Zero)
        {
            numerator = -numerator;
            denominator = -denominator;
        }

        var divisor = GreatestCommonDivisor(Int128.Abs(numerator), denominator);
        numerator /= divisor;
        denominator /= divisor;

        if (numerator < long.MinValue || numerator > long.MaxValue || denominator > long.MaxValue)
        {
            throw new OverflowException("Exact medication quantity exceeded the representable range.");
        }

        return new ExactQuantity((long)numerator, (long)denominator);
    }

    private static long GreatestCommonDivisor(long left, long right)
    {
        while (right != 0)
        {
            (left, right) = (right, left % right);
        }

        return left == 0 ? 1 : left;
    }

    private static Int128 GreatestCommonDivisor(Int128 left, Int128 right)
    {
        while (right != Int128.Zero)
        {
            (left, right) = (right, left % right);
        }

        return left == Int128.Zero ? Int128.One : left;
    }
}
