using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Graphics.Transitions;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.ProjectSystem;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Graphics;

public partial class TransitionRenderTests
{
    private static readonly Type[] s_stylizedTransitions =
    [
        typeof(ZoomBlurTransition), typeof(WhipPanTransition), typeof(GlitchTransition), typeof(LightLeakTransition),
    ];

    [TestCaseSource(nameof(s_stylizedTransitions))]
    public void StylizedTransitions_RestoreEndpointsAndKeepOpaqueClipsOpaque(Type type)
    {
        foreach (double progress in new[] { 0, 0.25, 0.5, 0.75, 1 })
        {
            using Bitmap bitmap = Render((ClipTransition)Activator.CreateInstance(type)!, progress);
            Bgra8888[] pixels = bitmap.GetPixelSpan<Bgra8888>().ToArray();
            Assert.That(pixels.Min(p => p.A), Is.GreaterThanOrEqualTo(254), $"opaque at {progress}");
            if (progress == 0) Assert.That(pixels.All(IsRed), Is.True, "no effect remains on the first frame");
            if (progress == 1) Assert.That(pixels.All(IsBlue), Is.True, "no effect remains on the last frame");
        }
    }

    [TestCaseSource(nameof(s_stylizedTransitions))]
    public void StylizedTransitions_ResampleThePicture_AndReplayTheSameFrameAfterSeeking(Type type)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var transition = (ClipTransition)Activator.CreateInstance(type)!;
            Scene scene = CreateStripedScene(transition);
            foreach (float scale in new[] { 1f, 0.5f })
            {
                using var renderer = new SceneRenderer(scene, RenderIntent.Delivery, renderScale: scale);
                Bgra8888[] atCut = Sample(0.42);
                Sample(0.8);
                Bgra8888[] afterSeek = Sample(0.42);
                Assert.That(afterSeek, Is.EqualTo(atCut), "a seek must reproduce the same blur, bands and light");

                float previousStrength = StrengthOf(transition).Value;
                StrengthOf(transition).Value = 0;
                Bgra8888[] disabled = Sample(0.42);
                double difference = atCut.Zip(disabled, (a, b) =>
                    (Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B)) / 3.0).Average();
                Assert.That(difference, Is.GreaterThan(5), "the shader must change the picture, not silently fall back");
                Assert.That(disabled.Min(p => p.A), Is.EqualTo(255));
                StrengthOf(transition).Value = previousStrength;

