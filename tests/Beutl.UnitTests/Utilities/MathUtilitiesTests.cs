using Beutl.Utilities;

namespace Beutl.UnitTests.Utilities;

public class MathUtilitiesTests
{
    [Test]
    [TestCase(1.0, 1.0, true)]
    [TestCase(1.0, 1.0 - MathUtilities.DoubleEpsilon, true)]
    [TestCase(1.0, 1.0 + MathUtilities.DoubleEpsilon, true)]
    [TestCase(1.0, 1.05, false)]
    [TestCase(double.PositiveInfinity, double.PositiveInfinity, true)]
    [TestCase(double.NegativeInfinity, double.NegativeInfinity, true)]
    public void AreCloseDouble_ShouldReturnExpectedResult(double value1, double value2, bool expected)
    {
        bool result = MathUtilities.AreClose(value1, value2);
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    [TestCase(1.0f, 1.0f, true)]
    [TestCase(1.0f, 1.0f - MathUtilities.FloatEpsilon, true)]
    [TestCase(1.0f, 1.0f + MathUtilities.FloatEpsilon, true)]
    [TestCase(float.PositiveInfinity, float.PositiveInfinity, true)]
    [TestCase(float.NegativeInfinity, float.NegativeInfinity, true)]
    public void AreCloseFloat_ShouldReturnExpectedResult(float value1, float value2, bool expected)
    {
        bool result = MathUtilities.AreClose(value1, value2);
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    [TestCase(0.0f, 0.0f)]
    [TestCase(180.0f, MathF.PI)]
    [TestCase(360.0f, 2 * MathF.PI)]
    public void Deg2Rad_ShouldConvertDegreesToRadians(float degrees, float expected)
    {
        float result = MathUtilities.Deg2Rad(degrees);
        Assert.That(result, Is.EqualTo(expected).Within(0.0001f));
    }

    [Test]
    [TestCase(0.0f, 0.0f)]
    [TestCase(MathF.PI, 180.0f)]
    [TestCase(2 * MathF.PI, 360.0f)]
    public void Rad2Deg_ShouldConvertRadiansToDegrees(float radians, float expected)
    {
        float result = MathUtilities.Rad2Deg(radians);
        Assert.That(result, Is.EqualTo(expected).Within(0.0001f));
    }

    [Test]
    [TestCase(0.0f, 0.0f)]
    [TestCase(200.0f, MathF.PI)]
    [TestCase(400.0f, 2 * MathF.PI)]
    public void Grad2Rad_ShouldConvertGradiansToRadians(float gradians, float expected)
    {
        float result = MathUtilities.Grad2Rad(gradians);
        Assert.That(result, Is.EqualTo(expected).Within(0.0001f));
    }

    [Test]
    [TestCase(0.0f, 0.0f)]
    [TestCase(1.0f, 2 * MathF.PI)]
    public void Turn2Rad_ShouldConvertTurnsToRadians(float turns, float expected)
    {
        float result = MathUtilities.Turn2Rad(turns);
        Assert.That(result, Is.EqualTo(expected).Within(0.0001f));
    }

    [Test]
    [TestCase(0.0, true)]
    [TestCase(9.0 * MathUtilities.DoubleEpsilon, true)]
    [TestCase(10.0 * MathUtilities.DoubleEpsilon, false)]
    public void IsZero_ShouldReturnExpectedResultForDouble(double value, bool expected)
    {
        bool result = MathUtilities.IsZero(value);
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    [TestCase(0.0f, true)]
    [TestCase(9.0f * MathUtilities.FloatEpsilon, true)]
    [TestCase(10.0f * MathUtilities.FloatEpsilon, false)]
    public void IsZero_ShouldReturnExpectedResultForFloat(float value, bool expected)
    {
        bool result = MathUtilities.IsZero(value);
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    [TestCase(1.0, 2.0, true)]
    [TestCase(2.0, 1.0, false)]
    [TestCase(1.0, 1.0 + 10.0 * MathUtilities.DoubleEpsilon, true)]
    public void LessThanOrClose_ShouldReturnExpectedResultForDouble(double value1, double value2, bool expected)
    {
        bool result = MathUtilities.LessThanOrClose(value1, value2);
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    [TestCase(1.0f, 2.0f, true)]
    [TestCase(2.0f, 1.0f, false)]
    [TestCase(1.0f, 1.0f + 10.0f * MathUtilities.FloatEpsilon, true)]
    public void LessThanOrClose_ShouldReturnExpectedResultForFloat(float value1, float value2, bool expected)
    {
        bool result = MathUtilities.LessThanOrClose(value1, value2);
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    [TestCase(48, 18, 6)]
    [TestCase(101, 103, 1)]
    [TestCase(0, 5, 5)]
    public void GreatestCommonDivisor_ShouldReturnExpectedResult(long left, long right, long expected)
    {
        long result = MathUtilities.GreatestCommonDivisor(left, right);
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    [TestCase(48, 18, 144)]
    [TestCase(101, 103, 10403)]
    [TestCase(0, 5, 0)]
    public void LeastCommonDenominator_ShouldReturnExpectedResult(long left, long right, long expected)
    {
        long result = MathUtilities.LeastCommonDenominator(left, right);
        Assert.That(result, Is.EqualTo(expected));
    }
}
