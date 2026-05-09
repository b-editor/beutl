using System.Globalization;

namespace Beutl.UnitTests.Core;

public class RationalArithmeticTests
{
    [Test]
    public void Constants_HaveExpectedValues()
    {
        Assert.That(Rational.Zero, Is.EqualTo(new Rational(0, 1)));
        Assert.That(Rational.One, Is.EqualTo(new Rational(1, 1)));
        Assert.That(Rational.AdditiveIdentity, Is.EqualTo(Rational.Zero));
        Assert.That(Rational.MultiplicativeIdentity, Is.EqualTo(Rational.One));
        Assert.That(Rational.MaxValue.Numerator, Is.EqualTo(long.MaxValue));
        Assert.That(Rational.MinValue.Numerator, Is.EqualTo(long.MinValue));
        Assert.That(Rational.Radix, Is.EqualTo(2));
    }

    [Test]
    public void IsNaN_ReturnsTrue_OnlyFor0Over0()
    {
        Assert.That(Rational.IsNaN(Rational.NaN), Is.True);
        Assert.That(Rational.IsNaN(new Rational(1, 0)), Is.False);
        Assert.That(Rational.IsNaN(new Rational(0, 1)), Is.False);
    }

    [Test]
    public void IsInfinity_DetectsBothSigns()
    {
        Assert.That(Rational.IsInfinity(Rational.PositiveInfinity), Is.True);
        Assert.That(Rational.IsInfinity(Rational.NegativeInfinity), Is.True);
        Assert.That(Rational.IsInfinity(Rational.One), Is.False);
        Assert.That(Rational.IsPositiveInfinity(Rational.PositiveInfinity), Is.True);
        Assert.That(Rational.IsNegativeInfinity(Rational.NegativeInfinity), Is.True);
        Assert.That(Rational.IsFinite(Rational.One), Is.True);
        Assert.That(Rational.IsFinite(Rational.PositiveInfinity), Is.False);
    }

    [Test]
    public void IsInteger_TrueForWholeNumbersOnly()
    {
        Assert.That(Rational.IsInteger(new Rational(5, 1)), Is.True);
        Assert.That(Rational.IsInteger(new Rational(6, 3)), Is.True);
        Assert.That(Rational.IsInteger(new Rational(1, 2)), Is.False);
        Assert.That(Rational.IsInteger(Rational.PositiveInfinity), Is.False);
    }

    [Test]
    public void IsZero_OnlyTrueForCanonicalZero()
    {
        Assert.That(Rational.IsZero(Rational.Zero), Is.True);
        Assert.That(Rational.IsZero(new Rational(0, 5)), Is.False);
    }

    [Test]
    public void IsEvenInteger_AndIsOddInteger()
    {
        Assert.That(Rational.IsEvenInteger(new Rational(4, 1)), Is.True);
        Assert.That(Rational.IsEvenInteger(new Rational(3, 1)), Is.False);
        Assert.That(Rational.IsOddInteger(new Rational(3, 1)), Is.True);
        Assert.That(Rational.IsOddInteger(new Rational(4, 1)), Is.False);
    }

    [Test]
    public void IsNegative_RespectsSignOfNumeratorAndDenominator()
    {
        Assert.That(Rational.IsNegative(new Rational(-1, 2)), Is.True);
        Assert.That(Rational.IsNegative(new Rational(1, -2)), Is.True);
        Assert.That(Rational.IsNegative(new Rational(-1, -2)), Is.False);
        Assert.That(Rational.IsNegative(new Rational(1, 2)), Is.False);
    }

    [Test]
    public void IsPositive_ReflectsCurrentImplementation()
    {
        // Implementation uses XOR of long.IsPositive(num) and long.IsPositive(den);
        // i.e. exactly one of them is non-negative.
        Assert.That(Rational.IsPositive(new Rational(-1, 2)), Is.True);
        Assert.That(Rational.IsPositive(new Rational(1, -2)), Is.True);
        Assert.That(Rational.IsPositive(new Rational(-1, -2)), Is.False);
        Assert.That(Rational.IsPositive(new Rational(1, 2)), Is.False);
    }

