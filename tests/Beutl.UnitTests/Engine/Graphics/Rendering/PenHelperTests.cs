using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;

using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
public class PenHelperTests
{
    // --- GetBounds tests ---

    [Test]
    public void GetBounds_WithPositiveOffset_ExpandsBounds()
    {
        var rect = new Rect(0, 0, 100, 100);
        var pen = new Pen { Thickness = { CurrentValue = 0 }, Offset = { CurrentValue = 10 } };
        var penResource = pen.ToResource(CompositionContext.Default);

        var result = PenHelper.GetBounds(rect, penResource);

        Assert.That(result.Width, Is.GreaterThan(rect.Width));
        Assert.That(result.Height, Is.GreaterThan(rect.Height));
        Assert.That(result, Is.EqualTo(new Rect(-10, -10, 120, 120)));
    }

    [Test]
    public void GetBounds_WithNegativeOffset_DoesNotExpandBounds()
    {
        var rect = new Rect(0, 0, 100, 100);
        var pen = new Pen
        {
            Thickness = { CurrentValue = 0 },
            Offset = { CurrentValue = -10 }
        };
        var penResource = pen.ToResource(CompositionContext.Default);

        var result = PenHelper.GetBounds(rect, penResource);

        Assert.That(result, Is.EqualTo(rect));
    }

    [Test]
    public void GetBounds_WithZeroOffset_DoesNotExpandBounds()
    {
        var rect = new Rect(0, 0, 100, 100);
        var pen = new Pen
        {
            Thickness = { CurrentValue = 0 },
            Offset = { CurrentValue = 0 }
        };
        var penResource = pen.ToResource(CompositionContext.Default);

        var result = PenHelper.GetBounds(rect, penResource);

        Assert.That(result, Is.EqualTo(rect));
    }

    // --- CreateOffsetPath tests ---

    [Test]
    public void CreateOffsetPath_WithZeroOffset_ReturnsNull()
    {
        using var fillPath = new SKPath();
        fillPath.AddRect(SKRect.Create(0, 0, 100, 100));

        var pen = new Pen { Offset = { CurrentValue = 0 } };
        var penResource = pen.ToResource(CompositionContext.Default);
        var bounds = new Rect(0, 0, 100, 100);

        var result = PenHelper.CreateOffsetPath(fillPath, penResource, bounds);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void CreateOffsetPath_WithPositiveOffset_ReturnsInflatedPath()
    {
        using var fillPath = new SKPath();
        fillPath.AddRect(SKRect.Create(0, 0, 100, 100));

        var pen = new Pen { Offset = { CurrentValue = 10 } };
        var penResource = pen.ToResource(CompositionContext.Default);
        var bounds = new Rect(0, 0, 100, 100);

        using var result = PenHelper.CreateOffsetPath(fillPath, penResource, bounds);

        Assert.That(result, Is.Not.Null);
        var originalBounds = fillPath.Bounds;
        var resultBounds = result!.Bounds;
        Assert.That(resultBounds.Width, Is.GreaterThan(originalBounds.Width));
        Assert.That(resultBounds.Height, Is.GreaterThan(originalBounds.Height));
    }

    [Test]
    public void CreateOffsetPath_WithNegativeOffset_ReturnsShrunkPath()
    {
        // Use a large rectangle so there is room to deflate
        using var fillPath = new SKPath();
        fillPath.AddRect(SKRect.Create(0, 0, 200, 200));

        var pen = new Pen { Offset = { CurrentValue = -10 } };
        var penResource = pen.ToResource(CompositionContext.Default);
        var bounds = new Rect(0, 0, 200, 200);

        using var result = PenHelper.CreateOffsetPath(fillPath, penResource, bounds);

        Assert.That(result, Is.Not.Null);
        var originalBounds = fillPath.Bounds;
        var resultBounds = result!.Bounds;
        Assert.That(resultBounds.Width, Is.LessThan(originalBounds.Width));
        Assert.That(resultBounds.Height, Is.LessThan(originalBounds.Height));
    }

    // --- CreateStrokePath (public, end-to-end) tests ---

    [Test]
    public void CreateStrokePath_WithPositiveOffset_ProducesLargerStroke()
    {
        using var fillPath = new SKPath();
        fillPath.AddRect(SKRect.Create(0, 0, 100, 100));
        var bounds = new Rect(0, 0, 100, 100);

        var penNoOffset = new Pen { Thickness = { CurrentValue = 4 }, Offset = { CurrentValue = 0 } };
        var penWithOffset = new Pen { Thickness = { CurrentValue = 4 }, Offset = { CurrentValue = 10 } };
        var resourceNoOffset = penNoOffset.ToResource(CompositionContext.Default);
        var resourceWithOffset = penWithOffset.ToResource(CompositionContext.Default);

        using var pathNoOffset = PenHelper.CreateStrokePath(fillPath, resourceNoOffset, bounds);
        using var pathWithOffset = PenHelper.CreateStrokePath(fillPath, resourceWithOffset, bounds);

        Assert.That(pathWithOffset.Bounds.Width, Is.GreaterThan(pathNoOffset.Bounds.Width));
        Assert.That(pathWithOffset.Bounds.Height, Is.GreaterThan(pathNoOffset.Bounds.Height));
    }

    [Test]
    public void CreateStrokePath_WithNegativeOffset_ProducesSmallerStroke()
    {
        // Use a larger rectangle so stroke remains visible after deflation
        using var fillPath = new SKPath();
        fillPath.AddRect(SKRect.Create(0, 0, 200, 200));
        var bounds = new Rect(0, 0, 200, 200);

        var penNoOffset = new Pen { Thickness = { CurrentValue = 4 }, Offset = { CurrentValue = 0 } };
        var penWithOffset = new Pen { Thickness = { CurrentValue = 4 }, Offset = { CurrentValue = -10 } };
        var resourceNoOffset = penNoOffset.ToResource(CompositionContext.Default);
        var resourceWithOffset = penWithOffset.ToResource(CompositionContext.Default);

        using var pathNoOffset = PenHelper.CreateStrokePath(fillPath, resourceNoOffset, bounds);
        using var pathWithOffset = PenHelper.CreateStrokePath(fillPath, resourceWithOffset, bounds);

        Assert.That(pathWithOffset.Bounds.Width, Is.LessThan(pathNoOffset.Bounds.Width));
        Assert.That(pathWithOffset.Bounds.Height, Is.LessThan(pathNoOffset.Bounds.Height));
    }
}
