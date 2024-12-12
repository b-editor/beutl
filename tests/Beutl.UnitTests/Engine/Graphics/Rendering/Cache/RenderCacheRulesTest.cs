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
}
