using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
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
    private string _basePath = null!;

    [SetUp]
    public void SetUp()
    {
        _basePath = Path.Combine(Path.GetTempPath(), $"beutl_scenebounds_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_basePath);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_basePath)) Directory.Delete(_basePath, recursive: true);
    }

    [Test]
    public void NestedScene_WithoutVisualContent_KeepsFrameQueryBounds()
    {
        RenderNodeMeasurement measurement = MeasureNestedScene(CreateInnerScene(_basePath, 120, 90));

        Assert.Multiple(() =>
        {
            Assert.That(measurement.HasFragments, Is.True);
            Assert.That(measurement.QueryBounds, Is.EqualTo(new Rect(0, 0, 120, 90)));
        });
    }

    [Test]
    public void NestedScene_WithOffCenterContent_KeepsFrameQueryBounds()
    {
        Scene inner = CreateInnerScene(_basePath, 120, 90);
        AddTopLeftRect(inner, _basePath, 20);

        RenderNodeMeasurement measurement = MeasureNestedScene(inner);

        Assert.Multiple(() =>
        {
            Assert.That(measurement.QueryBounds, Is.EqualTo(new Rect(0, 0, 120, 90)));
            Assert.That(measurement.OutputBounds, Is.EqualTo(new Rect(0, 0, 20, 20)),
                "widening the frame's query footprint must not widen what it actually draws");
        });
    }

    private static Scene CreateInnerScene(string basePath, int width, int height)
    {
        return new Scene(width, height, string.Empty)
        {
            Uri = new Uri(Path.Combine(basePath, "inner.scene"))
        };
    }

    private static void AddTopLeftRect(Scene scene, string basePath, int size)
    {
        var rect = new RectShape
        {
            Width = { Value = size },
            Height = { Value = size },
            AlignmentX = { Value = AlignmentX.Left },
            AlignmentY = { Value = AlignmentY.Top },
        };
        SceneDrawableTestHelpers.AddElement(scene, basePath, rect);
    }

    private static RenderNodeMeasurement MeasureNestedScene(Scene inner)
    {
        var drawable = new SceneDrawable();
        drawable.ReferencedScene.Value = inner;

        using Drawable.Resource resource = drawable.ToResource(new CompositionContext(TimeSpan.Zero));
        using var root = new DrawableRenderNode(resource);
        using (var context = new GraphicsContext2D(root, inner.FrameSize.ToSize(1)))
        {
            drawable.Render(context, resource);
        }

        using RenderNodeRenderer renderer = SceneDrawableTestHelpers.CreateRenderer(root, inner);
        return renderer.Measure();
    }
}
