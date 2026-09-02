using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Graphics.Shapes;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Recording;

[TestFixture]
public sealed class BrushSourceRecordingTests
{
    private static readonly Rect s_shape = new(0, 0, 40, 30);

    [TestCaseSource(nameof(Fills))]
    public void AFilledShapeRecordsOneRootFragmentOverItsOwnBounds(Func<Brush> fill)
    {
        var shape = new RectShape();
        shape.Width.CurrentValue = (float)s_shape.Width;
        shape.Height.CurrentValue = (float)s_shape.Height;
        shape.Fill.CurrentValue = fill();
        using var resource = (Drawable.Resource)shape.ToResource(CompositionContext.Default);
        using var root = new DrawableRenderNode(resource);
        using (var context = new GraphicsContext2D(root, s_shape.Size))
        {
            shape.Render(context, resource);
        }

        using var owner = new RenderRequestOwner();
        using RenderRequest request = CreateRequest(owner);
        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(root);
        RenderFragmentReference rootFragment = GetSingleRoot(graph);

        Assert.Multiple(() =>
        {
            Assert.That(graph.PublicationRoots, Has.Length.EqualTo(1),
                "A single filled shape must not fan out into several published roots.");
            Assert.That(graph.NestedRequests, Is.Empty,
                "Brush content is lowered into the same request.");
            Assert.That(rootFragment.Bounds, Is.EqualTo(s_shape));
        });
    }

    private static IEnumerable<TestCaseData> Fills()
    {
        yield return new TestCaseData(new Func<Brush>(static () => new SolidColorBrush(Colors.White)))
            .SetArgDisplayNames("solid");
        yield return new TestCaseData(new Func<Brush>(static () => new LinearGradientBrush()))
            .SetArgDisplayNames("gradient");
        yield return new TestCaseData(new Func<Brush>(MakeDrawableBrush)).SetArgDisplayNames("drawable");
    }

    private static Brush MakeDrawableBrush()
    {
        var content = new RectShape();
        content.Width.CurrentValue = 10;
        content.Height.CurrentValue = 10;
        content.Fill.CurrentValue = Brushes.White;
        var brush = new DrawableBrush(content);
        brush.Stretch.CurrentValue = Stretch.Fill;
        return brush;
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
