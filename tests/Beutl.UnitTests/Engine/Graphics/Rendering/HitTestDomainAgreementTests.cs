using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

// Hit testing answers "is there content here", so it has to agree with what the same request would
// actually put on screen. Both a request's TargetDomain and a finite Layer's domain clip the resolved
// output, and these pin that the hit test is clipped with it.
[TestFixture]
public sealed class HitTestDomainAgreementTests
{
    private static readonly Rect s_targetDomain = new(0, 0, 100, 100);

    [Test]
    public void ARequestTargetDomainClipsTheHitTestTheWayItClipsTheOutput()
    {
        using var fill = new SolidColorBrush(Colors.Red).ToResource(CompositionContext.Default);
        using var node = new EllipseRenderNode(new Rect(200, 0, 100, 80), fill, null);
        using var renderer = CreateRenderer(node);

        RenderNodeMeasurement measurement = renderer.Measure();
        using RenderNodeRasterization rasterization = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(measurement.OutputBounds.IsEmpty, Is.True, "the domain excludes the ellipse entirely");
            Assert.That(rasterization.IsEmpty, Is.True, "nothing is rasterized");
            Assert.That(renderer.HitTest(new Point(250, 40)), Is.False, "so nothing can be hit there either");
        });
    }

    [Test]
    public void ARequestTargetDomainStillHitsTheContentItKeeps()
    {
        using var fill = new SolidColorBrush(Colors.Red).ToResource(CompositionContext.Default);
        using var node = new EllipseRenderNode(new Rect(0, 0, 100, 80), fill, null);
        using var renderer = CreateRenderer(node);

        Assert.That(renderer.HitTest(new Point(50, 40)), Is.True);
    }

    [Test]
    public void AFiniteLayerDoesNotHitWhatItsDomainClipsAway()
    {
        using var node = new ClippedLayerNode();
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    OutputScale = 1f,
                    CacheOptions = RenderCacheOptions.Disabled,
                },
            });

        RenderNodeMeasurement measurement = renderer.Measure();

        Assert.Multiple(() =>
        {
            Assert.That(
                measurement.OutputBounds,
                Is.EqualTo(new Rect(50, 0, 50, 100)),
                "the layer bounds stop at the domain");
            Assert.That(renderer.HitTest(new Point(60, 50)), Is.True, "inside both the input and the domain");
            Assert.That(renderer.HitTest(new Point(140, 50)), Is.False, "inside the input, outside the domain");
        });
    }

    private static RenderNodeRenderer CreateRenderer(RenderNode node)
        => new(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    OutputScale = 1f,
                    TargetDomain = s_targetDomain,
                    CacheOptions = RenderCacheOptions.Disabled,
                },
            });

    private sealed class ClippedLayerNode : RenderNode
    {
        private static readonly Rect s_inputBounds = new(50, 0, 100, 100);

        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle source = context.OpaqueSource(OpaqueRenderDescription.CreateEngineSource(
                execute: static session =>
                {
                    using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                    output.Canvas.Use(static canvas => canvas.Clear(Colors.White));
                    session.Publish(output);
                },
                directReplay: null,
                bounds: OpaqueRenderBoundsContract.Source(s_inputBounds),
                hitTest: RenderHitTestContract.OutputBounds,
                scale: RenderScaleContract.MaterializeAtWorkingScale,
                deviceGridSensitivity: RenderDeviceGridSensitivity.Insensitive));
            context.Publish(context.Layer([source], s_targetDomain));
        }
    }
}
