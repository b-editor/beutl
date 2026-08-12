using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.UnitTests.Engine.Graphics.Backend;
using Beutl.UnitTests.Engine.Graphics.Rendering;

namespace Beutl.UnitTests.ProjectSystem;

// SceneDrawable must preserve output scale and nested render-tree lifetime. Execution cases are Vulkan-gated.
[NonParallelizable]
[TestFixture]
public class SceneDrawableScaleTests
{
    private static string GetTempPath()
        => Path.Combine(Path.GetTempPath(), $"beutl_scenedrawable_{Guid.NewGuid():N}");

    private static Scene CreateInnerScene(string basePath, int width, int height)
    {
        Directory.CreateDirectory(basePath);
        var scene = new Scene(width, height, string.Empty)
        {
            Uri = new Uri(Path.Combine(basePath, "inner.scene"))
        };

        // One renderable child: a RectShape carries a default White Fill, so it actually rasterizes content.
        var rect = new RectShape
        {
            Width = { CurrentValue = width },
            Height = { CurrentValue = height },
        };
        var element = new Element
        {
            Start = TimeSpan.Zero,
            Length = TimeSpan.FromSeconds(1),
            IsEnabled = true,
            Uri = new Uri(Path.Combine(basePath, $"{Guid.NewGuid():N}.layer"))
        };
        element.AddObject(rect);
        scene.Children.Add(element);
        return scene;
    }

    private static Scene CreateInnerSceneWithBackdrop(string basePath, int width, int height)
    {
        Scene scene = CreateInnerScene(basePath, width, height);
        var backdrop = new SourceBackdrop();
        var element = new Element
        {
            Start = TimeSpan.Zero,
            Length = TimeSpan.FromSeconds(1),
            IsEnabled = true,
            ZIndex = 1,
            Uri = new Uri(Path.Combine(basePath, $"{Guid.NewGuid():N}.layer"))
        };
        element.AddObject(backdrop);
        scene.Children.Add(element);
        return scene;
    }

    private static Scene CreateInnerSceneWithTwoDrawables(string basePath, int width, int height)
    {
        Scene scene = CreateInnerScene(basePath, width, height);
        var rect = new RectShape
        {
            Width = { CurrentValue = width / 2f },
            Height = { CurrentValue = height / 2f },
        };
        var element = new Element
        {
            Start = TimeSpan.Zero,
            Length = TimeSpan.FromSeconds(1),
            IsEnabled = true,
            ZIndex = 1,
            Uri = new Uri(Path.Combine(basePath, $"{Guid.NewGuid():N}.layer"))
        };
        element.AddObject(rect);
        scene.Children.Add(element);
        return scene;
    }

    private static Scene CreateInnerSceneWithRetryingDrawable(
        string basePath,
        int width,
        int height,
        out RetryingSceneDrawable drawable)
    {
        Scene scene = CreateInnerScene(basePath, width, height);
        drawable = new RetryingSceneDrawable();
        var element = new Element
        {
            Start = TimeSpan.Zero,
            Length = TimeSpan.FromSeconds(1),
            IsEnabled = true,
            ZIndex = 1,
            Uri = new Uri(Path.Combine(basePath, $"{Guid.NewGuid():N}.layer"))
        };
        element.AddObject(drawable);
        scene.Children.Add(element);
        return scene;
    }

    // Materializes the recorded nested-scene subtree and reports its concrete output metadata.
    private static RenderNodeMeasurement MeasureConcreteOutput(
        SceneDrawable drawable,
        Scene inner,
        float outputScale)
    {
        using Drawable.Resource resource = drawable.ToResource(new CompositionContext(TimeSpan.Zero));
        var root = new DrawableRenderNode(resource);
        using (var ctx = new GraphicsContext2D(root, inner.FrameSize.ToSize(1), outputScale))
        {
            drawable.Render(ctx, resource);
        }

        using var pipeline = ScaleRecordingTestHelper.SubtreePipeline(
            root,
            ScaleRecordingTestHelper.Layer(new Rect(0, 0, inner.FrameSize.Width, inner.FrameSize.Height)),
            ScaleRecordingTestHelper.Materialize());
        return ScaleRecordingTestHelper.Measure(pipeline, outputScale);
    }

