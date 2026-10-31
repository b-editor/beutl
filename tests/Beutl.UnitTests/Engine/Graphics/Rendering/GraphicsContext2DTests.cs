using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.Media.Source;
using Moq;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

public class GraphicsContext2DTests
{
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        TestMediaHelper.RegisterTestDecoder();
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

        using (var context = new GraphicsContext2D(root))
        {
            context.DrawNode(retained);
        }

        Assert.Multiple(() =>
        {
            Assert.That(root.Children, Is.EqualTo(new[] { retained }));
            Assert.That(root.HasChanges, Is.True);
            Assert.That(removed.IsDisposed, Is.True);
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
    public void DirectRecordingFailure_KeepsTheInstalledReplacementWhenTheOldChildFailsToDispose()
    {
        using var root = new ContainerRenderNode();
        var replacementFailure = new InvalidOperationException("existing node cleanup failed");
        var existing = new ThrowOnceDisposeRenderNode(replacementFailure);
        var trailing = new TrackingRenderNode();
        var replacement = new TrackingRenderNode();
        root.AddChild(existing);
        root.AddChild(trailing);

        using (var context = new GraphicsContext2D(root))
        {
            InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(
                () => context.DrawNode(replacement));
            Assert.That(failure, Is.SameAs(replacementFailure));
        }

        Assert.Multiple(() =>
        {
            Assert.That(root.Children, Is.EqualTo(new RenderNode[] { replacement, trailing }));
            Assert.That(replacement.IsDisposed, Is.False);
            Assert.That(trailing.IsDisposed, Is.False);
            Assert.That(existing.DisposeCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public void DirectRecordingFailure_NeverLeavesAChildWhoseDisposalAlreadyRan()
    {
        using var root = new ContainerRenderNode();
        var existing = new ReleaseThenThrowRenderNode();
        var replacement = new TrackingRenderNode();
        root.AddChild(existing);

        using (var context = new GraphicsContext2D(root))
        {
            Assert.Throws<InvalidOperationException>(() => context.DrawNode(replacement));
        }

        Assert.Multiple(() =>
        {
            Assert.That(existing.Released, Is.True);
            Assert.That(root.Children, Does.Not.Contain(existing));
            Assert.That(root.Children, Is.EqualTo(new RenderNode[] { replacement }));
            Assert.That(replacement.IsDisposed, Is.False);
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

        root.SettleConstruction();

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
        root.SettleConstruction();
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
        root.SettleConstruction();
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

        root.SettleConstruction();

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
    public void PushFilterEffect_AFlattenedGroupClosesExactlyTheScopesOfItsChildren()
    {
        using var root = new ContainerRenderNode();
        using var twoChildren = new FilterEffectGroup { Children = { new Blur(), new Blur() } }
            .ToResource(CompositionContext.Default);
        using var noChildren = new FilterEffectGroup().ToResource(CompositionContext.Default);

        using var context = new GraphicsContext2D(root, new Size(32, 24));
        using (context.Push())
        {
            int depth = context.Depth;
            using (context.PushFilterEffect(twoChildren))
            {
                Assert.That(context.Depth, Is.EqualTo(depth + 2));
            }

            Assert.That(context.Depth, Is.EqualTo(depth));
            using (context.PushFilterEffect(noChildren))
            {
                Assert.That(context.Depth, Is.EqualTo(depth));
            }

            Assert.That(context.Depth, Is.EqualTo(depth));
        }
    }

    [Test]
    public void Dispose_ShouldMarkTheRootContainerOfABareParameterChange()
    {
        var size = new Size(1920, 1080);
        using var root = new ContainerRenderNode();

        RecordBareRectangle(root, size, new Rect(0, 0, 10, 10));
        root.SettleConstruction();
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
        root.SettleConstruction();
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
        root.SettleConstruction();
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

    [Test]
    public void Clear_ShouldCreateClearRenderNode()
    {
        using var node = new ContainerRenderNode();
        using var context = new GraphicsContext2D(node, new Size(1920, 1080));

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

    private sealed class ReleaseThenThrowRenderNode : RenderNode
    {
        public bool Released { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            ObjectDisposedException.ThrowIf(Released, this);
            context.PassThrough();
        }

        protected override void OnDispose(bool disposing)
        {
            Released = true;
            throw new InvalidOperationException("resource release failed");
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
        using var node = new ContainerRenderNode();
        using var context = new GraphicsContext2D(node, new Size(1920, 1080));

        context.Clear(Colors.White);

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.InstanceOf<ClearRenderNode>());
        Assert.That(((ClearRenderNode)node.Children[0]).Color, Is.EqualTo(Colors.White));
    }

    [Test]
    public void Clear_KeepsItsNodeAndMarksAChangeOnlyWhenTheColorChanges()
    {
        using var root = new ContainerRenderNode();
        Record(root, static context => context.Clear());
        var clear = (ClearRenderNode)root.Children[0];
        long recorded = root.ChangeVersion;

        Record(root, static context => context.Clear());
        Assert.Multiple(() =>
        {
            Assert.That(root.Children[0], Is.SameAs(clear));
            Assert.That(clear.IsDisposed, Is.False);
            Assert.That(root.ChangeVersion, Is.EqualTo(recorded));
        });

        Record(root, static context => context.Clear(Colors.White));
        Assert.Multiple(() =>
        {
            Assert.That(root.Children[0], Is.SameAs(clear));
            Assert.That(clear.Color, Is.EqualTo(Colors.White));
            Assert.That(root.ChangeVersion, Is.GreaterThan(recorded));
        });

        static void Record(ContainerRenderNode root, Action<GraphicsContext2D> draw)
        {
            using var context = new GraphicsContext2D(root);
            draw(context);
        }
    }

    [Test]
    public void DrawImageSource_ShouldCreateImageSourceRenderNode()
    {
        using var node = new ContainerRenderNode();
        using var context = new GraphicsContext2D(node, new Size(1920, 1080));

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
        using var node = new ContainerRenderNode();
        using var context = new GraphicsContext2D(node, new Size(1920, 1080));

        var videoPath = TestMediaHelper.CreateTestVideoFile(100, 100, new Rational(30), 300);
        var videoSource = new VideoSource();
        videoSource.ReadFrom(new Uri(videoPath));
        using var videoResource = videoSource.ToResource(CompositionContext.Default);

        context.DrawVideoSource(videoResource, 0, Brushes.Resource.White, null);

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.InstanceOf<VideoSourceRenderNode>());
    }

    [Test]
    public void DrawEllipse_ShouldCreateEllipseRenderNode()
    {
        using var node = new ContainerRenderNode();
        using var context = new GraphicsContext2D(node, new Size(1920, 1080));

        context.DrawEllipse(new Rect(0, 0, 100, 100), Brushes.Resource.White, null);

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.InstanceOf<EllipseRenderNode>());
    }

    [Test]
    public void DrawGeometry_ShouldCreateGeometryRenderNode()
    {
        using var node = new ContainerRenderNode();
        using var context = new GraphicsContext2D(node, new Size(1920, 1080));
        var geometry = new EllipseGeometry();
        geometry.Width.Value = 100;
        geometry.Height.Value = 100;
        var resource = geometry.ToResource(CompositionContext.Default);

        context.DrawGeometry(resource, Brushes.Resource.White, null);

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.InstanceOf<GeometryRenderNode>());
    }

    [Test]
    public void DrawRectangle_ShouldCreateRectangleRenderNode()
    {
        using var node = new ContainerRenderNode();
        using var context = new GraphicsContext2D(node, new Size(1920, 1080));

        context.DrawRectangle(new Rect(0, 0, 100, 100), Brushes.Resource.White, null);

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.InstanceOf<RectangleRenderNode>());
    }

    [Test]
    public void DrawDrawable_ShouldCreateDrawableRenderNode()
    {
        var drawable = new RectShape();
        using var resource = drawable.ToResource(CompositionContext.Default);
        using var node = new ContainerRenderNode();
        using var context = new GraphicsContext2D(node, new Size(1920, 1080));

        context.DrawDrawable(resource);

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.InstanceOf<DrawableRenderNode>());
    }

    [Test]
    public void DrawNode_ShouldAddPassedNodeDirectly()
    {
        using var node = new ContainerRenderNode();
        using var context = new GraphicsContext2D(node, new Size(1920, 1080));
        var child = new ContainerRenderNode();

        context.DrawNode(child);

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.EqualTo(child));
    }

    [Test]
    public void DrawBackdrop_ShouldCreateDrawBackdropRenderNode()
    {
        using var node = new ContainerRenderNode();
        using var context = new GraphicsContext2D(node, new Size(1920, 1080));
        var backdrop = new Mock<IBackdrop>();

        context.DrawBackdrop(backdrop.Object);

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.InstanceOf<DrawBackdropRenderNode>());
    }

    [Test]
    public void Snapshot_ShouldCreateSnapshotBackdropRenderNode()
    {
        using var node = new ContainerRenderNode();
        using var context = new GraphicsContext2D(node, new Size(1920, 1080));

        _ = context.Snapshot();

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.InstanceOf<SnapshotBackdropRenderNode>());
    }

    [Test]
    public void Push_ShouldCreatePushRenderNode()
    {
        using var node = new ContainerRenderNode();
        using var context = new GraphicsContext2D(node, new Size(1920, 1080));

        context.Push().Dispose();

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.InstanceOf<PushRenderNode>());
    }

    [Test]
    public void PushLayer_ShouldCreateLayerRenderNode()
    {
        using var node = new ContainerRenderNode();
        using var context = new GraphicsContext2D(node, new Size(1920, 1080));

        context.PushLayer().Dispose();

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.InstanceOf<LayerRenderNode>());
    }

    [Test]
    public void PushBlendMode_ShouldCreateBlendModeRenderNode()
    {
        using var node = new ContainerRenderNode();
        using var context = new GraphicsContext2D(node, new Size(1920, 1080));

        context.PushBlendMode(BlendMode.Clear).Dispose();

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.InstanceOf<BlendModeRenderNode>());
    }