                Bgra8888[] Sample(double progress)
                {
                    renderer.Render(renderer.Compositor.EvaluateGraphics(TimeAt(progress)));
                    using Bitmap snapshot = renderer.Snapshot();
                    using Bitmap bitmap = snapshot.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Unpremul, BitmapColorSpace.Srgb);
                    return bitmap.GetPixelSpan<Bgra8888>().ToArray();
                }
            }
        });
    }

    [TestCaseSource(nameof(s_stylizedTransitions))]
    public void StylizedTransitions_PreserveTranslucencyAndAnAbsentSide(Type type)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            foreach (bool withOutgoing in new[] { true, false })
            {
                var transition = (ClipTransition)Activator.CreateInstance(type)!;
                var halfBlue = Color.FromArgb(128, 0, 0, 255);
                (Scene scene, RectShape incoming) = CreateScene(transition, withOutgoing, CreateRect(halfBlue));
                incoming.Fill.Value = new SolidColorBrush(halfBlue);
                using var renderer = new SceneRenderer(scene, RenderIntent.Delivery);
                renderer.Render(renderer.Compositor.EvaluateGraphics(TimeAt(0.5)));
                using Bitmap snapshot = renderer.Snapshot();
                using Bitmap bitmap = snapshot.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Unpremul, BitmapColorSpace.Srgb);
                Bgra8888[] pixels = bitmap.GetPixelSpan<Bgra8888>().ToArray();
                Assert.That(pixels.Max(p => p.A), Is.LessThanOrEqualTo(129), "effects must not make translucent clips opaque");
                if (withOutgoing)
                    Assert.That(pixels.Min(p => p.A), Is.GreaterThanOrEqualTo(127));
                else
                    Assert.That(pixels.Average(p => p.A), Is.InRange(1, 126), "an isolated enter still reveals the clip gradually");
            }
        });
    }

    [Test]
    public void LightLeak_KeepsAnInsetClipsPositionAndItsTransparentSurroundings()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            (Scene scene, RectShape incoming) = CreateScene(new LightLeakTransition(), withOutgoing: false);
            incoming.Width.Value = 8;
            incoming.Height.Value = 6;
            incoming.AlignmentX.Value = AlignmentX.Left;
            incoming.AlignmentY.Value = AlignmentY.Top;
            incoming.Transform.Value = new TranslateTransform(8, 4);
            using var renderer = new SceneRenderer(scene, RenderIntent.Delivery);
            renderer.Render(renderer.Compositor.EvaluateGraphics(TimeAt(0.75)));
            using Bitmap snapshot = renderer.Snapshot();
            using Bitmap bitmap = snapshot.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Unpremul, BitmapColorSpace.Srgb);
            Assert.That(Pixel(bitmap, 12, 6).A, Is.EqualTo(255), "the clip keeps its original position");
            Assert.That(bitmap.GetPixelSpan<Bgra8888>().ToArray().Count(p => p.A > 0), Is.EqualTo(8 * 6),
                "light must not paint the empty space outside the clip");
        });
    }

    [Test]
    public void WhipPan_UsesTheSelectedAxis([Values] ClipTransitionDirection direction)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            Scene scene = CreateStripedScene(new WhipPanTransition { Direction = { Value = direction } });
            using var renderer = new SceneRenderer(scene, RenderIntent.Delivery);
            renderer.Render(renderer.Compositor.EvaluateGraphics(TimeAt(0.5)));
            using Bitmap snapshot = renderer.Snapshot();
            using Bitmap bitmap = snapshot.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Unpremul, BitmapColorSpace.Srgb);
            Bgra8888 pixel = Pixel(bitmap, 10, Height / 2);
            Assert.That(pixel.R, direction is ClipTransitionDirection.TopToBottom or ClipTransitionDirection.BottomToTop
                ? Is.GreaterThan(240) : Is.LessThan(235), "only horizontal movement smears vertical bars");
        });
    }

    [TestCaseSource(nameof(s_stylizedTransitions))]
    public void StylizedTransitions_RenderDetailedPreview(Type type)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var transition = (ClipTransition)Activator.CreateInstance(type)!;
            var scene = new Scene(480, 270, string.Empty);
            for (int side = 0; side < 2; side++)
            {
                var element = new Element
                {
                    Start = { Value = MediaTime.FromSeconds(side) },
                    Length = { Value = MediaTime.FromSeconds(side == 0 ? 1 : 2) },
                };
                AddPreviewArtwork(element, side == 1);
                if (side == 1)
                {
                    transition.Duration.Value = MediaTime.FromSeconds(1);
                    element.EnterTransition.Value = transition;
                }
                scene.Children.Add(element);
            }

            var caption = new Element { Length = { Value = MediaTime.FromSeconds(3) }, ZIndex = { Value = 1 } };
            caption.Objects.Add(new RectShape
            {
                Width = { Value = 480 },
                Height = { Value = 30 },
                AlignmentY = { Value = AlignmentY.Top },
                Fill = { Value = new SolidColorBrush(Color.FromRgb(9, 13, 25)) },
            });
            caption.Objects.Add(new TextBlock
            {
                Text = { Value = type.Name.Replace("Transition", "").Replace("Blur", " BLUR").Replace("Pan", " PAN").Replace("Leak", " LEAK").ToUpperInvariant() },
                Size = { Value = 13 },
                FontWeight = { Value = FontWeight.Bold },
                AlignmentX = { Value = AlignmentX.Left },
                AlignmentY = { Value = AlignmentY.Top },
                Transform = { Value = new TranslateTransform(16, 7) },
            });
            scene.Children.Add(caption);

            using var renderer = new SceneRenderer(scene, RenderIntent.Delivery);
            string? root = Environment.GetEnvironmentVariable("BEUTL_HEADLESS_CAPTURE_DIR");
            int frameCount = root is { Length: > 0 } ? 61 : 3;
            string? directory = root is { Length: > 0 } ? Path.Combine(root, type.Name) : null;
            if (directory != null) Directory.CreateDirectory(directory);
            for (int frame = 0; frame < frameCount; frame++)
            {
                double progress = (double)frame / (frameCount - 1);
                renderer.Render(renderer.Compositor.EvaluateGraphics(TimeAt(progress)));
                using Bitmap snapshot = renderer.Snapshot();
                using Bitmap bitmap = snapshot.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Unpremul, BitmapColorSpace.Srgb);
                Assert.That(bitmap.GetPixelSpan<Bgra8888>().ToArray().Min(p => p.A), Is.GreaterThanOrEqualTo(254));
                if (directory != null) Assert.That(bitmap.Save(Path.Combine(directory, $"{frame:000}.png")), Is.True);
            }
        });
    }

    private static IExpressionProperty<float> StrengthOf(ClipTransition transition) => transition switch
    {
        ZoomBlurTransition value => value.Strength,
        WhipPanTransition value => value.Strength,
        GlitchTransition value => value.Strength,
        LightLeakTransition value => value.Strength,
        _ => throw new ArgumentException("Not a stylized transition.", nameof(transition)),
    };

    private static Scene CreateStripedScene(ClipTransition transition)
    {
        (Scene scene, RectShape incoming) = CreateScene(transition, true, CreateRect(Colors.Black));
        incoming.Fill.Value = new SolidColorBrush(Colors.Black);
        foreach (Element element in scene.Children)
        {
            for (int x = 8; x < Width; x += 16)
            {
                RectShape bar = CreateRect(Colors.White);
                bar.Width.Value = 6;
                bar.Transform.Value = new TranslateTransform(x - Width / 2f, 0);
                element.Objects.Add(bar);
            }
        }
        return scene;
    }

    private static void AddPreviewArtwork(Element element, bool incoming)
    {
        Color accent = incoming ? Color.FromRgb(255, 80, 150) : Color.FromRgb(60, 230, 235);
        var gradient = new LinearGradientBrush();
        gradient.GradientStops.Add(new GradientStop(incoming ? Color.FromRgb(52, 10, 54) : Color.FromRgb(8, 28, 56), 0));
        gradient.GradientStops.Add(new GradientStop(Color.FromRgb(7, 8, 22), 1));
        element.Objects.Add(new RectShape { Width = { Value = 480 }, Height = { Value = 270 }, Fill = { Value = gradient } });
        for (int x = 0; x < 480; x += 30)
            AddRect(x, 0, 1, 270, Color.FromArgb(30, accent.R, accent.G, accent.B));
        for (int y = 0; y < 270; y += 30)
            AddRect(0, y, 480, 1, Color.FromArgb(30, accent.R, accent.G, accent.B));
        element.Objects.Add(new EllipseShape
        {
            Width = { Value = 180 },
            Height = { Value = 180 },
            Fill = { Value = new SolidColorBrush(accent) },
            Transform = { Value = new TranslateTransform(incoming ? -135 : 135, incoming ? 45 : -35) },
        });
        AddRect(32, 43, 42, 4, accent);
        AddText(32, 60, 14, incoming ? "02 / AFTERGLOW" : "01 / NIGHT DRIVE", accent);
        AddText(30, 102, 51, incoming ? "MAKE IT" : "FEEL THE", Colors.White);
        AddText(30, 154, 51, incoming ? "GLOW." : "MOTION.", Colors.White);
        AddText(32, 232, 10, "BEUTL  /  MOTION STUDY", accent);
        for (int i = 0; i < 7; i++) AddRect(345 + i * 12, 225 - i * 7, 6, 20 + i * 7, accent);

        void AddRect(float x, float y, float width, float height, Color color)
        {
            element.Objects.Add(new RectShape
            {
                Width = { Value = width },
                Height = { Value = height },
                Fill = { Value = new SolidColorBrush(color) },
                AlignmentX = { Value = AlignmentX.Left },
                AlignmentY = { Value = AlignmentY.Top },
                Transform = { Value = new TranslateTransform(x, y) },
            });
        }

        void AddText(float x, float y, float size, string text, Color color)
        {
            element.Objects.Add(new TextBlock
            {
                Text = { Value = text },
                Size = { Value = size },
                FontWeight = { Value = FontWeight.Bold },
                Fill = { Value = new SolidColorBrush(color) },
                AlignmentX = { Value = AlignmentX.Left },
                AlignmentY = { Value = AlignmentY.Top },
                Transform = { Value = new TranslateTransform(x, y) },
            });
        }
    }
}
