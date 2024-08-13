using Beutl.Utilities;

namespace Beutl.UnitTests.Utilities;

public class MathUtilitiesTests
{
    [Test]
    [TestCase(0, 0, 0)]
    [TestCase(1, 1, 1)]
    [TestCase(1, 2, 2)]
    [TestCase(2, 1, 2)]
    [TestCase(-1, 1, 1)]
    [TestCase(1, -1, 1)]
    [TestCase(0, -2, -1)]
    public void ClampInt_ShouldReturnClampedValue(int val, int min, int max)
    {
        int result = MathUtilities.Clamp(val, min, max);
        Assert.That(result, Is.EqualTo(Math.Max(min, Math.Min(max, val))));
    }

    [Test]
    [TestCase(0.0, 0.0, 0.0)]
    [TestCase(1.0, 1.0, 1.0)]
    [TestCase(1.0, 2.0, 2.0)]
    [TestCase(2.0, 1.0, 2.0)]
    [TestCase(-1.0, 1.0, 1.0)]
    [TestCase(1.0, -1.0, 1.0)]
    [TestCase(0.0, -2.0, -1.0)]
    public void ClampDouble_ShouldReturnClampedValue(double val, double min, double max)
    {
        double result = MathUtilities.Clamp(val, min, max);
        Assert.That(result, Is.EqualTo(Math.Max(min, Math.Min(max, val))));
    }

    [Test]
    [TestCase(0.0f, 0.0f, 0.0f)]
    [TestCase(1.0f, 1.0f, 1.0f)]
    [TestCase(1.0f, 2.0f, 2.0f)]
    [TestCase(2.0f, 1.0f, 2.0f)]
    [TestCase(-1.0f, 1.0f, 1.0f)]
    [TestCase(1.0f, -1.0f, 1.0f)]
    [TestCase(0.0f, -2.0f, -1.0f)]
    public void ClampFloat_ShouldReturnClampedValue(float val, float min, float max)
    {
        float result = MathUtilities.Clamp(val, min, max);
        Assert.That(result, Is.EqualTo(Math.Max(min, Math.Min(max, val))));
    }

    [Test]
    [TestCase(0.0, 0.0, 0.0)]
    [TestCase(1.0, 1.0, 1.0)]
    [TestCase(1.0, 2.0, 2.0)]
    [TestCase(2.0, 1.0, 2.0)]
    [TestCase(-1.0, 1.0, 1.0)]
    [TestCase(1.0, -1.0, 1.0)]
    [TestCase(0.0, -2.0, -1.0)]
    public void ClampDecimal_ShouldReturnClampedValue(decimal val, decimal min, decimal max)
    {
        decimal result = MathUtilities.Clamp(val, min, max);
        Assert.That(result, Is.EqualTo(Math.Max(min, Math.Min(max, val))));
    }