    [Test]
    public void Abs_ReturnsAbsoluteValue()
    {
        Assert.That(Rational.Abs(new Rational(-3, 4)), Is.EqualTo(new Rational(3, 4)));
        Assert.That(Rational.Abs(new Rational(3, -4)), Is.EqualTo(new Rational(3, 4)));
    }

    [Test]
    public void Addition_CombinesWithCommonDenominator()
    {
        Rational result = new Rational(1, 2) + new Rational(1, 3);
        Assert.That(result.Simplify(), Is.EqualTo(new Rational(5, 6)));
    }

    [Test]
    public void Subtraction_CombinesWithCommonDenominator()
    {
        Rational result = new Rational(1, 2) - new Rational(1, 3);
        Assert.That(result.Simplify(), Is.EqualTo(new Rational(1, 6)));
    }

    [Test]
    public void Multiplication_RationalAndScalar()
    {
        Assert.That(new Rational(1, 2) * new Rational(2, 3), Is.EqualTo(new Rational(2, 6)));
        Assert.That(new Rational(1, 2) * 3, Is.EqualTo(new Rational(3, 2)));
        Assert.That(new Rational(1, 2) * 4L, Is.EqualTo(new Rational(4, 2)));
    }

    [Test]
    public void Division_RegularCase_DividesCorrectly()
    {
        Rational result = new Rational(1, 2) / new Rational(1, 4);
        Assert.That(result.Simplify(), Is.EqualTo(new Rational(2, 1)));
    }

    [Test]
    public void Division_ByZero_Throws()
    {
        Assert.That(() => new Rational(1, 2) / Rational.Zero,
            Throws.TypeOf<DivideByZeroException>());
    }

    [Test]
    public void Negation_FlipsNumeratorSign()
    {
        Assert.That(-new Rational(1, 2), Is.EqualTo(new Rational(-1, 2)));
    }

    [Test]
    public void IncrementAndDecrement_AddOrSubtractOne()
    {
        var v = new Rational(3, 2);
        Assert.That(++v, Is.EqualTo(new Rational(5, 2)));
        v = new Rational(3, 2);
        Assert.That(--v, Is.EqualTo(new Rational(1, 2)));
    }

    [Test]
    public void Modulo_RationalAndInt()
    {
        Rational result = new Rational(7, 2) % new Rational(3, 2);
        Assert.That(result, Is.EqualTo(new Rational(1, 2)));

        Rational intResult = new Rational(7, 2) % 2;
        Assert.That(intResult, Is.EqualTo(new Rational(1, 2)));
    }

    [Test]
    public void Comparison_HandlesNaN_NeverGreaterOrLess()
    {
        Assert.That(Rational.NaN < Rational.One, Is.False);
        Assert.That(Rational.NaN > Rational.One, Is.False);
        Assert.That(Rational.NaN <= Rational.One, Is.False);
        Assert.That(Rational.NaN >= Rational.One, Is.False);
    }

    [Test]
    public void Comparison_OfIntegerRationals()
    {
        var two = new Rational(2);
        var three = new Rational(3);
        var twoCopy = new Rational(2);
        var threeCopy = new Rational(3);
        Assert.That(two < three, Is.True);
        Assert.That(three > two, Is.True);
        Assert.That(two <= twoCopy, Is.True);
        Assert.That(three >= threeCopy, Is.True);
    }

    [Test]
    public void CompareTo_OrdersByValue_HandlesNullAndNaN()
    {
        var two = new Rational(2);
        Assert.That(two.CompareTo(null), Is.EqualTo(1));
        Assert.That(two.CompareTo((object)new Rational(3)), Is.EqualTo(-1));
        Assert.That(two.CompareTo(new Rational(2)), Is.EqualTo(0));
        Assert.That(Rational.NaN.CompareTo(Rational.NaN), Is.EqualTo(0));
        Assert.That(Rational.NaN.CompareTo(Rational.One), Is.EqualTo(-1));
        Assert.That(Rational.One.CompareTo(Rational.NaN), Is.EqualTo(1));
    }

    [Test]
    public void CompareTo_NonRational_Throws()
    {
        Assert.That(() => new Rational(1).CompareTo("not a rational"),
            Throws.ArgumentException);
    }

