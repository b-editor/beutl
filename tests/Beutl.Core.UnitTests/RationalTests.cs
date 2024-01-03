using NUnit.Framework;

namespace Beutl.Core.UnitTests;

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

        Assert.That(left, Is.EqualTo(new Rational(expLeftNum, expLeftDen)));
        Assert.That(right, Is.EqualTo(new Rational(exptRightNum, expRightDen)));
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
    [TestCase(1f / 2f, 1, 2)]
    [TestCase(1f / 3f, 1, 3)]
    [TestCase(1f / 7f, 1, 7)]
    [TestCase(float.NaN, 0, 0)]
    [TestCase(float.PositiveInfinity, 1, 0)]
    [TestCase(float.NegativeInfinity, -1, 0)]
    public void Create_From_Single(float original, long num, long den)
    {
        var r = Rational.FromSingle(original);

        Assert.That(r, Is.EqualTo(new Rational(num, den)));
    }

    [Test]
    [TestCase(2, 6, 1, 3)]
    [TestCase(2, 7, 2, 7)]
    [TestCase(2, 16, 1, 8)]
    [TestCase(9, 54, 1, 6)]
    [TestCase(54, 9, 6, 1)]
    public void Simply(long num, long den, long expNum, long expDen)
    {
        Assert.That(new Rational(num, den).Simplify(), Is.EqualTo(new Rational(expNum, expDen)));
    }
}