    [Test]
    [TestCase(0, int.MaxValue, int.MinValue)]
    public void ClampInt_ShouldThrowArgumentExceptionForInvalidRange(int val, int min, int max)
    {
        Assert.That(() => MathUtilities.Clamp(val, min, max), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    [TestCase(0d, double.MaxValue, double.MinValue)]
    public void ClampDouble_ShouldThrowArgumentExceptionForInvalidRange(double val, double min, double max)
    {
        Assert.That(() => MathUtilities.Clamp(val, min, max), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    [TestCase(0f, float.MaxValue, float.MinValue)]
    public void ClampFloat_ShouldThrowArgumentExceptionForInvalidRange(float val, float min, float max)
    {
        Assert.That(() => MathUtilities.Clamp(val, min, max), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    [TestCase(0d, 100d, 80d)]
    public void ClampDecimal_ShouldThrowArgumentExceptionForInvalidRange(double val, double min, double max)
    {
        Assert.That(() => MathUtilities.Clamp((decimal)val, (decimal)min, (decimal)max), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    [TestCase(1.0, 1.0, true)]
    [TestCase(1.0, 1.0 - MathUtilities.DoubleEpsilon, true)]
    [TestCase(1.0, 1.0 + MathUtilities.DoubleEpsilon, true)]
    public void AreCloseDouble_ShouldReturnExpectedResult(double value1, double value2, bool expected)
    {
        bool result = MathUtilities.AreClose(value1, value2);
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    [TestCase(1.0f, 1.0f, true)]
    [TestCase(1.0f, 1.0f - MathUtilities.FloatEpsilon, true)]
    [TestCase(1.0f, 1.0f + MathUtilities.FloatEpsilon, true)]
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
    [TestCase(MathF.PI, 200.0f)]
    [TestCase(2 * MathF.PI, 400.0f)]
    public void Rad2Grad_ShouldConvertRadiansToGradians(float radians, float expected)
    {
        float result = MathUtilities.Rad2Grad(radians);
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
    [TestCase(1.0, true)]
    [TestCase(1.0 + 9.0 * MathUtilities.DoubleEpsilon, true)]
    [TestCase(1.0 - 9.0 * MathUtilities.DoubleEpsilon, true)]
    [TestCase(1.0 + 11.0 * MathUtilities.DoubleEpsilon, false)]
    [TestCase(1.0 - 11.0 * MathUtilities.DoubleEpsilon, false)]
    public void IsOne_ShouldReturnExpectedResultForDouble(double value, bool expected)
    {
        bool result = MathUtilities.IsOne(value);
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    [TestCase(1.0f, true)]
    [TestCase(1.0f + 9.0f * MathUtilities.FloatEpsilon, true)]
    [TestCase(1.0f - 9.0f * MathUtilities.FloatEpsilon, true)]
    [TestCase(1.0f + 11.0f * MathUtilities.FloatEpsilon, false)]
    [TestCase(1.0f - 11.0f * MathUtilities.FloatEpsilon, false)]
    public void IsOne_ShouldReturnExpectedResultForFloat(float value, bool expected)
    {
        bool result = MathUtilities.IsOne(value);
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    [TestCase(1.0, 2.0, true)]
    [TestCase(2.0, 1.0, false)]
    [TestCase(1.0, 1.0 + 10.0 * MathUtilities.DoubleEpsilon, false)]
    public void LessThan_ShouldReturnExpectedResultForDouble(double value1, double value2, bool expected)
    {
        bool result = MathUtilities.LessThan(value1, value2);
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    [TestCase(1.0f, 2.0f, true)]
    [TestCase(2.0f, 1.0f, false)]
    [TestCase(1.0f, 1.0f + 10.0f * MathUtilities.FloatEpsilon, false)]
    public void LessThan_ShouldReturnExpectedResultForFloat(float value1, float value2, bool expected)
    {
        bool result = MathUtilities.LessThan(value1, value2);
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    [TestCase(2.0, 1.0, true)]
    [TestCase(1.0, 2.0, false)]
    [TestCase(1.0 + 10.0 * MathUtilities.DoubleEpsilon, 1.0, false)]
    public void GreaterThan_ShouldReturnExpectedResultForDouble(double value1, double value2, bool expected)
    {
        bool result = MathUtilities.GreaterThan(value1, value2);
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    [TestCase(2.0f, 1.0f, true)]
    [TestCase(1.0f, 2.0f, false)]
    [TestCase(1.0f + 10.0f * MathUtilities.FloatEpsilon, 1.0f, false)]
    public void GreaterThan_ShouldReturnExpectedResultForFloat(float value1, float value2, bool expected)
    {
        bool result = MathUtilities.GreaterThan(value1, value2);
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
    [TestCase(2.0, 1.0, true)]
    [TestCase(1.0, 2.0, false)]
    [TestCase(1.0 + 10.0 * MathUtilities.DoubleEpsilon, 1.0, true)]
    public void GreaterThanOrClose_ShouldReturnExpectedResultForDouble(double value1, double value2, bool expected)
    {
        bool result = MathUtilities.GreaterThanOrClose(value1, value2);
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    [TestCase(2.0f, 1.0f, true)]
    [TestCase(1.0f, 2.0f, false)]
    [TestCase(1.0f + 10.0f * MathUtilities.FloatEpsilon, 1.0f, true)]
    public void GreaterThanOrClose_ShouldReturnExpectedResultForFloat(float value1, float value2, bool expected)
    {
        bool result = MathUtilities.GreaterThanOrClose(value1, value2);
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
