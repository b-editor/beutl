using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Graphics.Shaders;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Planning;

// A shader graph runs at the density of its densest input. Recording and the region analysis read the same inputs: the
// source, and each picture the graph declared besides it.
public sealed class ShaderGraphPlannedScaleTests
{
    [Test]
    public void AGraphIsPlannedAtTheDensityOfAPictureDenserThanItsSource()
    {
        var bounds = new Rect(0, 0, 100, 100);
        using var owner = new RenderRequestOwner();
        using var request = new RenderRequest(new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            targetDomain: bounds,
            owner: owner));
        var transaction = new NodeRecordingTransaction(new RenderRequestRecorder(request), new object(), []);
        var context = new RenderNodeContext(transaction);
        RenderFragmentHandle source = context.OpaqueSource(Source(bounds, RenderScaleContract.Vector));
        RenderFragmentHandle picture = context.OpaqueSource(Source(bounds, RenderScaleContract.Custom(static _ => 2f)));

        RenderFragmentReference graph = transaction.GetReference(
            context.ShaderGraph(source, ReadingGraph(), new[] { picture }));
        var payload = (ShaderGraphRenderFragmentPayload)graph.Payload!;

        Assert.Multiple(() =>
        {
            Assert.That(graph.RecordedEffectiveScale, Is.EqualTo(EffectiveScale.At(2)));
            // The density the region analysis resolves the same fragment to, from its inputs.
            Assert.That(graph.RecordedEffectiveScale, Is.EqualTo(ShaderGraphRenderFragmentPayload.ResolvePlannedScale(
                payload.Description, payload.WorkingScalePolicy, graph.Inputs, 1, float.PositiveInfinity)));
        });
    }

    private static OpaqueRenderDescription Source(Rect bounds, RenderScaleContract scale)
        => OpaqueRenderDescription.CreateRequestLocal(
            static _ => { },
            OpaqueRenderBoundsContract.Source(bounds),
            RenderHitTestContract.None,
            RenderValueCardinality.Single,
            scale);

    // Draws the picture over the source.
    private static ShaderGraphDescription ReadingGraph()
        => ShaderGraphBuilder.Build(0, static (graph, _) =>
        {
            ShaderImage picture = graph.ExternalImage("picture");
            ShaderImage output = graph.Image2D("output", graph.Source);
            graph.Fullscreen("read", ShaderProgram.Fullscreen(new ShaderSource("""
                #version 450
                layout(binding = 0) uniform sampler2D picture;
                layout(location = 0) out vec4 color;
                void main() { color = texture(picture, vec2(0.5)); }
                """)), pass =>
            {
                pass.SampledImage(0, picture);
                pass.ColorAttachment(0, output);
            });
            return graph.Output(output);
        });
}
