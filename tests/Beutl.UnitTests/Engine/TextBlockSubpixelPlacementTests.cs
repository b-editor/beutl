using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;

namespace Beutl.UnitTests.Engine;

[TestFixture]
public class TextBlockSubpixelPlacementTests
{
    private const int CanvasWidth = 800;
    private const int CanvasHeight = 400;
    private const int SampleCount = 10;
    private const float SampleStep = 0.1f;

    // Skia snaps a text run's baseline to whole device pixels unless SKFont.BaselineSnap is off,
    // which made an animated TextBlock advance vertically in 1 px jumps. With snapping off the
    // placement still quantizes to the glyph rasterizer's quarter-pixel phase, so these assertions
    // pin down the whole-pixel step being gone, not continuous placement.
    [Test]
    public void VerticalPlacement_TracksSubPixelOffsets()
    {
        Typeface typeface = TypefaceProvider.Typeface();
        float[] centroids = new float[SampleCount];
        for (int i = 0; i < SampleCount; i++)
        {
            centroids[i] = MeasureInkCentroidY(typeface, i * SampleStep);
        }

        float maxStep = 0;
        for (int i = 1; i < centroids.Length; i++)
        {
            maxStep = MathF.Max(maxStep, MathF.Abs(centroids[i] - centroids[i - 1]));
        }

        string measured = string.Join(", ", centroids.Select(c => c.ToString("F3")));
        Assert.Multiple(() =>
        {
            Assert.That(maxStep, Is.LessThan(0.5f),
                $"a {SampleStep} px offset moved the text by {maxStep:F3} px. Centroids: {measured}");
            Assert.That(centroids.Distinct().Count(), Is.GreaterThanOrEqualTo(4),
                $"distinct positions over a {SampleStep * (SampleCount - 1):F1} px sweep. Centroids: {measured}");
        });
    }

    private static float MeasureInkCentroidY(Typeface typeface, float translateY)
    {
        var textBlock = new TextBlock();
        textBlock.FontFamily.CurrentValue = typeface.FontFamily;
        textBlock.FontStyle.CurrentValue = typeface.Style;
        textBlock.FontWeight.CurrentValue = typeface.Weight;
        textBlock.Size.CurrentValue = 40;
        textBlock.Fill.CurrentValue = Brushes.White;
        textBlock.Text.CurrentValue = "Beutl";

        var transform = new TranslateTransform();
        transform.Y.CurrentValue = translateY;
        var group = new TransformGroup();
        group.Children.Add(transform);
        textBlock.Transform.CurrentValue = group;

        Drawable.Resource resource = (Drawable.Resource)textBlock.ToResource(CompositionContext.Default);
        var node = new DrawableRenderNode(resource);
        using (var context = new GraphicsContext2D(node, new Size(CanvasWidth, CanvasHeight)))
        {
            textBlock.Render(context, resource);
        }

        RenderTarget? created = RenderTarget.Create(CanvasWidth, CanvasHeight);
        Assume.That(created, Is.Not.Null, "no render target backend is available in this environment");

        using RenderTarget renderTarget = created!;
        using (var canvas = new ImmediateCanvas(renderTarget))
        {
            canvas.Clear();
            using var renderer = new RenderNodeRenderer(
                node,
                new RenderNodeRendererOptions {
                    DefaultRequest = new RenderNodeRenderRequest
                    {
                        UseRenderCache = false,
                    },
                });
            renderer.Render(canvas);
        }

        using Bitmap snapshot = renderTarget.Snapshot();
        using Bitmap bitmap = snapshot.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Unpremul,
            BitmapColorSpace.Srgb);

        double totalAlpha = 0;
        double weightedY = 0;
        for (int y = 0; y < CanvasHeight; y++)
        {
            Span<byte> row = bitmap.GetRow(y);
            double rowAlpha = 0;
            for (int x = 0; x < CanvasWidth; x++)
            {
                rowAlpha += row[(x * 4) + 3];
            }

            totalAlpha += rowAlpha;
            weightedY += rowAlpha * y;
        }

        Assert.That(totalAlpha, Is.GreaterThan(0), "the text rendered nothing, so the measurement is meaningless");
        return (float)(weightedY / totalAlpha);
    }
}
