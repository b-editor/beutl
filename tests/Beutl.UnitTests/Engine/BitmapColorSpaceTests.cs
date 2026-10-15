using Beutl.Media;

namespace Beutl.UnitTests.Engine;

public class BitmapColorSpaceTests
{
    [Test]
    public void Srgb_IsSrgb_True()
    {
        Assert.That(BitmapColorSpace.Srgb.IsSrgb, Is.True);
    }

    [Test]
    public void LinearSrgb_GammaIsLinear()
    {
        Assert.That(BitmapColorSpace.LinearSrgb.GammaIsLinear, Is.True);
    }

    [Test]
    public void Equality_SrgbVsLinear_AreNotEqual()
    {
        Assert.That(BitmapColorSpace.Srgb, Is.Not.EqualTo(BitmapColorSpace.LinearSrgb));
        Assert.That(BitmapColorSpace.Srgb != BitmapColorSpace.LinearSrgb, Is.True);
    }

    [Test]
    public void Equals_AgainstNull_ReturnsFalse()
    {
        BitmapColorSpace srgb = BitmapColorSpace.Srgb;
        Assert.That(srgb!.Equals((BitmapColorSpace?)null), Is.False);
        Assert.That(srgb!.Equals((object?)null), Is.False);
    }

    [Test]
    public void ToString_ReturnsNonEmpty()
    {
        Assert.That(BitmapColorSpace.Srgb.ToString(), Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void OperatorEquality_BothNull_True()
    {
        BitmapColorSpace? a = null;
        BitmapColorSpace? b = null;
        Assert.That(a == b, Is.True);
        Assert.That(a != b, Is.False);
    }

    [Test]
    public void OperatorEquality_LeftNull_RightNotNull_False()
    {
        BitmapColorSpace? a = null;
        Assert.That(a == BitmapColorSpace.Srgb, Is.False);
    }

    [Test]
    public void CreateIcc_InvalidData_ReturnsNull()
    {
        Assert.That(BitmapColorSpace.CreateIcc([0x00, 0x01, 0x02]), Is.Null);
    }
}
