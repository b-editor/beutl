using Beutl.Utilities;

namespace Beutl.UnitTests.Utilities;

public class MathUtilitiesEdgeTests
{
    [Test]
    public void AreClose_DoubleWithCustomEps_TrueWhenInsideEpsilon()
    {
        Assert.That(MathUtilities.AreClose(1.0, 1.001, 0.01), Is.True);
        Assert.That(MathUtilities.AreClose(1.0, 1.05, 0.01), Is.False);
        Assert.That(MathUtilities.AreClose(double.PositiveInfinity, double.PositiveInfinity, 1e-9), Is.True);
    }

    [Test]
    public void AreClose_DoubleNoEps_HandlesInfinity()
    {
        Assert.That(MathUtilities.AreClose(double.PositiveInfinity, double.PositiveInfinity), Is.True);
        Assert.That(MathUtilities.AreClose(double.NegativeInfinity, double.NegativeInfinity), Is.True);
    }

    [Test]
    public void AreClose_FloatNoEps_HandlesInfinity()
    {
        Assert.That(MathUtilities.AreClose(float.PositiveInfinity, float.PositiveInfinity), Is.True);
        Assert.That(MathUtilities.AreClose(float.NegativeInfinity, float.NegativeInfinity), Is.True);
    }
}
