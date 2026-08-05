using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Particles;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Recording;

// These nodes must record a child subtree before they can compute the bounds that decide whether
// there is anything to draw, so their degenerate-bounds bail-out abandons a subtree that already
// published target-effect fragments of its own.
[TestFixture]
public sealed class AbandonedRecordedSubtreeTests
{
    [Test]
    public void ParticleRenderNode_WithParticlesScaledToZero_RendersNothingWithoutFailing()
    {
        var particle = new RectShape();
        particle.Width.CurrentValue = 20;
        particle.Height.CurrentValue = 12;
        particle.Fill.CurrentValue = Brushes.White;

        var emitter = new ParticleEmitter();
        emitter.ParticleDrawable.CurrentValue = particle;
        emitter.MaxParticles.CurrentValue = 1;
        emitter.Speed.CurrentValue = 0;
        emitter.Gravity.CurrentValue = 0;
        emitter.ParticleSize.CurrentValue = 0;
        emitter.SizeRandom.CurrentValue = 0;
        using var resource = (ParticleEmitter.Resource)emitter.ToResource(
            new CompositionContext(TimeSpan.FromSeconds(1)));

        Assert.That(resource.GetAliveParticles().Length, Is.GreaterThanOrEqualTo(1),
            "precondition: the emitter must still hold alive particles, only sized to zero");

        using var node = new ParticleRenderNode(resource);
        using RenderNodeRasterization rasterization = Rasterize(node);

        Assert.That(rasterization.IsEmpty, Is.True,
            "a zero-sized particle set draws nothing, and the recorded particle-drawable subtree it "
            + "abandoned must not fail the recording");
    }

    [Test]
    public void DrawableBrush_WithZeroAreaContent_RendersTheOwnerWithoutFailing()
    {
        var content = new RectShape();
        content.Width.CurrentValue = 18;
        content.Height.CurrentValue = 12;
        content.Fill.CurrentValue = Brushes.White;
        var collapse = new ScaleTransform();
        collapse.Scale.CurrentValue = 0;
        content.Transform.CurrentValue = collapse;

        var brush = new DrawableBrush(content);
        using var brushResource = (Brush.Resource)brush.ToResource(CompositionContext.Default);
        using var node = new RectangleRenderNode(new Rect(0, 0, 32, 24), brushResource, null);
        using var owner = new RenderRequestOwner();
        using var request = new RenderRequest(new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            outputScale: 1,
            maxWorkingScale: 1,
            owner: owner));

        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(node);
        RenderFragmentReference root = GetSingleRoot(graph);

        Assert.Multiple(() =>
        {
            Assert.That(root.Kind, Is.EqualTo(RenderFragmentKind.OpaqueSource),
                "zero-area brush content is dropped from the paint, so the owner keeps no brush "
                + "dependency");
            Assert.That(root.Inputs, Is.Empty);
        });
    }

    private static RenderNodeRasterization Rasterize(RenderNode node)
    {
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
            });
        return renderer.Rasterize();
    }

    private static RenderFragmentReference GetSingleRoot(RecordedRenderGraph graph)
    {
        RenderFragmentId rootId = graph.PublicationRoots.Single();
        return (RenderFragmentReference)graph.Fragments
            .Single(fragment => fragment.Id == rootId)
            .Payload!;
    }
}
