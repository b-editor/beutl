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
    public void Process_OpReportsConcreteNativeDensity_NotUnbounded()
    {
        var node = new VideoSourceRenderNode(_resource!, frame: 0, Brushes.Resource.White, null);
        var operations = node.Process(new Beutl.Graphics.Rendering.RenderNodeContext(
            [], Beutl.Graphics.Rendering.RenderIntent.Delivery));

        Assert.That(operations, Is.Not.Empty);
        Assert.That(operations[0].EffectiveScale.IsUnbounded, Is.False,
            "a video source must report a concrete density, not the vector Unbounded sentinel");
        Assert.That(operations[0].EffectiveScale.Value, Is.EqualTo(1f),
            "a video frame drawn at its native 1:1 size has supply density 1");
    }
}
