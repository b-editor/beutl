using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Source;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
public class VideoSourceRenderNodeTest
{
    private VideoSource? _videoSource;
    private VideoSource.Resource? _resource;

    [TestCase(-10f, StrokeAlignment.Center)]
    [TestCase(-10f, StrokeAlignment.Outside)]
    [TestCase(-50f, StrokeAlignment.Outside)]
    [TestCase(-60f, StrokeAlignment.Outside)]
    public void NegativeOffset_HitTestMatchesThePaintedStroke(float offset, StrokeAlignment alignment)
    {
        var pen = new Pen
        {
            Brush = { CurrentValue = Brushes.Black },
            Thickness = { CurrentValue = 40 },
            Offset = { CurrentValue = offset },
            StrokeAlignment = { CurrentValue = alignment },
        };
        using var penResource = pen.ToResource(CompositionContext.Default);
        using var fillPath = new SKPath();
        fillPath.AddRect(SKRect.Create(0, 0, 100, 100));
        using var stroke = PenHelper.CreateStrokePath(fillPath, penResource, new Rect(0, 0, 100, 100));
        using var node = new VideoSourceRenderNode(_resource!, frame: 0, null, penResource);
        using var renderer = new RenderNodeRenderer(node, new RenderNodeRenderRequest { Intent = RenderIntent.Preview });
        foreach (int x in new[] { -5, 5, 15, 35, 50, 85, 95, 105 })
            Assert.That(renderer.HitTest(new Point(x, 50)), Is.EqualTo(stroke.Contains(x, 50)), $"x={x}");
    }

    [SetUp]
    public void SetUp()
    {
        TestMediaHelper.RegisterTestDecoder();
        string path = TestMediaHelper.CreateTestVideoFile(100, 100, new Rational(30, 1), 30);
        _videoSource = new VideoSource();
        _videoSource.ReadFrom(new Uri(path));
        _resource = _videoSource.ToResource(CompositionContext.Default);
    }

    [TearDown]
    public void TearDown()
    {
        _resource?.Dispose();
        _resource = null;
        _videoSource = null;
    }

    // A decoded video frame reports concrete At(1) density, not Unbounded.
    [Test]
    public void Measure_ReportsConcreteNativeDensity_NotUnbounded()
    {
        using var node = new VideoSourceRenderNode(_resource!, frame: 0, Brushes.Resource.White, null);
        using var renderer = new RenderNodeRenderer(node, new RenderNodeRenderRequest
        {
            Intent = RenderIntent.Preview,
            CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
        });
        RenderNodeMeasurement measurement = renderer.Measure();

        Assert.That(measurement.HasFragments, Is.True);
        Assert.That(measurement.EffectiveScale.IsUnbounded, Is.False,
            "a video source must report a concrete density, not the vector Unbounded sentinel");
        Assert.That(measurement.EffectiveScale.Value, Is.EqualTo(1f),
            "a video frame drawn at its native 1:1 size has supply density 1");
    }
}
