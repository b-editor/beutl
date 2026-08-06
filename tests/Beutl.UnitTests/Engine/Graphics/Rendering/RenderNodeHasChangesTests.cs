using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Graphics3D;
using Beutl.Media;
using Beutl.Media.Source;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

// Renderer.RevalidateAll owns the only clear of HasChanges, after IncrementRenderCount consumes it.
// Update is one of several writers into the same frame, so it may only ever set the flag.
[TestFixture]
public class RenderNodeHasChangesTests
{
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        TestMediaHelper.RegisterTestDecoder();
    }

    public static IEnumerable<TestCaseData> NoOpUpdateCases()
    {
        yield return new TestCaseData((Func<NoOpUpdateCase>)EllipseCase).SetName("EllipseRenderNode");
        yield return new TestCaseData((Func<NoOpUpdateCase>)RectangleCase).SetName("RectangleRenderNode");
        yield return new TestCaseData((Func<NoOpUpdateCase>)GeometryCase).SetName("GeometryRenderNode");
        yield return new TestCaseData((Func<NoOpUpdateCase>)TransformCase).SetName("TransformRenderNode");
        yield return new TestCaseData((Func<NoOpUpdateCase>)CustomTransformCase).SetName("CustomTransformRenderNode");
        yield return new TestCaseData((Func<NoOpUpdateCase>)ImageSourceCase).SetName("ImageSourceRenderNode");
        yield return new TestCaseData((Func<NoOpUpdateCase>)VideoSourceCase).SetName("VideoSourceRenderNode");
        yield return new TestCaseData((Func<NoOpUpdateCase>)Scene3DCase).SetName("Scene3DRenderNode");
    }

    [TestCaseSource(nameof(NoOpUpdateCases))]
    public void Update_ShouldNotClearAMarkLeftByAnotherWriter(Func<NoOpUpdateCase> factory)
    {
        NoOpUpdateCase testCase = factory();
        using RenderNode node = testCase.Node;
        node.HasChanges = true;

        bool changed = testCase.NoOpUpdate();

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.False, "the update was supposed to be a no-op");
            Assert.That(node.HasChanges, Is.True);
        });
    }

    private static NoOpUpdateCase EllipseCase()
    {
        var rect = new Rect(0, 0, 100, 100);
        var fill = Brushes.Resource.White;
        var node = new EllipseRenderNode(rect, fill, null);
        return new NoOpUpdateCase(node, () => node.Update(rect, fill, null));
    }

    private static NoOpUpdateCase RectangleCase()
    {
        var rect = new Rect(0, 0, 100, 100);
        var fill = Brushes.Resource.White;
        var node = new RectangleRenderNode(rect, fill, null);
        return new NoOpUpdateCase(node, () => node.Update(rect, fill, null));
    }

    private static NoOpUpdateCase GeometryCase()
    {
        var geometry = new EllipseGeometry();
        geometry.Width.CurrentValue = 100;
        geometry.Height.CurrentValue = 100;
        var resource = (Geometry.Resource)geometry.ToResource(CompositionContext.Default);
        var fill = Brushes.Resource.White;
        var node = new GeometryRenderNode(resource, fill, null);
        return new NoOpUpdateCase(node, () => node.Update(resource, fill, null));
    }

    private static NoOpUpdateCase TransformCase()
    {
        Matrix matrix = Matrix.CreateRotation(45);
        var node = new TransformRenderNode(matrix, TransformOperator.Prepend);
        return new NoOpUpdateCase(node, () => node.Update(matrix, TransformOperator.Prepend));
    }

    private static NoOpUpdateCase CustomTransformCase()
    {
        var bounds = new MemoryNode<Rect>(new Rect(0, 0, 100, 100));
        var screenSize = new Size(1920, 1080);
        var node = new DrawableGroup.CustomTransformRenderNode(
            null, RelativePoint.Center, screenSize, AlignmentX.Center, AlignmentY.Center, bounds);
        return new NoOpUpdateCase(
            node,
            () => node.Update(null, RelativePoint.Center, screenSize, AlignmentX.Center, AlignmentY.Center, bounds));
    }

    private static NoOpUpdateCase ImageSourceCase()
    {
        var imageSource = new ImageSource();
        imageSource.ReadFrom(TestMediaHelper.CreateTestImageUri(100, 100, Colors.White));
        var resource = (ImageSource.Resource)imageSource.ToResource(CompositionContext.Default);
        var fill = Brushes.Resource.White;
        var node = new ImageSourceRenderNode(resource, fill, null);
        return new NoOpUpdateCase(node, () => node.Update(resource, fill, null));
    }

    private static NoOpUpdateCase VideoSourceCase()
    {
        var videoSource = new VideoSource();
        videoSource.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(100, 100, new Rational(30), 300)));
        var resource = (VideoSource.Resource)videoSource.ToResource(CompositionContext.Default);
        var fill = Brushes.Resource.White;
        var node = new VideoSourceRenderNode(resource, 0, fill, null);
        return new NoOpUpdateCase(node, () => node.Update(resource, 0, fill, null));
    }

    private static NoOpUpdateCase Scene3DCase()
    {
        var scene = new Scene3D();
        scene.RenderWidth.CurrentValue = 32;
        scene.RenderHeight.CurrentValue = 32;
        var resource = (Scene3D.Resource)scene.ToResource(CompositionContext.Default);
        var node = new Scene3DRenderNode(resource);
        return new NoOpUpdateCase(node, () => node.Update(resource));
    }

    public sealed record NoOpUpdateCase(RenderNode Node, Func<bool> NoOpUpdate);
}
