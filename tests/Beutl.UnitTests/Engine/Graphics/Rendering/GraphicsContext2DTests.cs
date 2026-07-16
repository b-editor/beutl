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

        // A FilterEffectGroup is one render node (its whole child chain describes into a single graph, fed to
        // fusion), so mutating its children re-describes the same node rather than untracking one. Removing the
        // effect entirely is what untracks the FilterEffectRenderNode.
        drawable.FilterEffect.CurrentValue = null;
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
        Assert.That(untrackedNode, Is.TypeOf<PlanFilterEffectRenderNode>());
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

    [Test]
    public void Dispose_WhenUpdatedGraphShrinks_DisposesEveryRetiredNode()
    {
        var container = new ContainerRenderNode();
        var first = new TrackingRenderNode();
        var second = new TrackingRenderNode();
        container.AddChild(first);
        container.AddChild(second);
        var context = new GraphicsContext2D(container);

        context.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(container.Children, Is.Empty);
            Assert.That(first.DisposeCalls, Is.EqualTo(1));
            Assert.That(second.DisposeCalls, Is.EqualTo(1));
        });
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
    public void Pop_PropagatesAnyChangedSibling_WhenLaterSiblingIsUnchanged()
    {
        var root = new ContainerRenderNode();
        var firstRect = new Rect(0, 0, 100, 100);
        var secondRect = new Rect(200, 0, 100, 100);

        using (var context = new GraphicsContext2D(root, new Size(1920, 1080)))
        using (context.Push())
        {
            context.DrawRectangle(firstRect, Brushes.Resource.White, null);
            context.DrawRectangle(secondRect, Brushes.Resource.White, null);
        }

        root.HasChanges = false;
        using (var context = new GraphicsContext2D(root, new Size(1920, 1080)))
        using (context.Push())
        {
            context.DrawRectangle(firstRect.WithWidth(120), Brushes.Resource.White, null);
            context.DrawRectangle(secondRect, Brushes.Resource.White, null);
        }

        Assert.That(root.HasChanges, Is.True,
            "a later unchanged sibling must not erase an earlier sibling's change before the nested root is popped");
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
    public void DrawNodeFactory_WhenFactoryReturnsDisposedNode_RejectsBeforeOwnershipTransfer()
    {
        var container = new ContainerRenderNode();
        using var context = new GraphicsContext2D(container);
        var disposed = new TrackingRenderNode();
        disposed.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
            context.DrawNode(0, _ => disposed, static (_, _) => false));

        Assert.Multiple(() =>
        {
            Assert.That(container.Children, Is.Empty);
            Assert.That(disposed.DisposeCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public void DrawNodeFactory_WhenUpdaterDisposesInstalledNode_DetachesAndRejectsIt()
    {
        var container = new ContainerRenderNode();
        using var context = new GraphicsContext2D(container);
        var node = new TrackingRenderNode();
        context.DrawNode(0, _ => node, static (_, _) => false);
        context.Reset();

        Assert.Throws<ObjectDisposedException>(() =>
            context.DrawNode<TrackingRenderNode, int>(
                1,
                _ => throw new AssertionException("factory must not run"),
                (current, _) =>
            {
                current.Dispose();
                return false;
            }));

        Assert.Multiple(() =>
        {
            Assert.That(container.Children, Is.Empty);
            Assert.That(node.DisposeCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public void DrawNodeFactory_WhenUpdaterDisposeThrows_DetachesNodeAndPreservesUpdaterFailure()
    {
        var updaterFailure = new InvalidOperationException("updater dispose failure");
        var cleanupFailure = new InvalidOperationException("untracked cleanup failure");
        var container = new ContainerRenderNode();
        using var context = new GraphicsContext2D(container);
        var node = new ThrowingTrackingRenderNode(updaterFailure);
        context.DrawNode(0, _ => node, static (_, _) => false);
        context.Reset();
        context.OnUntracked = _ => throw cleanupFailure;

        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(() =>
            context.DrawNode<ThrowingTrackingRenderNode, int>(
                1,
                _ => throw new AssertionException("factory must not run"),
                static (current, _) =>
                {
                    current.Dispose();
                    return false;
                }));

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(updaterFailure));
            Assert.That(container.Children, Is.Empty);
            Assert.That(node.IsDisposed, Is.True);
            Assert.That(node.DisposeCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public void PushNodeFactory_WhenUpdaterDisposesInstalledNode_DoesNotPushIt()
    {
        var root = new ContainerRenderNode();
        using var context = new GraphicsContext2D(root);
        var node = new ContainerRenderNode();
        using (context.PushNode(0, _ => node, static (_, _) => false))
        {
        }
        context.Reset();

        Assert.Throws<ObjectDisposedException>(() =>
            context.PushNode<ContainerRenderNode, int>(
                1,
                _ => throw new AssertionException("factory must not run"),
                (current, _) =>
            {
                current.Dispose();
                return false;
            }));

        Assert.Multiple(() =>
        {
            Assert.That(root.Children, Is.Empty);
            Assert.That(node.IsDisposed, Is.True);
        });
    }

    [Test]
    public void PushNodeFactory_WhenUpdaterThrowsAfterDisposal_DetachesNodeAndPreservesUpdaterFailure()
    {
        var updaterFailure = new InvalidOperationException("updater failure after disposal");
        var cleanupFailure = new InvalidOperationException("untracked cleanup failure");
        var root = new ContainerRenderNode();
        using var context = new GraphicsContext2D(root);
        var node = new ContainerRenderNode();
        using (context.PushNode(0, _ => node, static (_, _) => false))
        {
        }
        context.Reset();
        context.OnUntracked = _ => throw cleanupFailure;

        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(() =>
            context.PushNode<ContainerRenderNode, int>(
                1,
                _ => throw new AssertionException("factory must not run"),
                (current, _) =>
                {
                    current.Dispose();
                    throw updaterFailure;
                }));

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(updaterFailure));
            Assert.That(root.Children, Is.Empty);
            Assert.That(node.IsDisposed, Is.True);
        });
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

    [Test]
    public void Pop_WhenRetiredCleanupThrows_DetachesAllChildrenAndRestoresParentState()
    {
        var untrackedFailure = new InvalidOperationException("untracked failure");
        var disposeFailure = new InvalidOperationException("dispose failure");
        var root = new ContainerRenderNode();
        var context = new GraphicsContext2D(root, new Size(1920, 1080));
        var throwing = new ThrowingTrackingRenderNode(disposeFailure);
        var sibling = new TrackingRenderNode();

        using (context.Push())
        {
            context.DrawNode(throwing);
            context.DrawNode(sibling);
        }

        context.Reset();
        context.OnUntracked = node =>
        {
            if (ReferenceEquals(node, throwing))
                throw untrackedFailure;
        };
        PushedState state = context.Push();

        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(state.Dispose);

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(untrackedFailure));
            Assert.That(throwing.DisposeCalls, Is.EqualTo(1));
            Assert.That(sibling.DisposeCalls, Is.EqualTo(1));
            Assert.That(((ContainerRenderNode)root.Children[0]).Children, Is.Empty);
        });

        context.OnUntracked = null;
        var parentChild = new TrackingRenderNode();
        Assert.DoesNotThrow(() => context.DrawNode(parentChild),
            "the failed nested cleanup must leave subsequent drawing at the parent level");
        Assert.That(root.Children[1], Is.SameAs(parentChild));
    }

    [Test]
    public void Pop_WhenInnerCleanupThrows_StillRestoresEveryRequestedParentLevel()
    {
        var cleanupFailure = new InvalidOperationException("nested cleanup failure");
        var root = new ContainerRenderNode();
        var context = new GraphicsContext2D(root, new Size(1920, 1080));
        var throwing = new ThrowingTrackingRenderNode(cleanupFailure);

        using (context.Push())
        using (context.Push())
        {
            context.DrawNode(throwing);
        }

        context.Reset();
        PushedState outer = context.Push();
        context.Push();

        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(outer.Dispose);

        var parentChild = new TrackingRenderNode();
        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(cleanupFailure));
            Assert.That(throwing.DisposeCalls, Is.EqualTo(1));
            Assert.DoesNotThrow(() => context.DrawNode(parentChild));
            Assert.That(root.Children[1], Is.SameAs(parentChild),
                "cleanup failure at an inner level must not leave the context inside a nested container");
        });
    }

    [Test]
    public void DrawDrawable_WhenRenderAndNestedCleanupThrow_PreservesRenderFailure()
    {
        var renderFailure = new InvalidOperationException("nested render failure");
        var cleanupFailure = new InvalidOperationException("nested cleanup failure");
        var drawable = new DualFailureDrawable(renderFailure, cleanupFailure);
        using var resource = (Drawable.Resource)drawable.ToResource(new CompositionContext(
            TimeSpan.Zero,
            RenderIntent.Delivery,
            RenderPullPurpose.Frame));
        var root = new ContainerRenderNode();
        var context = new GraphicsContext2D(root, new Size(1920, 1080));
        context.DrawDrawable(resource);
        context.Reset();
        drawable.ThrowOnRender = true;

        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(
            () => context.DrawDrawable(resource));

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(renderFailure));
            Assert.That(drawable.RetiredNodeDisposeCalls, Is.EqualTo(1));
        });
    }
}

internal sealed class TrackingRenderNode : RenderNode
{
    public int DisposeCalls { get; private set; }

    public override RenderNodeOperation[] Process(RenderNodeContext context) => [];

    protected override void OnDispose(bool disposing)
    {
        if (disposing)
        {
            DisposeCalls++;
        }
    }
}

internal sealed class ThrowingTrackingRenderNode(Exception failure) : RenderNode
{
    public int DisposeCalls { get; private set; }

    public override RenderNodeOperation[] Process(RenderNodeContext context) => [];

    protected override void OnDispose(bool disposing)
    {
        if (disposing)
        {
            DisposeCalls++;
            throw failure;
        }
    }
}
