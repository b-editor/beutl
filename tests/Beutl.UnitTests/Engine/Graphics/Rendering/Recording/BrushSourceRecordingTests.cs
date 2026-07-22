using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Recording;

[TestFixture]
public sealed class BrushSourceRecordingTests
{
    [Test]
    public void RawExternalBrush_RemovesEngineDirectReplay()
    {
        var brush = new FallbackBrush();
        using var brushResource = (Brush.Resource)brush.ToResource(CompositionContext.Default);
        using var node = new RectangleRenderNode(new Rect(0, 0, 32, 24), brushResource, null);
        using var owner = new RenderRequestOwner();
        using var request = CreateRequest(owner);

        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(node);
        RenderFragmentReference root = GetSingleRoot(graph);
        var payload = (OpaqueRenderFragmentPayload)root.Payload!;

        Assert.Multiple(() =>
        {
            Assert.That(root.Kind, Is.EqualTo(RenderFragmentKind.OpaqueSource));
            Assert.That(root.Inputs, Is.Empty);
            Assert.That(root.HasOpaqueExternalWork, Is.True);
            Assert.That(payload.Topology, Is.EqualTo(OpaqueRenderTopology.Source));
            Assert.That(payload.Description.DirectReplay, Is.Null,
                "An arbitrary Brush implementation must never enter the engine-owned direct replay path.");
        });
    }

    [Test]
    public void DrawableBrush_RecordsMaterializedInputForEngineDirectReplay()
    {
        var content = new RectShape();
        content.Width.CurrentValue = 18;
        content.Height.CurrentValue = 12;
        content.Fill.CurrentValue = Brushes.White;
        var brush = new DrawableBrush(content);
        brush.Stretch.CurrentValue = Stretch.Fill;
        using var brushResource = (Brush.Resource)brush.ToResource(CompositionContext.Default);
        using var node = new RectangleRenderNode(new Rect(0, 0, 32, 24), brushResource, null);
        using var owner = new RenderRequestOwner();
        using var request = CreateRequest(owner);

        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(node);
        RenderFragmentReference root = GetSingleRoot(graph);
        var payload = (OpaqueRenderFragmentPayload)root.Payload!;

        Assert.Multiple(() =>
        {
            Assert.That(root.Kind, Is.EqualTo(RenderFragmentKind.OpaqueCombine));
            Assert.That(root.Inputs, Has.Length.EqualTo(1),
                "DrawableBrush content must remain an explicit materializable graph dependency.");
            Assert.That(root.Inputs[0].CanBeUsedAsValueInput, Is.True);
            Assert.That(root.HasOpaqueExternalWork, Is.True);
            Assert.That(payload.Topology, Is.EqualTo(OpaqueRenderTopology.Combine));
            Assert.That(payload.Description.DirectReplay, Is.Not.Null,
                "Engine-owned source replay may consume its explicit materialized brush inputs.");
        });
    }

    private static RenderRequest CreateRequest(RenderRequestOwner owner)
        => new(new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            outputScale: 1,
            maxWorkingScale: 1,
            owner: owner));

    private static RenderFragmentReference GetSingleRoot(RecordedRenderGraph graph)
    {
        RenderFragmentId rootId = graph.PublicationRoots.Single();
        return (RenderFragmentReference)graph.Fragments
            .Single(fragment => fragment.Id == rootId)
            .Payload!;
    }
}
