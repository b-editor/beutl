using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Media.Source;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Recording;

[TestFixture]
public sealed class OpaqueRenderDescriptionDirectReplayTests
{
    [Test]
    public void VectorPaintedSource_WithPlainBrush_DeclaresDirectMaterialization()
    {
        using var node = new RectangleRenderNode(
            new Rect(2, 3, 24, 16),
            Brushes.Resource.White,
            null);

        RecordSingleOpaqueSource(node, static (reference, description) =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(description.DirectReplay, Is.Not.Null);
                Assert.That(description.HasDirectReplayMaterializationContract, Is.True);
                Assert.That(reference.HasOpaqueExternalWork, Is.False);
            });
        });
    }

    [Test]
    public void VectorPaintedSource_WithDrawableBrush_RequiresOpaqueExternalWork()
    {
        using Brush.Resource brush = CreateDrawableBrush();
        using var node = new RectangleRenderNode(
            new Rect(2, 3, 24, 16),
            brush,
            null);

        RecordSingleOpaqueSource(node, static (reference, description) =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(description.DirectReplay, Is.Null);
                Assert.That(description.HasDirectReplayMaterializationContract, Is.False);
                Assert.That(reference.HasOpaqueExternalWork, Is.True);
            });
        });
    }

    [Test]
    public void ConcreteImageSource_DirectReplayDoesNotDeclareDirectMaterialization()
    {
        var imageSource = new ImageSource();
        imageSource.ReadFrom(TestMediaHelper.CreateTestImageUri(24, 16, Colors.White));
        using ImageSource.Resource source = imageSource.ToResource(CompositionContext.Default);
        using var node = new ImageSourceRenderNode(source, Brushes.Resource.White, null);

        RecordSingleOpaqueSource(node, static (reference, description) =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(description.DirectReplay, Is.Not.Null);
                Assert.That(description.HasDirectReplayMaterializationContract, Is.False);
                Assert.That(reference.HasOpaqueExternalWork, Is.True);
            });
        });
    }

    [Test]
    public void WithoutDirectReplay_ClearsDirectMaterializationContract()
    {
        using var node = new RectangleRenderNode(
            new Rect(2, 3, 24, 16),
            Brushes.Resource.White,
            null);

        RecordSingleOpaqueSource(node, static (_, description) =>
        {
            OpaqueRenderDescription withoutDirectReplay = description.WithoutDirectReplay();

            Assert.Multiple(() =>
            {
                Assert.That(description.DirectReplay, Is.Not.Null);
                Assert.That(description.HasDirectReplayMaterializationContract, Is.True);
                Assert.That(withoutDirectReplay, Is.Not.SameAs(description));
                Assert.That(withoutDirectReplay.DirectReplay, Is.Null);
                Assert.That(withoutDirectReplay.HasDirectReplayMaterializationContract, Is.False);
            });
        });
    }

    private static Brush.Resource CreateDrawableBrush()
    {
        var content = new RectShape();
        content.Width.CurrentValue = 8;
        content.Height.CurrentValue = 8;
        content.Fill.CurrentValue = Brushes.White;
        var brush = new DrawableBrush(content);
        brush.Stretch.CurrentValue = Stretch.Fill;
        return brush.ToResource(CompositionContext.Default);
    }

    private static void RecordSingleOpaqueSource(
        RenderNode node,
        Action<RenderFragmentReference, OpaqueRenderDescription> assert)
    {
        using var owner = new RenderRequestOwner();
        using var request = new RenderRequest(new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            targetDomain: new Rect(0, 0, 64, 64),
            outputScale: 1,
            maxWorkingScale: 1,
            owner: owner));

        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(node);
        RenderFragmentId rootId = graph.PublicationRoots.Single();
        RenderFragmentReference reference = graph.GetFragment(rootId);
        var payload = (OpaqueRenderFragmentPayload)reference.Payload!;

        Assert.That(reference.Kind, Is.EqualTo(RenderFragmentKind.OpaqueSource));
        assert(reference, payload.Description);
    }
}
