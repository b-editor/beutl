using System.Globalization;

namespace Beutl.UnitTests.Core;

public class RationalTests
{
    [Test]
    [TestCase(1, 2, 1, 3,
        3, 6, 2, 6)]
    [TestCase(1, 7, 1, 12,
        12, 84, 7, 84)]
    public void Reduce(
        long leftNum, long leftDen,
        long rightNum, long rightDen,
        long expLeftNum, long expLeftDen,
        long exptRightNum, long expRightDen)
    {
        var left = new Rational(leftNum, leftDen);
        var right = new Rational(rightNum, rightDen);

        Rational.Reduce(ref left, ref right);

        Assert.That((left.Numerator, left.Denominator), Is.EqualTo((expLeftNum, expLeftDen)));
        Assert.That((right.Numerator, right.Denominator), Is.EqualTo((exptRightNum, expRightDen)));
    }

    [Test]
    [TestCase(1d / 2d, 1, 2)]
    [TestCase(1d / 3d, 1, 3)]
    [TestCase(1d / 7d, 1, 7)]
    [TestCase(double.NaN, 0, 0)]
    [TestCase(double.PositiveInfinity, 1, 0)]
    [TestCase(double.NegativeInfinity, -1, 0)]
    public void Create_From_Double(double original, long num, long den)
    {
        var r = Rational.FromDouble(original);

        Assert.That(r, Is.EqualTo(new Rational(num, den)));
    }

    [Test]
    [TestCase(2, 6, 1, 3)]
    [TestCase(2, 7, 2, 7)]
    [TestCase(2, 16, 1, 8)]
    [TestCase(9, 54, 1, 6)]
    public void Simply(long num, long den, long expNum, long expDen)
    {
        Rational simplified = new Rational(num, den).Simplify();
        Assert.That((simplified.Numerator, simplified.Denominator), Is.EqualTo((expNum, expDen)));
    }

    [Test]
    public void Simply_IntegerValue_KeepsItsValue()
    {
        Assert.That(new Rational(54, 9).Simplify(), Is.EqualTo(new Rational(6, 1)));
    }

    // An unreduced whole value is written as its value, so it reads back the same (#3459).
    [TestCase(60, 2, "30")]
    [TestCase(-60, 2, "-30")]
    [TestCase(60, -2, "-30")]
    [TestCase(5, -1, "-5")]
    [TestCase(60, 1, "60")]
    [TestCase(30000, 1001, "30000/1001")]
    // Only rounds to a whole number as a double.
    [TestCase(long.MaxValue, 2, "9223372036854775807/2")]
    // Its whole value overflows.
    [TestCase(long.MinValue, -1, "-9223372036854775808/-1")]
    public void ToString_WritesAWholeValueAsItsValue(long num, long den, string expected)
    {
        var value = new Rational(num, den);
        char[] buffer = new char[32];

        Assert.Multiple(() =>
        {
            Assert.That(value.ToString(CultureInfo.InvariantCulture), Is.EqualTo(expected));
            Assert.That(value.TryFormat(buffer, out int written, default, CultureInfo.InvariantCulture), Is.True);
            Assert.That(new string(buffer, 0, written), Is.EqualTo(expected));
            Assert.That(Rational.Parse(expected, CultureInfo.InvariantCulture).ToDouble(), Is.EqualTo(value.ToDouble()));
        });
    }

    [Test]
    [TestCase("1/1", 1, 1)]
    [TestCase("0/1", 0, 1)]
    [TestCase("0 / 1", 0, 1)]
    public void Parse(string s, long num, long den)
    {
        Rational parsed = Rational.Parse(s);
        Assert.That((parsed.Numerator, parsed.Denominator), Is.EqualTo((num, den)));
    }

    [Test]
    [TestCase("1.5")]
    [TestCase("/1")]
    public void Parse_Invalid(string s)
    {
        Assert.Catch(() => Rational.Parse(s));
    }
}
