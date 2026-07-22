using Beutl.Composition;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Source;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
public class VideoSourceRenderNodeTest
{
    private VideoSource? _videoSource;
    private VideoSource.Resource? _resource;

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
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions { UseRenderCache = false });
        RenderNodeMeasurement measurement = renderer.Measure();

        Assert.That(measurement.HasFragments, Is.True);
        Assert.That(measurement.EffectiveScale.IsUnbounded, Is.False,
            "a video source must report a concrete density, not the vector Unbounded sentinel");
        Assert.That(measurement.EffectiveScale.Value, Is.EqualTo(1f),
            "a video frame drawn at its native 1:1 size has supply density 1");
    }
}
