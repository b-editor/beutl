using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.UnitTests.ProjectSystem;

// A nested scene is a fixed-size viewport, so the bounds a caller queries are the referenced frame rather
// than whatever the frame happens to contain. The preview's selection outline and transform handles are
// placed from that value, and an empty or off-centre nested scene must not move or lose them.
[TestFixture]
public class SceneDrawableBoundsTests
{
    [Test]
    public void NestedScene_WithoutVisualContent_KeepsFrameQueryBounds()
    {
        string basePath = GetTempPath();
        try
        {
            RenderNodeMeasurement measurement = MeasureNestedScene(CreateInnerScene(basePath, 120, 90));

            Assert.Multiple(() =>
            {
                Assert.That(measurement.HasFragments, Is.True);
                Assert.That(measurement.QueryBounds, Is.EqualTo(new Rect(0, 0, 120, 90)));
            });
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void NestedScene_WithOffCenterContent_KeepsFrameQueryBounds()
    {
        string basePath = GetTempPath();
        try
        {
            Scene inner = CreateInnerScene(basePath, 120, 90);
            AddTopLeftRect(inner, basePath, 20);

            RenderNodeMeasurement measurement = MeasureNestedScene(inner);

            Assert.Multiple(() =>
            {
                Assert.That(measurement.QueryBounds, Is.EqualTo(new Rect(0, 0, 120, 90)));
                Assert.That(measurement.OutputBounds, Is.EqualTo(new Rect(0, 0, 20, 20)),
                    "widening the frame's query footprint must not widen what it actually draws");
            });
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    private static string GetTempPath()
        => Path.Combine(Path.GetTempPath(), $"beutl_scenebounds_{Guid.NewGuid():N}");

    private static Scene CreateInnerScene(string basePath, int width, int height)
    {
        Directory.CreateDirectory(basePath);
        return new Scene(width, height, string.Empty)
        {
            Uri = new Uri(Path.Combine(basePath, "inner.scene"))
        };
    }

    private static void AddTopLeftRect(Scene scene, string basePath, int size)
    {
        var rect = new RectShape
        {
            Width = { CurrentValue = size },
            Height = { CurrentValue = size },
            AlignmentX = { CurrentValue = AlignmentX.Left },
            AlignmentY = { CurrentValue = AlignmentY.Top },
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
    }

    private static RenderNodeMeasurement MeasureNestedScene(Scene inner)
    {
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
                    CacheOptions = RenderCacheOptions.Disabled,
                },
            });
        return renderer.Measure();
    }
}