    [Test]
    public void PushClip_ShouldCreateRectClipRenderNode()
    {
        using var node = new ContainerRenderNode();
        using var context = new GraphicsContext2D(node, new Size(1920, 1080));

        context.PushClip(new Rect(0, 0, 100, 100)).Dispose();

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.InstanceOf<RectClipRenderNode>());
    }

    [Test]
    public void PushClipGeometry_ShouldCreateGeometryClipRenderNode()
    {
        using var node = new ContainerRenderNode();
        using var context = new GraphicsContext2D(node, new Size(1920, 1080));
        var geometry = new EllipseGeometry();
        geometry.Width.Value = 100;
        geometry.Height.Value = 100;
        var resource = geometry.ToResource(CompositionContext.Default);

        context.PushClip(resource).Dispose();

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.InstanceOf<GeometryClipRenderNode>());
    }

    [Test]
    public void PushOpacity_ShouldCreateOpacityRenderNode()
    {
        using var node = new ContainerRenderNode();
        using var context = new GraphicsContext2D(node, new Size(1920, 1080));

        context.PushOpacity(0.5f).Dispose();

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.InstanceOf<OpacityRenderNode>());
    }

    [Test]
    public void PushFilterEffect_ShouldCreateFilterEffectRenderNode()
    {
        using var node = new ContainerRenderNode();
        using var context = new GraphicsContext2D(node, new Size(1920, 1080));
        var effect = new Blur();
        var resource = effect.ToResource(CompositionContext.Default);

        context.PushFilterEffect(resource).Dispose();

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.InstanceOf<FilterEffectRenderNode>());
    }

    [Test]
    public void PushOpacityMask_ShouldCreateOpacityMaskRenderNode()
    {
        using var node = new ContainerRenderNode();
        using var context = new GraphicsContext2D(node, new Size(1920, 1080));
        var mask = Brushes.Resource.White;

        context.PushOpacityMask(mask, new Rect(0, 0, 100, 100)).Dispose();

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.InstanceOf<OpacityMaskRenderNode>());
    }

    [Test]
    public void PushTransform_ShouldCreateTransformRenderNode()
    {
        using var node = new ContainerRenderNode();
        using var context = new GraphicsContext2D(node, new Size(1920, 1080));
        var transform = new RotationTransform();
        var resource = transform.ToResource(CompositionContext.Default);

        context.PushTransform(resource).Dispose();

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.InstanceOf<TransformRenderNode>());
    }

    [Test]
    public void PushMatrixTransform_ShouldCreateTransformRenderNode()
    {
        using var node = new ContainerRenderNode();
        using var context = new GraphicsContext2D(node, new Size(1920, 1080));
        var matrix = Matrix.CreateRotation(45);

        context.PushTransform(matrix).Dispose();

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.InstanceOf<TransformRenderNode>());
    }
}

internal sealed partial class PartialFailureDrawable : Drawable
{
    private readonly RenderNode _child;
    private readonly Exception _failure;

    public PartialFailureDrawable(RenderNode child, Exception failure)
    {
        _child = child;
        _failure = failure;
    }

    private void RenderContent(GraphicsContext2D context)
    {
        context.DrawNode(_child);
        throw _failure;
    }

    public new partial class Resource
    {
        protected override void RenderCore(GraphicsContext2D context) => RequireOriginal().RenderContent(context);

        protected override Size MeasureCore(Size availableSize)
            => Size.Empty;

        protected override void OnDraw(GraphicsContext2D context)
        {
        }
    }
}
