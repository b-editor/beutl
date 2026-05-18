using System.Globalization;

namespace Beutl.UnitTests.Core;

public class RationalFormattingTests
{
    [Test]
    public void ToString_NaN_ReturnsIndeterminate()
    {
        Assert.That(Rational.NaN.ToString(), Is.EqualTo("[ Indeterminate ]"));
    }

    [Test]
    public void ToString_PositiveInfinity_ReturnsLabel()
    {
        Assert.That(Rational.PositiveInfinity.ToString(), Is.EqualTo("[ PositiveInfinity ]"));
    }

    [Test]
    public void ToString_NegativeInfinity_ReturnsLabel()
    {
        Assert.That(Rational.NegativeInfinity.ToString(), Is.EqualTo("[ NegativeInfinity ]"));
    }

    [Test]
    public void ToString_Zero_ReturnsZero()
    {
        Assert.That(Rational.Zero.ToString(), Is.EqualTo("0"));
    }

    [Test]
    public void ToString_Integer_ReturnsNumeratorOnly()
    {
        Assert.That(new Rational(5, 1).ToString(), Is.EqualTo("5"));
        Assert.That(new Rational(-3, 1).ToString(), Is.EqualTo("-3"));
    }

    [Test]
    public void ToString_Fraction_ReturnsSlashFormat()
    {
        Assert.That(new Rational(1, 2).ToString(), Is.EqualTo("1/2"));
        Assert.That(new Rational(7, 12).ToString(), Is.EqualTo("7/12"));
    }

    [Test]
    public void ToString_WithFormatSpecifier_DelegatesToFormatProvider()
    {
        var r = new Rational(3, 4);
        Assert.That(r.ToString("X", CultureInfo.InvariantCulture), Is.EqualTo("3/4"));
    }

    [Test]
    public void TryFormat_NaN_ReturnsLabel()
    {
        Span<char> buffer = stackalloc char[32];
        Assert.That(Rational.NaN.TryFormat(buffer, out int written, default, null), Is.True);
        Assert.That(buffer[..written].ToString(), Is.EqualTo("[ Indeterminate ]"));
    }

    [Test]
    public void TryFormat_PositiveInfinity_ReturnsLabel()
    {
        Span<char> buffer = stackalloc char[32];
        Assert.That(
            Rational.PositiveInfinity.TryFormat(buffer, out int written, default, null),
            Is.True
        );
        Assert.That(buffer[..written].ToString(), Is.EqualTo("[ PositiveInfinity ]"));
    }

    [Test]
    public void TryFormat_NegativeInfinity_ReturnsLabel()
    {
        Span<char> buffer = stackalloc char[32];
        Assert.That(
            Rational.NegativeInfinity.TryFormat(buffer, out int written, default, null),
            Is.True
        );
        Assert.That(buffer[..written].ToString(), Is.EqualTo("[ NegativeInfinity ]"));
    }

    [Test]
    public void TryFormat_Zero_WritesZero()
    {
        Span<char> buffer = stackalloc char[8];
        Assert.That(Rational.Zero.TryFormat(buffer, out int written, default, null), Is.True);
        Assert.That(buffer[..written].ToString(), Is.EqualTo("0"));
    }

    [Test]
    public void TryFormat_Integer_WritesNumerator()
    {
        Span<char> buffer = stackalloc char[8];
        Assert.That(new Rational(7, 1).TryFormat(buffer, out int written, default, null), Is.True);
        Assert.That(buffer[..written].ToString(), Is.EqualTo("7"));
    }

    [Test]
    public void TryFormat_Fraction_WritesSlashForm()
    {
        Span<char> buffer = stackalloc char[16];
        Assert.That(new Rational(3, 4).TryFormat(buffer, out int written, default, null), Is.True);
        Assert.That(buffer[..written].ToString(), Is.EqualTo("3/4"));
    }

    [Test]
    public void ToDecimal_ReturnsExactRatio()
    {
        Assert.That(new Rational(1, 4).ToDecimal(), Is.EqualTo(0.25m));
        Assert.That(new Rational(-3, 2).ToDecimal(), Is.EqualTo(-1.5m));
    }

    [Test]
    public void ToDouble_ReturnsRatio()
    {
        Assert.That(new Rational(1, 2).ToDouble(), Is.EqualTo(0.5));
    }

    [Test]
    public void ToSingle_ReturnsRatio()
    {
        Assert.That(new Rational(1, 4).ToSingle(), Is.EqualTo(0.25f));
    }

    [Test]
    public void EqualityOperators_BehaveAsExpected()
    {
        Assert.That(new Rational(1, 2) == new Rational(2, 4), Is.True);
        Assert.That(new Rational(1, 2) != new Rational(3, 4), Is.True);
    }

    [Test]
    public void Equals_NullObject_ReturnsFalse()
    {
        Assert.That(new Rational(1, 2).Equals((object?)null), Is.False);
    }

    [Test]
    public void GetHashCode_SameNumeratorDenominator_AreEqual()
    {
        Assert.That(new Rational(2, 4).GetHashCode(), Is.EqualTo(new Rational(2, 4).GetHashCode()));
    }

    [Test]
    public void Negation_FlipsSignOfNumerator()
    {
        var r = new Rational(3, 4);
        Assert.That(-r, Is.EqualTo(new Rational(-3, 4)));
    }

    [Test]
    public void MultiplicationByInt_ScalesNumerator()
    {
        var r = new Rational(1, 4);
        Assert.That(r * 3, Is.EqualTo(new Rational(3, 4)));
    }

    [Test]
    public void MultiplicationByLong_ScalesNumerator()
    {
        var r = new Rational(1, 4);
        Assert.That(r * 5L, Is.EqualTo(new Rational(5, 4)));
    }

    [Test]
    public void Simplify_NaN_ReturnsNaN()
    {
        Assert.That(Rational.NaN.Simplify(), Is.EqualTo(Rational.NaN));
    }

    [Test]
    public void Simplify_PositiveInfinity_ReturnsItself()
    {
        Assert.That(Rational.PositiveInfinity.Simplify(), Is.EqualTo(Rational.PositiveInfinity));
    }

    [Test]
    public void Simplify_Zero_ReturnsItself()
    {
        Assert.That(Rational.Zero.Simplify(), Is.EqualTo(Rational.Zero));
    }

    [Test]
    public void Simplify_EqualNumeratorDenominator_ReturnsOne()
    {
        Assert.That(new Rational(7, 7).Simplify(), Is.EqualTo(Rational.One));
    }

    [Test]
    public void Simplify_ZeroNumerator_TreatedAsIntegerAndReturnsItself()
    {
        // 0/5 は IsInteger を満たすので Simplify はそのまま返す
        Assert.That(new Rational(0, 5).Simplify(), Is.EqualTo(new Rational(0, 5)));
    }
}
