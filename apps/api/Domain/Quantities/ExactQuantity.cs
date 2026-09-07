namespace MedicationTracker.Api.Domain.Quantities;

public readonly record struct ExactQuantity
{
    public long Numerator { get; }
    public long Denominator { get; }

    public ExactQuantity(long numerator, long denominator = 1)
    {
        if (denominator == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(denominator), "Denominator cannot be zero.");
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

    public static ExactQuantity operator +(ExactQuantity left, ExactQuantity right) =>
        new(
            checked(left.Numerator * right.Denominator + right.Numerator * left.Denominator),
            checked(left.Denominator * right.Denominator));

    public static ExactQuantity operator -(ExactQuantity left, ExactQuantity right) =>
        new(
            checked(left.Numerator * right.Denominator - right.Numerator * left.Denominator),
            checked(left.Denominator * right.Denominator));

    private static long GreatestCommonDivisor(long left, long right)
    {
        while (right != 0)
        {
            (left, right) = (right, left % right);
        }

        return left == 0 ? 1 : left;
    }
}
