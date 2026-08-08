using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Source;
using Microsoft.Extensions.Logging;
using Moq;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

public class GraphicsContext2DTests
{
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        TestMediaHelper.RegisterTestDecoder();
    }

    [SetUp]
    public void Setup()
    {
        Log.LoggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole());
    }

    [Test]
    public void ShouldTriggerOnUntrackedEvent()
    {
        var drawable = new RectShape();
        drawable.AlignmentX.CurrentValue = AlignmentX.Center;
        drawable.AlignmentY.CurrentValue = AlignmentY.Center;
        drawable.TransformOrigin.CurrentValue = RelativePoint.Center;
        drawable.Width.CurrentValue = 100;
        drawable.Height.CurrentValue = 100;
        drawable.Fill.CurrentValue = Brushes.White;
        drawable.FilterEffect.CurrentValue = new FilterEffectGroup { Children = { new SplitEffect(), new InnerShadow() } };
        drawable.Transform.CurrentValue = new TransformGroup { Children = { new RotationTransform(), new ScaleTransform() } };
        var resource = drawable.ToResource(CompositionContext.Default);

        var node = new DrawableRenderNode(resource);
        using (var context = new GraphicsContext2D(node, new Size(1920, 1080)))
        {
            drawable.Render(context, resource);
        }

        ((FilterEffectGroup)drawable.FilterEffect.CurrentValue).Children.RemoveAt(0);
        var updateOnly = false;
        resource.Update(drawable, CompositionContext.Default, ref updateOnly);

        bool triggered = false;
        RenderNode? untrackedNode = null;
        using (var context2 = new GraphicsContext2D(node, new Size(1920, 1080)))
        {
            context2.OnUntracked = n =>
            {
                triggered = true;
                untrackedNode = n;
            };
            drawable.Render(context2, resource);
        }

        Assert.That(triggered, Is.True);
        Assert.That(untrackedNode, Is.Not.Null);
        Assert.That(untrackedNode, Is.TypeOf<FilterEffectRenderNode>());
    }

    [Test]
    public void DrawNodeUpdateFailure_PreservesTheExistingTree()
    {
        using var root = new ContainerRenderNode();
        var updated = new TrackingRenderNode();
        var trailing = new TrackingRenderNode();
        root.AddChild(updated);
        root.AddChild(trailing);

        using (var context = new GraphicsContext2D(root))
        {
            Assert.That(
                () => context.DrawNode(
                    0,
                    static _ => new TrackingRenderNode(),
                    static (_, _) => throw new InvalidOperationException("update failed")),
                Throws.InvalidOperationException.With.Message.EqualTo("update failed"));
        }

        Assert.Multiple(() =>
        {
            Assert.That(root.Children, Is.EqualTo(new[] { updated, trailing }));
            Assert.That(updated.IsDisposed, Is.False);
            Assert.That(trailing.IsDisposed, Is.False);
        });
    }

    [Test]
    public void Dispose_DischargesUnvisitedTrailingNodes()
    {
        using var root = new ContainerRenderNode();
        var retained = new TrackingRenderNode();
        var removed = new TrackingRenderNode();
        root.AddChild(retained);
        root.AddChild(removed);
        RenderNode? untracked = null;

        using (var context = new GraphicsContext2D(root))
        {
            context.OnUntracked = node => untracked = node;
            context.DrawNode(retained);
        }

        Assert.Multiple(() =>
        {
            Assert.That(root.Children, Is.EqualTo(new[] { retained }));
            Assert.That(root.HasChanges, Is.True);
            Assert.That(removed.IsDisposed, Is.True);
            Assert.That(untracked, Is.SameAs(removed));
        });
    }

    [Test]
    public void Dispose_DoesNotReplacePrimaryExceptionWithTrailingCleanupFailure()
    {
        using var root = new ContainerRenderNode();
        var retained = new TrackingRenderNode();
        var cleanup = new InvalidOperationException("trailing cleanup failed");
        var trailing = new ThrowOnceDisposeRenderNode(cleanup);
        root.AddChild(retained);
        root.AddChild(trailing);
        var primary = new InvalidOperationException("recording failed");

        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(() =>
        {
            using var context = new GraphicsContext2D(root);
            context.DrawNode(retained);
            throw primary;
        });

        Assert.Multiple(() =>
        {
            Assert.That(failure, Is.SameAs(primary));
            Assert.That(trailing.DisposeCalls, Is.EqualTo(1));
            Assert.That(root.Children, Is.EqualTo(new[] { retained }));
        });
    }

    [Test]
    public void DirectRecordingFailure_PreservesExistingTreeAndDisposesRejectedNode()
    {
        using var root = new ContainerRenderNode();
        var replacementFailure = new InvalidOperationException("existing node cleanup failed");
        var existing = new ThrowOnceDisposeRenderNode(replacementFailure);
        var trailing = new TrackingRenderNode();
        var rejected = new TrackingRenderNode();
        root.AddChild(existing);
        root.AddChild(trailing);

        using (var context = new GraphicsContext2D(root))
        {
            InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(
                () => context.DrawNode(rejected));
            Assert.That(failure, Is.SameAs(replacementFailure));
        }

        Assert.Multiple(() =>
        {
            Assert.That(root.Children, Is.EqualTo(new RenderNode[] { existing, trailing }));
            Assert.That(existing.IsDisposed, Is.False);
            Assert.That(trailing.IsDisposed, Is.False);
            Assert.That(rejected.IsDisposed, Is.True);
        });
    }

    [Test]
    public void NestedDrawableFailure_DiscardsFaultedNodeAndStaleSuffix()
    {
        using var root = new ContainerRenderNode();
        var retained = new TrackingRenderNode();
        var trailing = new TrackingRenderNode();
        var outerTrailing = new TrackingRenderNode();
        var primary = new InvalidOperationException("nested drawable failed");
        var drawable = new PartialFailureDrawable(retained, primary);
        using Drawable.Resource resource = drawable.ToResource(CompositionContext.Default);
        var nested = new DrawableRenderNode(resource);
        nested.AddChild(retained);
        nested.AddChild(trailing);
        root.AddChild(nested);
        root.AddChild(outerTrailing);

        InvalidOperationException? failure;
        using (var context = new GraphicsContext2D(root))
        {
            failure = Assert.Throws<InvalidOperationException>(
                () => context.DrawDrawable(resource));
        }

        Assert.Multiple(() =>
        {
            Assert.That(failure, Is.SameAs(primary));
            Assert.That(root.Children, Is.Empty);
            Assert.That(nested.IsDisposed, Is.True);
            Assert.That(retained.IsDisposed, Is.True);
            Assert.That(trailing.IsDisposed, Is.True);
            Assert.That(outerTrailing.IsDisposed, Is.True);
        });
    }

    [Test]
    public void Pop_ShouldPropagateAChangeFollowedByAnUnchangedSibling()
    {
        var size = new Size(1920, 1080);
        var fill = Brushes.Resource.White;
        var ellipse = new Rect(0, 0, 20, 20);
        using var root = new ContainerRenderNode();

        using (var context = new GraphicsContext2D(root, size))
        {
            using (context.Push())
            {
                context.DrawRectangle(new Rect(0, 0, 10, 10), fill, null);
                context.DrawEllipse(ellipse, fill, null);
            }
        }

        ClearHasChanges(root);

        using (var context = new GraphicsContext2D(root, size))
        {
            using (context.Push())
            {
                context.DrawRectangle(new Rect(0, 0, 30, 30), fill, null);
                context.DrawEllipse(ellipse, fill, null);
            }
        }

        Assert.That(root.HasChanges, Is.True);
    }

    [Test]
    public void Pop_ShouldNotMarkASiblingScopeWhoseSubtreeIsUnchanged()
    {
        var size = new Size(1920, 1080);
        using var root = new ContainerRenderNode();

        RecordTwoScopes(root, size, new Rect(0, 0, 10, 10));
        ClearHasChanges(root);
        RecordTwoScopes(root, size, new Rect(0, 0, 30, 30));

        var changedScope = (ContainerRenderNode)root.Children[0];
        var unchangedScope = (ContainerRenderNode)root.Children[1];
        Assert.Multiple(() =>
        {
            Assert.That(changedScope.HasChanges, Is.True);
            Assert.That(unchangedScope.HasChanges, Is.False);
        });
    }

    [Test]
    public void Pop_ShouldPropagateAChangeNestedTwoScopesDeepToTheRoot()
    {
        var size = new Size(1920, 1080);
        using var root = new ContainerRenderNode();

        RecordNestedRectangle(root, size, new Rect(0, 0, 10, 10));
        ClearHasChanges(root);
        RecordNestedRectangle(root, size, new Rect(0, 0, 30, 30));

        var outer = (ContainerRenderNode)root.Children[0];
        var inner = (ContainerRenderNode)outer.Children[0];
        Assert.Multiple(() =>
        {
            Assert.That(inner.HasChanges, Is.True);
            Assert.That(outer.HasChanges, Is.True);
            Assert.That(root.HasChanges, Is.True);
        });
    }

    [Test]
    public void Pop_ShouldMarkTheEnclosingContainerOfAStructuralInsertion()
    {
        var size = new Size(1920, 1080);
        var fill = Brushes.Resource.White;
        using var root = new ContainerRenderNode();

        using (var context = new GraphicsContext2D(root, size))
        using (context.Push())
        {
            context.DrawRectangle(new Rect(0, 0, 10, 10), fill, null);
        }

        ClearHasChanges(root);

        using (var context = new GraphicsContext2D(root, size))
        using (context.Push())
        {
            context.DrawRectangle(new Rect(0, 0, 10, 10), fill, null);
            context.DrawEllipse(new Rect(0, 0, 20, 20), fill, null);
        }

        var scope = (ContainerRenderNode)root.Children[0];
        Assert.That(scope.HasChanges, Is.True);
    }

    [Test]
    public void Dispose_ShouldMarkTheRootContainerOfABareParameterChange()
    {
        var size = new Size(1920, 1080);
        using var root = new ContainerRenderNode();

        RecordBareRectangle(root, size, new Rect(0, 0, 10, 10));
        ClearHasChanges(root);
        RecordBareRectangle(root, size, new Rect(0, 0, 30, 30));

        Assert.That(root.HasChanges, Is.True);
    }

    [Test]
    public void Update_ShouldNotClearALeafMarkFromAnEarlierPassInTheSameFrame()
    {
        var size = new Size(1920, 1080);
        var settled = new Rect(0, 0, 30, 30);
        using var root = new ContainerRenderNode();

        RecordBareEllipse(root, size, new Rect(0, 0, 10, 10));
        ClearHasChanges(root);
        RecordBareEllipse(root, size, settled);
        RecordBareEllipse(root, size, settled);

        var ellipse = (EllipseRenderNode)root.Children[0];
        Assert.That(ellipse.HasChanges, Is.True);
    }

    [Test]
    public void Update_ShouldNotClearAStructuralMarkFromAnEarlierPassInTheSameFrame()
    {
        var size = new Size(1920, 1080);
        Matrix matrix = Matrix.CreateRotation(45);
        using var root = new ContainerRenderNode();

        RecordTransformScope(root, size, matrix, withEllipse: true);
        ClearHasChanges(root);
        RecordTransformScope(root, size, matrix, withEllipse: false);
        RecordTransformScope(root, size, matrix, withEllipse: false);

        var scope = (TransformRenderNode)root.Children[0];
        Assert.That(scope.HasChanges, Is.True);
    }

    [Test]
    public void Reset_ShouldRestartRecordingAtTheRootContainer()
    {
        var size = new Size(1920, 1080);
        using var root = new ContainerRenderNode();

        using (var context = new GraphicsContext2D(root, size))
        {
            context.Push();
            context.Reset();
            context.DrawRectangle(new Rect(0, 0, 10, 10), Brushes.Resource.White, null);
        }

        Assert.That(root.Children, Has.Count.EqualTo(1));
        Assert.That(root.Children[0], Is.InstanceOf<RectangleRenderNode>());
    }

    private static void RecordBareRectangle(ContainerRenderNode root, Size size, Rect rect)
    {
        using var context = new GraphicsContext2D(root, size);
        context.DrawRectangle(rect, Brushes.Resource.White, null);
    }

    private static void RecordBareEllipse(ContainerRenderNode root, Size size, Rect rect)
    {
        using var context = new GraphicsContext2D(root, size);
        context.DrawEllipse(rect, Brushes.Resource.White, null);
    }

    private static void RecordTransformScope(
        ContainerRenderNode root, Size size, Matrix matrix, bool withEllipse)
    {
        var fill = Brushes.Resource.White;
        using var context = new GraphicsContext2D(root, size);
        using (context.PushTransform(matrix))
        {
            context.DrawRectangle(new Rect(0, 0, 10, 10), fill, null);
            if (withEllipse)
                context.DrawEllipse(new Rect(0, 0, 20, 20), fill, null);
        }
    }

    private static void RecordTwoScopes(ContainerRenderNode root, Size size, Rect changing)
    {
        var fill = Brushes.Resource.White;
        using var context = new GraphicsContext2D(root, size);
        using (context.Push())
        using (context.Push())
        {
            context.DrawRectangle(changing, fill, null);
        }

        using (context.Push())
        using (context.Push())
        {
            context.DrawEllipse(new Rect(0, 0, 20, 20), fill, null);
        }
    }

    private static void RecordNestedRectangle(ContainerRenderNode root, Size size, Rect rect)
    {
        using var context = new GraphicsContext2D(root, size);
        using (context.Push())
        using (context.Push())
        {
            context.DrawRectangle(rect, Brushes.Resource.White, null);
        }
    }

    private static void ClearHasChanges(RenderNode node)
    {
        node.HasChanges = false;
        if (node is ContainerRenderNode container)
        {
            foreach (RenderNode child in container.Children)
                ClearHasChanges(child);
        }
    }

    [Test]
    public void Clear_ShouldCreateClearRenderNode()
    {
        var node = new ContainerRenderNode();
        var context = new GraphicsContext2D(node, new Size(1920, 1080));

        context.Clear();

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.InstanceOf<ClearRenderNode>());
    }

    private sealed class TrackingRenderNode : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            context.PassThrough();
        }
    }

    private sealed class ThrowOnceDisposeRenderNode(Exception failure) : RenderNode
    {
        public int DisposeCalls { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            context.PassThrough();
        }

        protected override void OnDispose(bool disposing)
        {
            DisposeCalls++;
            if (disposing && DisposeCalls == 1)
                throw failure;
        }
    }

    [Test]
    public void ClearWithColor_ShouldCreateClearRenderNode()
    {
        var node = new ContainerRenderNode();
        var context = new GraphicsContext2D(node, new Size(1920, 1080));

        context.Clear(Colors.White);

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.InstanceOf<ClearRenderNode>());
        Assert.That(((ClearRenderNode)node.Children[0]).Color, Is.EqualTo(Colors.White));
    }

    [Test]
    public void DrawImageSource_ShouldCreateImageSourceRenderNode()
    {
        var node = new ContainerRenderNode();
        var context = new GraphicsContext2D(node, new Size(1920, 1080));

        var imageUri = TestMediaHelper.CreateTestImageUri(100, 100, Colors.White);
        var imageSource = new ImageSource();
        imageSource.ReadFrom(imageUri);
        using var imageResource = imageSource.ToResource(CompositionContext.Default);

        context.DrawImageSource(imageResource, Brushes.Resource.White, null);

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.InstanceOf<ImageSourceRenderNode>());
    }

    [Test]
    public void DrawVideoSource_ShouldCreateVideoSourceRenderNode()
    {
        var node = new ContainerRenderNode();
        var context = new GraphicsContext2D(node, new Size(1920, 1080));

        var videoPath = TestMediaHelper.CreateTestVideoFile(100, 100, new Rational(30), 300);
        var videoSource = new VideoSource();
        videoSource.ReadFrom(new Uri(videoPath));
        using var videoResource = videoSource.ToResource(CompositionContext.Default);

        context.DrawVideoSource(videoResource, TimeSpan.Zero, Brushes.Resource.White, null);

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.InstanceOf<VideoSourceRenderNode>());
    }

    [Test]
    public void DrawEllipse_ShouldCreateEllipseRenderNode()
    {
        var node = new ContainerRenderNode();
        var context = new GraphicsContext2D(node, new Size(1920, 1080));

        context.DrawEllipse(new Rect(0, 0, 100, 100), Brushes.Resource.White, null);

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.InstanceOf<EllipseRenderNode>());
    }

    [Test]
    public void DrawGeometry_ShouldCreateGeometryRenderNode()
    {
        var node = new ContainerRenderNode();
        var context = new GraphicsContext2D(node, new Size(1920, 1080));
        var geometry = new EllipseGeometry();
        geometry.Width.CurrentValue = 100;
        geometry.Height.CurrentValue = 100;
        var resource = geometry.ToResource(CompositionContext.Default);

        context.DrawGeometry(resource, Brushes.Resource.White, null);

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.InstanceOf<GeometryRenderNode>());
    }

    [Test]
    public void DrawRectangle_ShouldCreateRectangleRenderNode()
    {
        var node = new ContainerRenderNode();
        var context = new GraphicsContext2D(node, new Size(1920, 1080));

        context.DrawRectangle(new Rect(0, 0, 100, 100), Brushes.Resource.White, null);

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.InstanceOf<RectangleRenderNode>());
    }

    [Test]
    public void DrawDrawable_ShouldCreateDrawableRenderNode()
    {
        var drawable = new RectShape();
        var resource = drawable.ToResource(CompositionContext.Default);
        var node = new ContainerRenderNode();
        var context = new GraphicsContext2D(node, new Size(1920, 1080));

        context.DrawDrawable(resource);

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.InstanceOf<DrawableRenderNode>());
    }

    [Test]
    public void DrawNode_ShouldAddPassedNodeDirectly()
    {
        var node = new ContainerRenderNode();
        var context = new GraphicsContext2D(node, new Size(1920, 1080));
        var child = new ContainerRenderNode();

        context.DrawNode(child);

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.EqualTo(child));
    }

    [Test]
    public void DrawBackdrop_ShouldCreateDrawBackdropRenderNode()
    {
        var node = new ContainerRenderNode();
        var context = new GraphicsContext2D(node, new Size(1920, 1080));
        var backdrop = new Mock<IBackdrop>();

        context.DrawBackdrop(backdrop.Object);

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.InstanceOf<DrawBackdropRenderNode>());
    }

    [Test]
    public void Snapshot_ShouldCreateSnapshotBackdropRenderNode()
    {
        var node = new ContainerRenderNode();
        var context = new GraphicsContext2D(node, new Size(1920, 1080));

        _ = context.Snapshot();

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.InstanceOf<SnapshotBackdropRenderNode>());
    }

    [Test]
    public void Push_ShouldCreatePushRenderNode()
    {
        var node = new ContainerRenderNode();
        var context = new GraphicsContext2D(node, new Size(1920, 1080));

        context.Push().Dispose();

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.InstanceOf<PushRenderNode>());
    }

    [Test]
    public void PushLayer_ShouldCreateLayerRenderNode()
    {
        var node = new ContainerRenderNode();
        var context = new GraphicsContext2D(node, new Size(1920, 1080));

        context.PushLayer().Dispose();

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.InstanceOf<LayerRenderNode>());
    }

    [Test]
    public void PushBlendMode_ShouldCreateBlendModeRenderNode()
    {
        var node = new ContainerRenderNode();
        var context = new GraphicsContext2D(node, new Size(1920, 1080));

        context.PushBlendMode(BlendMode.Clear).Dispose();

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.InstanceOf<BlendModeRenderNode>());
    }

    [Test]
    public void PushClip_ShouldCreateRectClipRenderNode()
    {
        var node = new ContainerRenderNode();
        var context = new GraphicsContext2D(node, new Size(1920, 1080));

        context.PushClip(new Rect(0, 0, 100, 100)).Dispose();

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.InstanceOf<RectClipRenderNode>());
    }

    [Test]
    public void PushClipGeometry_ShouldCreateGeometryClipRenderNode()
    {
        var node = new ContainerRenderNode();
        var context = new GraphicsContext2D(node, new Size(1920, 1080));
        var geometry = new EllipseGeometry();
        geometry.Width.CurrentValue = 100;
        geometry.Height.CurrentValue = 100;
        var resource = geometry.ToResource(CompositionContext.Default);

        context.PushClip(resource).Dispose();

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.InstanceOf<GeometryClipRenderNode>());
    }

    [Test]
    public void PushOpacity_ShouldCreateOpacityRenderNode()
    {
        var node = new ContainerRenderNode();
        var context = new GraphicsContext2D(node, new Size(1920, 1080));

        context.PushOpacity(0.5f).Dispose();

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.InstanceOf<OpacityRenderNode>());
    }

    [Test]
    public void PushFilterEffect_ShouldCreateFilterEffectRenderNode()
    {
        var node = new ContainerRenderNode();
        var context = new GraphicsContext2D(node, new Size(1920, 1080));
        var effect = new Blur();
        var resource = effect.ToResource(CompositionContext.Default);

        context.PushFilterEffect(resource).Dispose();

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.InstanceOf<FilterEffectRenderNode>());
    }

    [Test]
    public void PushOpacityMask_ShouldCreateOpacityMaskRenderNode()
    {
        var node = new ContainerRenderNode();
        var context = new GraphicsContext2D(node, new Size(1920, 1080));
        var mask = Brushes.Resource.White;

        context.PushOpacityMask(mask, new Rect(0, 0, 100, 100)).Dispose();

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.InstanceOf<OpacityMaskRenderNode>());
    }

    [Test]
    public void PushTransform_ShouldCreateTransformRenderNode()
    {
        var node = new ContainerRenderNode();
        var context = new GraphicsContext2D(node, new Size(1920, 1080));
        var transform = new RotationTransform();
        var resource = transform.ToResource(CompositionContext.Default);

        context.PushTransform(resource).Dispose();

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.InstanceOf<TransformRenderNode>());
    }

    [Test]
    public void PushTransformGroup_ShouldCreateTransformRenderNode()
    {
        var node = new ContainerRenderNode();
        var context = new GraphicsContext2D(node, new Size(1920, 1080));
        var transform = new TransformGroup { Children = { new RotationTransform(), new ScaleTransform() } };
        var resource = transform.ToResource(CompositionContext.Default);

        context.PushTransform(resource).Dispose();

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.InstanceOf<TransformRenderNode>());
    }

    [Test]
    public void PushMatrixTransform_ShouldCreateTransformRenderNode()
    {
        var node = new ContainerRenderNode();
        var context = new GraphicsContext2D(node, new Size(1920, 1080));
        var matrix = Matrix.CreateRotation(45);

        context.PushTransform(matrix).Dispose();

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.InstanceOf<TransformRenderNode>());
    }
}

internal sealed partial class PartialFailureDrawable(
    RenderNode child,
    Exception failure) : Drawable
{
    public override void Render(GraphicsContext2D context, Drawable.Resource resource)
    {
        context.DrawNode(child);
        throw failure;
    }

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource)
        => Size.Empty;

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
    }
}
