using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Graphics3D;
using Beutl.Media;
using Beutl.Media.Source;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

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
        node.MarkChanged();

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

    /// <summary>
    /// Compiles and exercises the invalidation pattern the 004 migration contract hands an out-of-tree
    /// author to copy, so the documented code cannot drift away from the API it is compiled against.
    /// </summary>
    [Test]
    public void TheDocumentedInvalidationPattern_ReportsOnlyARealChange()
    {
        using var node = new DocumentedOpacityNode();
        Assert.That(node.HasChanges, Is.False, "a node nobody has written to has nothing to re-record");

        node.Opacity = 0.25f;
        Assert.That(node.HasChanges, Is.True, "the setter has to report the change it just made");

        node.ClearChanges(node.ChangeVersion);
        node.Opacity = 0.25f;

        Assert.That(node.HasChanges, Is.False, "assigning the value the node already holds is not a change");
    }

    /// <summary>The node shape <c>contracts/breaking-changes.md</c> documents as the migration target.</summary>
    private sealed class DocumentedOpacityNode : RenderNode
    {
        private float _opacity = 1f;

        public float Opacity
        {
            get => _opacity;
            set
            {
                if (_opacity == value)
                    return;

                _opacity = value;
                MarkChanged();
            }
        }

        public override void Process(RenderNodeContext context)
        {
            context.Publish(context.Opacity(context.Inputs[0], _opacity));
        }
    }

    public sealed record NoOpUpdateCase(RenderNode Node, Func<bool> NoOpUpdate);
}
