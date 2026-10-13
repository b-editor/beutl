using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Cache;

[TestFixture]
public class RenderCacheRulesTest
{
    [TestCase(50, 50, true)]
    [TestCase(5, 5, false)]
    [TestCase(200, 200, false)]
    public void Match_PixelSize_ShouldReturnWhetherPixelsAreWithinRange(int width, int height, bool expected)
    {
        var rules = new RenderCacheRules(10000, 100);

        Assert.That(rules.Match(new PixelSize(width, height)), Is.EqualTo(expected));
    }

    [TestCase(2500, true)]
    [TestCase(25, false)]
    [TestCase(40000, false)]
    public void Match_Pixels_ShouldReturnWhetherPixelsAreWithinRange(int pixels, bool expected)
    {
        var rules = new RenderCacheRules(10000, 100);

        Assert.That(rules.Match(pixels), Is.EqualTo(expected));
    }

    [Test]
    public void Match_PixelSize_LargeOverflow_DoesNotWrapNegative()
    {
        var rules = new RenderCacheRules(int.MaxValue, 1);
        // 50000 × 50000 = 2_500_000_000 > int.MaxValue; would wrap negative under int arithmetic.
        var size = new PixelSize(50000, 50000);

        bool result = rules.Match(size);

        Assert.That(result, Is.False);
    }

    [Test]
    public void Create_ShouldClampMinToOne_WhenBelowOne()
    {
        var rules = RenderCacheRules.Create(maxPixels: 10000, minPixels: 0);

        Assert.That(rules.MinPixels, Is.EqualTo(1));
        Assert.That(rules.MaxPixels, Is.EqualTo(10000));
    }

    [Test]
    public void Create_ShouldRaiseMaxToMin_WhenMinExceedsMax()
    {
        // A mis-ordered configuration (min > max) must not produce a range that matches nothing.
        var rules = RenderCacheRules.Create(maxPixels: 100, minPixels: 5000);

        Assert.That(rules.MinPixels, Is.EqualTo(5000));
        Assert.That(rules.MaxPixels, Is.EqualTo(5000));
        Assert.That(rules.Match(5000), Is.True);
    }

    [Test]
    public void Create_ShouldPreserveValidRange()
    {
        var rules = RenderCacheRules.Create(maxPixels: 10000, minPixels: 100);

        Assert.That(rules.MinPixels, Is.EqualTo(100));
        Assert.That(rules.MaxPixels, Is.EqualTo(10000));
    }
}
