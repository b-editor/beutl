using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Cache;

[TestFixture]
public class RenderCacheRulesTest
{
    [Test]
    public void Match_ShouldReturnTrue_WhenPixelsWithinRange()
    {
        // Arrange
        var rules = new RenderCacheRules(10000, 100);
        var size = new PixelSize(50, 50); // 2500 pixels

        // Act
        bool result = rules.Match(size);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void Match_ShouldReturnFalse_WhenPixelsBelowMin()
    {
        // Arrange
        var rules = new RenderCacheRules(10000, 100);
        var size = new PixelSize(5, 5); // 25 pixels

        // Act
        bool result = rules.Match(size);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void Match_ShouldReturnFalse_WhenPixelsAboveMax()
    {
        // Arrange
        var rules = new RenderCacheRules(10000, 100);
        var size = new PixelSize(200, 200); // 40000 pixels

        // Act
        bool result = rules.Match(size);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void Match_ShouldReturnTrue_WhenPixelsWithinRange_Int()
    {
        // Arrange
        var rules = new RenderCacheRules(10000, 100);
        int pixels = 2500;

        // Act
        bool result = rules.Match(pixels);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void Match_ShouldReturnFalse_WhenPixelsBelowMin_Int()
    {
        // Arrange
        var rules = new RenderCacheRules(10000, 100);
        int pixels = 25;

        // Act
        bool result = rules.Match(pixels);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void Match_ShouldReturnFalse_WhenPixelsAboveMax_Int()
    {
        // Arrange
        var rules = new RenderCacheRules(10000, 100);
        int pixels = 40000;

        // Act
        bool result = rules.Match(pixels);

        // Assert
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
