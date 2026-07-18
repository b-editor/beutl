using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.ProjectSystem;

// SceneDrawable must emit ops tagged At(w), never Unbounded. Vulkan-gated.
[NonParallelizable]
[TestFixture]
public class SceneDrawableScaleTests
{
    private static string GetTempPath()
        => Path.Combine(Path.GetTempPath(), $"beutl_scenedrawable_{Guid.NewGuid():N}");

    private static Scene CreateInnerScene(
        string basePath, int width, int height, Drawable? content = null)
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
        element.AddObject(content ?? rect);
        scene.Children.Add(element);
        return scene;
    }

    private static void RenderRecordedRoot(
        DrawableRenderNode root,
        PixelSize logicalSize,
        float outputScale,
        RenderPullPurpose pullPurpose)
    {
        var processor = new RenderNodeProcessor(
            root, useRenderCache: false, RenderIntent.Preview, outputScale,
            pullPurpose: pullPurpose);
        int width = (int)MathF.Ceiling(logicalSize.Width * outputScale);
        int height = (int)MathF.Ceiling(logicalSize.Height * outputScale);
        using RenderTarget target = RenderTarget.Create(width, height)!;
        using var canvas = new ImmediateCanvas(
            target, RenderIntent.Preview, outputScale, logicalSize: logicalSize.ToSize(1),
            pullPurpose: pullPurpose);
        processor.Render(canvas);
    }

    // Pulls the single concrete op at the given output scale.
    private static RenderNodeOperation PullConcreteOp(SceneDrawable drawable, Scene inner, float outputScale)
    {
        Drawable.Resource resource = drawable.ToResource(new CompositionContext(TimeSpan.Zero));
        var root = new DrawableRenderNode(resource);
        using (var ctx = new GraphicsContext2D(root, inner.FrameSize.ToSize(1), outputScale))
        {
            drawable.Render(ctx, resource);
        }

        var processor = new RenderNodeProcessor(root, useRenderCache: false, RenderIntent.Delivery, outputScale: outputScale);
        RenderNodeOperation[] ops = processor.PullToRoot();

        RenderNodeOperation? concrete = null;
        foreach (RenderNodeOperation op in ops)
        {
            if (concrete == null && !op.EffectiveScale.IsUnbounded)
            {
                concrete = op;
            }
            else
            {
                op.Dispose();
            }
        }

        Assert.That(concrete, Is.Not.Null,
            "SceneDrawable emitted no concrete (bitmap) op — the nested-scene surface was lost or tagged Unbounded.");
        return concrete!;
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

                RenderNodeOperation op = PullConcreteOp(drawable, inner, outputScale);

                // A nested-scene buffer is concrete bitmap supply, never Unbounded.
                Assert.That(op.EffectiveScale.IsUnbounded, Is.False,
                    "the nested-scene surface was reported as re-rasterizable Unbounded instead of a concrete bitmap");
                // Inherits the outer output scale as its supply density.
                Assert.That(op.EffectiveScale.Value, Is.EqualTo(outputScale).Within(1e-4),
                    $"the nested scene did not inherit the outer output scale {outputScale} as its supply density");

                op.Dispose();
            });
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void NestedScene_AuxiliaryScaleDoesNotReplaceFrameRenderer()
    {
        string basePath = GetTempPath();
        try
        {
            VulkanTestEnvironment.EnsureAvailable();
            VulkanTestEnvironment.InvokeOnRenderThread(() =>
            {
                var counter = new CountingSceneDrawable();
                Scene inner = CreateInnerScene(basePath, 120, 90, counter);
                var drawable = new SceneDrawable();
                drawable.ReferencedScene.CurrentValue = inner;
                using Drawable.Resource frameResource = drawable.ToResource(new CompositionContext(
                    TimeSpan.Zero, RenderIntent.Preview, RenderPullPurpose.Frame));
                using Drawable.Resource auxiliaryResource = drawable.ToResource(new CompositionContext(
                    TimeSpan.Zero, RenderIntent.Preview, RenderPullPurpose.Auxiliary));
                using var frameRoot = new DrawableRenderNode(frameResource);
                using var auxiliaryRoot = new DrawableRenderNode(auxiliaryResource);
                using (var context = new GraphicsContext2D(
                           frameRoot, inner.FrameSize.ToSize(1), outputScale: 1f))
                {
                    drawable.Render(context, frameResource);
                }

                using (var context = new GraphicsContext2D(
                           auxiliaryRoot, inner.FrameSize.ToSize(1), outputScale: 2f))
                {
                    drawable.Render(context, auxiliaryResource);
                }

                RenderRecordedRoot(frameRoot, inner.FrameSize, 1f, RenderPullPurpose.Frame);
                RenderRecordedRoot(auxiliaryRoot, inner.FrameSize, 2f, RenderPullPurpose.Auxiliary);
                RenderRecordedRoot(frameRoot, inner.FrameSize, 1f, RenderPullPurpose.Frame);

                Assert.That(counter.RenderCount, Is.EqualTo(2),
                    "the retained frame renderer should survive an auxiliary pull at a different scale");
            });
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }
}

internal sealed partial class CountingSceneDrawable : Drawable
{
    public int RenderCount { get; private set; }

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource) => new(20, 20);

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
        RenderCount++;
        context.DrawRectangle(new Rect(0, 0, 20, 20), Brushes.Resource.White, null);
    }
}