    [TestCase(1.0f)] // even at s_out == 1 the nested buffer is concrete At(1), not Unbounded vector.
    [TestCase(1.5f)]
    [TestCase(2.0f)]
    public void NestedScene_InheritsConcreteEffectiveScale_AtOutputScale(float outputScale)
    {
        string basePath = GetTempPath();
        try
        {
            VulkanTestEnvironment.EnsureAvailable();
            VulkanTestEnvironment.InvokeOnRenderThread(() =>
            {
                Scene inner = CreateInnerScene(basePath, 120, 90);
                var drawable = new SceneDrawable();
                drawable.ReferencedScene.CurrentValue = inner;

                RenderNodeMeasurement measurement = MeasureConcreteOutput(drawable, inner, outputScale);

                // A nested-scene buffer is concrete bitmap supply, never Unbounded.
                Assert.That(measurement.HasFragments, Is.True,
                    "SceneDrawable emitted no recorded fragment for the nested scene.");
                Assert.That(measurement.EffectiveScale.IsUnbounded, Is.False,
                    "the nested-scene surface was reported as re-rasterizable Unbounded instead of a concrete bitmap");
                // Inherits the outer output scale as its supply density.
                Assert.That(measurement.EffectiveScale.Value, Is.EqualTo(outputScale).Within(1e-4),
                    $"the nested scene did not inherit the outer output scale {outputScale} as its supply density");
            });
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void NestedScene_WithSourceBackdrop_RasterizesAfterRecording()
    {
        string basePath = GetTempPath();
        try
        {
            VulkanTestEnvironment.EnsureAvailable();
            VulkanTestEnvironment.InvokeOnRenderThread(() =>
            {
                Scene inner = CreateInnerSceneWithBackdrop(basePath, 120, 90);
                var drawable = new SceneDrawable();
                drawable.ReferencedScene.CurrentValue = inner;

                using Drawable.Resource resource = drawable.ToResource(new CompositionContext(TimeSpan.Zero));
                using var root = new DrawableRenderNode(resource);
                using (var context = new GraphicsContext2D(root, inner.FrameSize.ToSize(1)))
                {
                    drawable.Render(context, resource);
                }

                using var renderer = new RenderNodeRenderer(
                    root,
                    new RenderNodeRendererOptions
                    {
                        DefaultRequest = new RenderNodeRenderRequest
                        {
                            TargetDomain = new Rect(0, 0, inner.FrameSize.Width, inner.FrameSize.Height),
                            CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                        },
                    });
                using RenderNodeRasterization rasterization = renderer.Rasterize();

                Assert.Multiple(() =>
                {
                    Assert.That(rasterization.IsEmpty, Is.False);
                    Assert.That(rasterization.Bitmap, Is.Not.Null);
                    Assert.That(rasterization.Bitmap!.GetPixelSpan().ToArray(), Has.Some.Not.Zero);
                });
            });
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void NestedScene_ReusesChildrenAndWarmsStableChildCachesDespiteChangingAncestors()
    {
        string basePath = GetTempPath();
        try
        {
            Scene inner = CreateInnerSceneWithTwoDrawables(basePath, 120, 90);
            var drawable = new SceneDrawable();
            drawable.ReferencedScene.CurrentValue = inner;

            using var resource = (SceneDrawable.Resource)drawable.ToResource(
                new CompositionContext(TimeSpan.Zero));
            using var root = new DrawableRenderNode(resource);
            RenderDrawableTree(drawable, resource, root, inner.FrameSize.ToSize(1));

            ContainerRenderNode sceneNode = FindNestedSceneNode(root);
            DrawableRenderNode[] nested = [.. sceneNode.Children.Cast<DrawableRenderNode>()];
            Assert.That(nested, Has.Length.EqualTo(2));
            DrawableRenderNode stable = nested[0];
            DrawableRenderNode changing = nested[1];
            Drawable.Resource changingResource = resource.Frame!.Value.Objects
                .OfType<Drawable.Resource>()
                .ElementAt(1);

            CompleteSuccessfulFrame(root);
            for (int frame = 0; frame < RenderNodeCache.StableRequestCount; frame++)
            {
                changingResource.Version++;
                resource.Version++;
                Assert.That(root.Update(resource), Is.True);
                RenderDrawableTree(drawable, resource, root, inner.FrameSize.ToSize(1));

                ContainerRenderNode currentSceneNode = FindNestedSceneNode(root);
                Assert.Multiple(() =>
                {
                    Assert.That(currentSceneNode, Is.SameAs(sceneNode));
                    Assert.That(currentSceneNode.Children[0], Is.SameAs(stable));
                    Assert.That(currentSceneNode.Children[1], Is.SameAs(changing));
                });
                CompleteSuccessfulFrame(root);
            }

            Assert.Multiple(() =>
            {
                Assert.That(root.Cache.CanCapture, Is.False, "the changing parent restarts warm-up each frame");
                Assert.That(sceneNode.Cache.CanCapture, Is.False, "the changing child invalidates its ancestors");
                Assert.That(changing.Cache.CanCapture, Is.False, "the changing child restarts warm-up each frame");
                Assert.That(stable.Cache.CanCapture, Is.True, "the unchanged child retains its warm-up");
                Assert.That(stable.Cache.SuccessfulStableRequestCount,
                    Is.GreaterThanOrEqualTo(RenderNodeCache.StableRequestCount));
            });

            root.Dispose();
            Assert.Multiple(() =>
            {
                Assert.That(sceneNode.IsDisposed, Is.True);
                Assert.That(stable.IsDisposed, Is.True);
                Assert.That(changing.IsDisposed, Is.True);
            });
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void NestedScene_RetriesFailedChildUpdateTransactionally()
    {
        string basePath = GetTempPath();
        try
        {
            Scene inner = CreateInnerSceneWithRetryingDrawable(
                basePath,
                120,
                90,
                out RetryingSceneDrawable retrying);
            var drawable = new SceneDrawable();
            drawable.ReferencedScene.CurrentValue = inner;

            using var resource = (SceneDrawable.Resource)drawable.ToResource(
                new CompositionContext(TimeSpan.Zero));
            using var root = new DrawableRenderNode(resource);
            RenderDrawableTree(drawable, resource, root, inner.FrameSize.ToSize(1));

            ContainerRenderNode sceneNode = FindNestedSceneNode(root);
            DrawableRenderNode child = sceneNode.Children
                .Cast<DrawableRenderNode>()
                .Single(node => ReferenceEquals(node.Drawable!.Value.Resource.GetOriginal(), retrying));
            Drawable.Resource childResource = child.Drawable!.Value.Resource;
            int originalVersion = child.Drawable.Value.Version;
            RenderNode[] originalOutputs = [.. child.Children];
            Assert.That(originalOutputs, Has.Length.EqualTo(2));

            retrying.OutputCount = 1;
            retrying.ThrowAfterFirstOutput = true;
            childResource.Version++;
            resource.Version++;
            Assert.That(root.Update(resource), Is.True);

            InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(
                () => RenderDrawableTree(
                    drawable,
                    resource,
                    root,
                    inner.FrameSize.ToSize(1)));
            TrackingSceneRenderNode failedCandidate = retrying.CreatedNodes[^1];

            Assert.Multiple(() =>
            {
                Assert.That(failure!.Message, Is.EqualTo("retrying nested scene"));
                Assert.That(retrying.RenderCalls, Is.EqualTo(2));
                Assert.That(EnumerateSubtree(root), Does.Contain(sceneNode));
                Assert.That(sceneNode.Children, Does.Contain(child));
                Assert.That(child.Drawable!.Value.Version, Is.EqualTo(originalVersion));
                Assert.That(child.Children, Is.EqualTo(originalOutputs));
                Assert.That(originalOutputs, Has.All.Matches<RenderNode>(node => !node.IsDisposed));
                Assert.That(failedCandidate.IsDisposed, Is.True);
            });

            retrying.ThrowAfterFirstOutput = false;
            Assert.That(root.Update(resource), Is.False);
            RenderDrawableTree(drawable, resource, root, inner.FrameSize.ToSize(1));

            Assert.Multiple(() =>
            {
                Assert.That(retrying.RenderCalls, Is.EqualTo(3));
                Assert.That(sceneNode.Children, Does.Contain(child));
                Assert.That(child.Drawable!.Value.Version, Is.EqualTo(childResource.Version));
                Assert.That(child.Children, Has.Count.EqualTo(1));
                Assert.That(child.Children[0], Is.SameAs(retrying.CreatedNodes[^1]));
                Assert.That(child.Children[0].IsDisposed, Is.False);
                Assert.That(originalOutputs, Has.All.Matches<RenderNode>(node => node.IsDisposed));
            });
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void NestedScene_RemovingTrailingChildrenDisposesAllAfterOneFails()
    {
        string basePath = GetTempPath();
        try
        {
            Scene inner = CreateInnerSceneWithRetryingDrawable(
                basePath,
                120,
                90,
                out RetryingSceneDrawable first);
            first.OutputCount = 1;
            var second = new RetryingSceneDrawable { OutputCount = 1 };
            var secondElement = new Element
            {
                Start = TimeSpan.Zero,
                Length = TimeSpan.FromSeconds(1),
                IsEnabled = true,
                ZIndex = 2,
                Uri = new Uri(Path.Combine(basePath, $"{Guid.NewGuid():N}.layer"))
            };
            secondElement.AddObject(second);
            inner.Children.Add(secondElement);

            var drawable = new SceneDrawable();
            drawable.ReferencedScene.CurrentValue = inner;
            using var resource = (SceneDrawable.Resource)drawable.ToResource(
                new CompositionContext(TimeSpan.Zero));
            using var root = new DrawableRenderNode(resource);
            RenderDrawableTree(drawable, resource, root, inner.FrameSize.ToSize(1));

            ContainerRenderNode sceneNode = EnumerateSubtree(root)
                .OfType<ContainerRenderNode>()
                .Single(node => node.Children.Count == 3
                                && node.Children.All(static child => child is DrawableRenderNode));
            DrawableRenderNode firstWrapper = sceneNode.Children
                .Cast<DrawableRenderNode>()
                .Single(node => ReferenceEquals(node.Drawable!.Value.Resource.GetOriginal(), first));
            DrawableRenderNode secondWrapper = sceneNode.Children
                .Cast<DrawableRenderNode>()
                .Single(node => ReferenceEquals(node.Drawable!.Value.Resource.GetOriginal(), second));
            TrackingSceneRenderNode firstOutput = first.CreatedNodes.Single();
            TrackingSceneRenderNode secondOutput = second.CreatedNodes.Single();
            firstOutput.ThrowOnDispose = true;

            CompositionFrame frame = resource.Frame!.Value;
            resource.Frame = new CompositionFrame([frame.Objects[0]], frame.Time, frame.Size);
            resource.Version++;
            Assert.That(root.Update(resource), Is.True);

            try
            {
                InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(
                    () => RenderDrawableTree(
                        drawable,
                        resource,
                        root,
                        inner.FrameSize.ToSize(1)));

                Assert.Multiple(() =>
                {
                    Assert.That(failure!.Message, Is.EqualTo("tracking scene-node disposal"));
                    Assert.That(sceneNode.Children, Has.Count.EqualTo(1));
                    Assert.That(firstOutput.DisposeCalls, Is.EqualTo(1));
                    Assert.That(firstWrapper.IsDisposed, Is.False);
                    Assert.That(secondOutput.DisposeCalls, Is.EqualTo(1));
                    Assert.That(secondOutput.IsDisposed, Is.True);
                    Assert.That(secondWrapper.IsDisposed, Is.True);
                });
            }
            finally
            {
                firstOutput.ThrowOnDispose = false;
                firstWrapper.Dispose();
                secondWrapper.Dispose();
            }
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void NestedScene_RebuildsAtANewOutputScaleWithoutReplacingChildren()
    {
        string basePath = GetTempPath();
        try
        {
            Scene inner = CreateInnerSceneWithRetryingDrawable(
                basePath,
                120,
                90,
                out RetryingSceneDrawable retrying);
            var drawable = new SceneDrawable();
            drawable.ReferencedScene.CurrentValue = inner;

            using var resource = (SceneDrawable.Resource)drawable.ToResource(
                new CompositionContext(TimeSpan.Zero));
            using var root = new DrawableRenderNode(resource);
            RenderDrawableTree(
                drawable,
                resource,
                root,
                inner.FrameSize.ToSize(1),
                outputScale: 1);

            ContainerRenderNode sceneNode = FindNestedSceneNode(root);
            DrawableRenderNode child = sceneNode.Children
                .Cast<DrawableRenderNode>()
                .Single(node => ReferenceEquals(node.Drawable!.Value.Resource.GetOriginal(), retrying));
            RenderNode[] firstOutputs = [.. child.Children];

            RenderDrawableTree(
                drawable,
                resource,
                root,
                inner.FrameSize.ToSize(1),
                outputScale: 2);

            Assert.Multiple(() =>
            {
                Assert.That(FindNestedSceneNode(root), Is.SameAs(sceneNode));
                Assert.That(sceneNode.Children, Does.Contain(child));
                Assert.That(retrying.RenderCalls, Is.EqualTo(2));
                Assert.That(retrying.ObservedOutputScales, Is.EqualTo(new[] { 1f, 2f }));
                Assert.That(child.Children, Has.Count.EqualTo(2));
                Assert.That(child.Children, Is.Not.EqualTo(firstOutputs));
                Assert.That(child.Children, Has.All.Matches<RenderNode>(node => !node.IsDisposed));
                Assert.That(firstOutputs, Has.All.Matches<RenderNode>(node => node.IsDisposed));
            });
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    private static void RenderDrawableTree(
        SceneDrawable drawable,
        Drawable.Resource resource,
        DrawableRenderNode root,
        Size canvasSize,
        float outputScale = 1)
    {
        using var context = new GraphicsContext2D(root, canvasSize, outputScale);
        drawable.Render(context, resource);
    }

    private static ContainerRenderNode FindNestedSceneNode(RenderNode root)
    {
        return EnumerateSubtree(root)
            .OfType<ContainerRenderNode>()
            .Single(node => node.Children.Count == 2
                            && node.Children.All(static child => child is DrawableRenderNode));
    }

    private static IEnumerable<RenderNode> EnumerateSubtree(RenderNode node)
    {
        yield return node;
        if (node is not ContainerRenderNode container)
            yield break;

        foreach (RenderNode child in container.Children)
        {
            foreach (RenderNode descendant in EnumerateSubtree(child))
                yield return descendant;
        }
    }

    private static void CompleteSuccessfulFrame(RenderNode node)
    {
        RenderNodeCacheHelper.BeginLifecycle(node).CompleteSuccessfully(advanceWarmup: true);
    }
}

internal sealed partial class RetryingSceneDrawable : Drawable
{
    public bool ThrowAfterFirstOutput { get; set; }

    public int OutputCount { get; set; } = 2;

    public int RenderCalls { get; private set; }

    public List<float> ObservedOutputScales { get; } = [];

    public List<TrackingSceneRenderNode> CreatedNodes { get; } = [];

    public override void Render(GraphicsContext2D context, Drawable.Resource resource)
    {
        RenderCalls++;
        ObservedOutputScales.Add(context.OutputScale);
        for (int index = 0; index < OutputCount; index++)
        {
            var node = new TrackingSceneRenderNode();
            CreatedNodes.Add(node);
            context.DrawNode(node);
            if (index == 0 && ThrowAfterFirstOutput)
                throw new InvalidOperationException("retrying nested scene");
        }
    }

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource)
        => new(16, 16);

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
    }
}

internal sealed class TrackingSceneRenderNode : RenderNode
{
    public bool ThrowOnDispose { get; set; }

    public int DisposeCalls { get; private set; }

    public override void Process(RenderNodeContext context)
    {
        context.PassThrough();
    }

    protected override void OnDispose(bool disposing)
    {
        DisposeCalls++;
        if (ThrowOnDispose)
            throw new InvalidOperationException("tracking scene-node disposal");
    }
}
