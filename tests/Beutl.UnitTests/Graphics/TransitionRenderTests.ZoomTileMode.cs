using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transitions;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Graphics;

public partial class TransitionRenderTests
{
    [Test]
    public void ZoomTileModes_FillTheEdgesDifferentlyAndUpdateWhenChanged(
        [Values(typeof(ZoomTransition), typeof(ZoomBlurTransition))] Type type,
        [Values(1f, 0.5f)] float scale)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var transition = (ClipTransition)Activator.CreateInstance(type)!;
            if (transition is ZoomTransition zoom) zoom.Direction.Value = ZoomTransitionDirection.Out;
            else ((ZoomBlurTransition)transition).Direction.Value = ZoomTransitionDirection.Out;

            RectShape outgoing = CreateRect(s_red);
            outgoing.Fill.Value = CreateGradient();
            (var scene, RectShape incoming) = CreateScene(transition, withOutgoing: true, outgoing: outgoing);
            incoming.Fill.Value = CreateGradient();
            using var renderer = new SceneRenderer(scene, RenderIntent.Delivery, renderScale: scale);
            renderer.Render(renderer.Compositor.EvaluateGraphics(MediaTime.FromSeconds(0.5)));
            using Bitmap originalSnapshot = renderer.Snapshot();
            using Bitmap original = originalSnapshot.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Unpremul, BitmapColorSpace.Srgb);
            Bgra8888[] originalPixels = original.GetPixelSpan<Bgra8888>().ToArray();
            Bgra8888 originalEdge = Pixel(original, 0, (int)(Height / 2 * scale));
            foreach (GradientSpreadMethod tileMode in new[]
                     { GradientSpreadMethod.Decal, GradientSpreadMethod.Pad, GradientSpreadMethod.Reflect, GradientSpreadMethod.Repeat, GradientSpreadMethod.Decal })
            {
                ZoomTileModeOf(transition).Value = tileMode;
                // End and begin at the same playhead position to exercise cached mode changes.
                foreach (double progress in new[] { 0.5, 0, 1, 0.5 })
                {
                    renderer.Render(renderer.Compositor.EvaluateGraphics(TimeAt(progress)));
                    using Bitmap snapshot = renderer.Snapshot();
                    using Bitmap bitmap = snapshot.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Unpremul, BitmapColorSpace.Srgb);
                    Bgra8888 edge = Pixel(bitmap, 0, (int)(Height / 2 * scale));
                    if (progress is 0 or 1)
                    {
                        Bgra8888[] pixels = bitmap.GetPixelSpan<Bgra8888>().ToArray();
                        int colorDifference = pixels.Zip(originalPixels, static (actual, expected) =>
                            Math.Max(Math.Abs(actual.R - expected.R), Math.Max(
                                Math.Abs(actual.G - expected.G), Math.Abs(actual.B - expected.B)))).Max();
                        Assert.That(pixels.Select(p => p.A), Is.EqualTo(originalPixels.Select(p => p.A)));
                        // Isolated transition layers can round a color channel by one 8-bit step.
                        Assert.That(colorDifference, Is.LessThanOrEqualTo(1),
                            $"endpoints remain the original frame with {tileMode} at {progress}");
                    }
                    else if (tileMode == GradientSpreadMethod.Decal)
                    {
                        Assert.That(edge.A, Is.Zero, "decal keeps the previous transparent edge");
                    }
                    else
                    {
                        Assert.That(bitmap.GetPixelSpan<Bgra8888>().ToArray().Min(p => p.A), Is.GreaterThanOrEqualTo(254),
                            $"{tileMode} must fill the frame without a transparent seam");
                        if (tileMode == GradientSpreadMethod.Pad)
                        {
                            Assert.That(edge.R, Is.EqualTo(originalEdge.R).Within(2), "pad extends the leftmost pixel");
                            Assert.That(edge.B, Is.EqualTo(originalEdge.B).Within(2));
                        }
                        else if (tileMode == GradientSpreadMethod.Reflect)
                        {
                            Assert.That(edge.R, Is.GreaterThan(edge.B + 40), "reflection samples back into the left side");
                            Assert.That(edge.B, Is.GreaterThan(16), "reflection differs from edge padding");
                        }
                        else
                        {
                            Assert.That(edge.B, Is.GreaterThan(edge.R + 40), "repeat wraps to the blue right side");
                            Assert.That(edge.R, Is.GreaterThan(16), "repeat differs from edge padding");
                        }
                    }
                }
            }
        });

        static LinearGradientBrush CreateGradient()
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = { Value = new RelativePoint(0, 0.5f, RelativeUnit.Relative) },
                EndPoint = { Value = new RelativePoint(1, 0.5f, RelativeUnit.Relative) },
            };
            brush.GradientStops.Add(new GradientStop(s_red, 0));
            brush.GradientStops.Add(new GradientStop(s_blue, 1));
            return brush;
        }
    }

    private static IExpressionProperty<GradientSpreadMethod> ZoomTileModeOf(ClipTransition transition) => transition switch
    {
        ZoomTransition zoom => zoom.TileMode,
        ZoomBlurTransition zoomBlur => zoomBlur.TileMode,
        _ => throw new ArgumentException("Not a zoom transition.", nameof(transition)),
    };
}