    [Test]
    public void ToString_NaN_ProducesIndeterminateLabel()
    {
        Assert.That(Rational.NaN.ToString(), Is.EqualTo("[ Indeterminate ]"));
        Assert.That(Rational.PositiveInfinity.ToString(), Is.EqualTo("[ PositiveInfinity ]"));
        Assert.That(Rational.NegativeInfinity.ToString(), Is.EqualTo("[ NegativeInfinity ]"));
        Assert.That(Rational.Zero.ToString(), Is.EqualTo("0"));
        Assert.That(new Rational(5).ToString(), Is.EqualTo("5"));
        Assert.That(new Rational(1, 2).ToString(), Is.EqualTo("1/2"));
    }

    [Test]
    public void TryFormat_WritesExpectedRepresentation()
    {
        Span<char> buf = stackalloc char[32];
        Assert.That(Rational.NaN.TryFormat(buf, out int w, default, CultureInfo.InvariantCulture), Is.True);
        Assert.That(buf[..w].ToString(), Is.EqualTo("[ Indeterminate ]"));

        Assert.That(Rational.PositiveInfinity.TryFormat(buf, out w, default, CultureInfo.InvariantCulture), Is.True);
        Assert.That(buf[..w].ToString(), Is.EqualTo("[ PositiveInfinity ]"));

        Assert.That(new Rational(0, 1).TryFormat(buf, out w, default, CultureInfo.InvariantCulture), Is.True);
        Assert.That(buf[..w].ToString(), Is.EqualTo("0"));

        Assert.That(new Rational(7, 2).TryFormat(buf, out w, default, CultureInfo.InvariantCulture), Is.True);
        Assert.That(buf[..w].ToString(), Is.EqualTo("7/2"));
    }

    [Test]
    public void Conversions_ToDecimalDoubleSingle()
    {
        var r = new Rational(1, 2);
        Assert.That(r.ToDouble(), Is.EqualTo(0.5));
        Assert.That(r.ToSingle(), Is.EqualTo(0.5f));
        Assert.That(r.ToDecimal(), Is.EqualTo(0.5m));
    }

    [Test]
    public void Simplify_HandlesEdgeCases()
    {
        Assert.That(Rational.NaN.Simplify(), Is.EqualTo(Rational.NaN));
        Assert.That(Rational.PositiveInfinity.Simplify(), Is.EqualTo(Rational.PositiveInfinity));
        Assert.That(Rational.NegativeInfinity.Simplify(), Is.EqualTo(Rational.NegativeInfinity));
        Assert.That(new Rational(5, 5).Simplify(), Is.EqualTo(new Rational(1, 1)));
        Assert.That(Rational.Zero.Simplify(), Is.EqualTo(Rational.Zero));
        Assert.That(new Rational(6, 8).Simplify(), Is.EqualTo(new Rational(3, 4)));
    }

    [Test]
    public void TryParse_ValidAndInvalidStrings()
    {
        Assert.That(Rational.TryParse("3/4", out Rational r1), Is.True);
        Assert.That(r1, Is.EqualTo(new Rational(3, 4)));

        Assert.That(Rational.TryParse("5", out Rational r2), Is.True);
        Assert.That(r2, Is.EqualTo(new Rational(5)));

        Assert.That(Rational.TryParse("1.5", out _), Is.False);
        Assert.That(Rational.TryParse("/2", out _), Is.False);
        Assert.That(Rational.TryParse("a/b", out _), Is.False);
    }

    [Test]
    public void MaxMagnitude_ReturnsLargerByAbsoluteValue()
    {
        Assert.That(Rational.MaxMagnitude(new Rational(-3, 1), new Rational(2, 1)),
            Is.EqualTo(new Rational(-3, 1)));
        Assert.That(Rational.MaxMagnitude(Rational.NaN, new Rational(2)),
            Is.EqualTo(Rational.NaN));
    }

    [Test]
    public void MinMagnitude_ReturnsSmallerByAbsoluteValue()
    {
        Assert.That(Rational.MinMagnitude(new Rational(-3, 1), new Rational(2, 1)),
            Is.EqualTo(new Rational(2, 1)));
        Assert.That(Rational.MinMagnitude(Rational.NaN, new Rational(2)),
            Is.EqualTo(Rational.NaN));
    }
}
