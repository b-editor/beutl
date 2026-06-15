using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.ProjectSystem;

// feature 003 (FR-022 / FR-019b): a SceneDrawable at output scale w rasterizes the nested scene into a
// fixed-resolution ceil(FrameSize × w) surface and emits it as a RenderNodeOperation. That op must carry the
// concrete inherited density At(w), never the re-rasterizable Unbounded sentinel, so a parent reconciles it at
// its real pixel density instead of treating it as vector and re-rasterizing (soft supersample).
//
// SceneBitmapRenderNode.Process allocates a real nested Renderer (RenderTarget), so the test is Vulkan-gated and
// runs on the render thread. It only pulls the op (where EffectiveScale is tagged), never invoking the render
// lambda, so no GPU blit runs; the pull alone forces the At(w) tag.
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

    // Pulls the single concrete (non-Unbounded) op of the SceneDrawable at the given output scale. The default
    // centering transform is a pure translation (scale 1), so the nested op's density flows up unchanged;
    // selecting by concreteness stays decoupled from the pass-through op count. The caller disposes the returned op.
    private static RenderNodeOperation PullConcreteOp(SceneDrawable drawable, Scene inner, float outputScale)
    {
        Drawable.Resource resource = drawable.ToResource(new CompositionContext(TimeSpan.Zero));
        var root = new DrawableRenderNode(resource);
        using (var ctx = new GraphicsContext2D(root, inner.FrameSize.ToSize(1), outputScale))
        {
            drawable.Render(ctx, resource);
        }

        var processor = new RenderNodeProcessor(root, useRenderCache: false, outputScale: outputScale);
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

                // FR-019b: a fixed-resolution nested-scene buffer is concrete bitmap supply, never Unbounded.
                Assert.That(op.EffectiveScale.IsUnbounded, Is.False,
                    "the nested-scene surface was reported as re-rasterizable Unbounded instead of a concrete bitmap");
                // FR-022: it inherits the outer output scale as its supply density (ceil(FrameSize × w) device px).
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
}
