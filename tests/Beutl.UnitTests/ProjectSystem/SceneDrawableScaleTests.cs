using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.UnitTests.Engine.Graphics.Backend;
using Beutl.UnitTests.Engine.Graphics.Rendering;

namespace Beutl.UnitTests.ProjectSystem;

// SceneDrawable must emit ops tagged At(w), never Unbounded. Vulkan-gated.
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
}
