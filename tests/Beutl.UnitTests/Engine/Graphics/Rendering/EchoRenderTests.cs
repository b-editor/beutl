using Beutl.Animation;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.UnitTests.Engine.Graphics.Backend;
using Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
[NonParallelizable]
public class EchoRenderTests
{
    private readonly DecoderRegistry _decoders = TestMediaHelper.CreateRegistry();

    [TestCase(-1f, 2)]
    [TestCase(1f, 1)]
    public void NewestOnTopPutsTheLaterColorAboveTheEarlierColor(float interval, double time)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var echo = new Echo
            {
                Interval = { Value = interval },
                Count = { Value = 2 },
                Decay = { Value = 100 },
                BlendMode = { Value = EchoBlendMode.NewestOnTop },
            };
            using var renderer = CreateRenderer(TimeColoredShape(echo));
            Rgba pixel = ReadCenter(renderer, time);

            // Both samples are opaque and overlap. Drawing in the wrong order makes the center red.
            using (Assert.EnterMultipleScope())
            {
                Assert.That(pixel.Red, Is.EqualTo(0).Within(0.003));
                Assert.That(pixel.Blue, Is.EqualTo(1).Within(0.003));
                Assert.That(pixel.Alpha, Is.EqualTo(1).Within(0.003));
            }
        });
    }

    [Test]
    public void AddSumsTheOverlappingColorsAndAlpha()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var echo = new Echo
            {
                Interval = { Value = -1 },
                Count = { Value = 2 },
                StartingIntensity = { Value = 50 },
                Decay = { Value = 100 },
                BlendMode = { Value = EchoBlendMode.NewestOnTop },
            };
            using var renderer = CreateRenderer(TimeColoredShape(echo));
            Rgba over = ReadCenter(renderer, 2);
            echo.BlendMode.Value = EchoBlendMode.Add;
            Rgba added = ReadCenter(renderer, 2);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(over.Red, Is.EqualTo(0.25).Within(0.003));
                Assert.That(over.Blue, Is.EqualTo(0.5).Within(0.003));
                Assert.That(over.Alpha, Is.EqualTo(0.75).Within(0.003));
                Assert.That(added.Red, Is.EqualTo(0.5).Within(0.003));
                Assert.That(added.Blue, Is.EqualTo(0.5).Within(0.003));
                Assert.That(added.Alpha, Is.EqualTo(1).Within(0.003));
            }
        });
    }

    [Test]
    public void NonFiniteIntervalDrawsRepeatedCopiesOfTheCurrentTime()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var echo = new Echo
            {
                Count = { Value = 2 },
                StartingIntensity = { Value = 50 },
                Decay = { Value = 100 },
                BlendMode = { Value = EchoBlendMode.NewestOnTop },
            };
            EchoTests.SetUnvalidated(echo.Interval, float.NegativeInfinity);
            using var renderer = CreateRenderer(TimeColoredShape(echo));
            Rgba pixel = ReadCenter(renderer, 2);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(pixel.Red, Is.EqualTo(0).Within(0.003));
                Assert.That(pixel.Blue, Is.EqualTo(0.75).Within(0.003));
                Assert.That(pixel.Alpha, Is.EqualTo(0.75).Within(0.003));
            }
        });
    }

    [Test]
    public void ZeroStartingIntensityDrawsNothing()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var echo = new Echo();
            using var renderer = CreateRenderer(TimeColoredShape(echo));
            Assert.That(ReadCenter(renderer, 2).Alpha, Is.EqualTo(1).Within(0.003));

            echo.StartingIntensity.Value = 0;
            using Bitmap image = Render(renderer, 2);
            Assert.That(ImageMetrics.HasFiniteVisibleContent(image), Is.False);
        });
    }

    [Test]
    public void SamplesUsePrecedingEffectsAndTheCompositeUsesFollowingEffectsAndGroupOpacity()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var prefix = new OpacityEffect();
            MotionBlurTests.Animate(prefix.Opacity, 0, 100);
            var echo = new Echo
            {
                Interval = { Value = -1 },
                Count = { Value = 2 },
                Decay = { Value = 100 },
                BlendMode = { Value = EchoBlendMode.NewestOnTop },
            };
            var suffix = new Threshold { Value = { Value = 60 }, IsEnabled = { Value = false } };
            var shape = new RectShape
            {
                Width = { Value = 60 },
                Height = { Value = 40 },
                Fill = { Value = new SolidColorBrush(Colors.White) },
                Opacity = { Value = 50 },
                FilterEffect = { Value = new FilterEffectGroup { Children = { prefix, echo, suffix } } },
            };
            var group = new DrawableGroup { Opacity = { Value = 50 }, Children = { shape } };
            using var renderer = CreateRenderer(group);

            // Sample alphas 0.25 and 0.5 combine to 0.625. The enclosing group halves the composite alpha.
            Assert.That(ReadCenter(renderer, 2).Alpha, Is.EqualTo(0.3125).Within(0.003));
            // Only the composite passes the threshold. The enclosing group then halves the resulting alpha of 1.
            suffix.IsEnabled.Value = true;
            Assert.That(ReadCenter(renderer, 2).Alpha, Is.EqualTo(0.5).Within(0.003));
        });
    }

    [Test]
    public void TheDefaultsShowEchoesOnAnOpaqueFullFrameMovingPicture()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var echo = new Echo();
            using var renderer = CreateRenderer(BarSweepingOverBlack(echo));
            using Bitmap defaults = Render(renderer, 2);
            echo.BlendMode.Value = EchoBlendMode.NewestOnTop;
            using Bitmap newestOnTop = Render(renderer, 2);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(ImageMetrics.ReadPixel(defaults, 88, 32).Red, Is.EqualTo(1).Within(0.003), "the bar now");
                Assert.That(ImageMetrics.ReadPixel(defaults, 76, 32).Red, Is.EqualTo(0.7).Within(0.003), "the bar 0.1 s earlier");
                Assert.That(ImageMetrics.ReadPixel(defaults, 64, 32).Red, Is.EqualTo(0.49).Within(0.003), "the bar 0.2 s earlier");
                Assert.That(ImageMetrics.ReadPixel(defaults, 20, 32).Red, Is.Zero.Within(0.003), "the background");
                Assert.That(ImageMetrics.ReadPixel(defaults, 76, 32).Alpha, Is.EqualTo(1).Within(0.003));
                Assert.That(ImageMetrics.ReadPixel(newestOnTop, 76, 32).Red, Is.Zero.Within(0.003),
                    "drawn over its echoes, the opaque picture now hides them");
            }
        });
    }

    // A white bar sweeping over a black frame, 12 pixels to the right every 0.1 s, centered at x = 88 at 2 s.
    private static DrawableGroup BarSweepingOverBlack(Echo echo)
    {
        var move = new TranslateTransform();
        MotionBlurTests.Animate(move.X, -200, 40);
        return new DrawableGroup
        {
            FilterEffect = { Value = echo },
            Children =
            {
                new RectShape { Width = { Value = 96 }, Height = { Value = 64 }, Fill = { Value = new SolidColorBrush(Colors.Black) } },
                new RectShape
                {
                    Width = { Value = 8 },
                    Height = { Value = 64 },
                    Fill = { Value = new SolidColorBrush(Colors.White) },
                    Transform = { Value = move },
                },
            },
        };
    }

    private static RectShape TimeColoredShape(Echo echo)
    {
        var fill = new SolidColorBrush();
        fill.Color.SetKeyFrameAnimation(new KeyFrameAnimation<Color>
        {
            KeyFrames =
            {
                new KeyFrame<Color> { KeyTime = { Value = MediaTime.FromSeconds(1) }, Value = { Value = Colors.Red } },
                new KeyFrame<Color> { KeyTime = { Value = MediaTime.FromSeconds(2) }, Value = { Value = Colors.Blue } },
            },
        });
        return new RectShape
        {
            Width = { Value = 60 },
            Height = { Value = 40 },
            Fill = { Value = fill },
            FilterEffect = { Value = echo },
        };
    }

    private SceneRenderer CreateRenderer(Drawable drawable)
        => new(MotionBlurTests.SceneWith(drawable), new SceneRenderServices(TestFonts.Manager, _decoders), RenderIntent.Delivery);

    private static Rgba ReadCenter(SceneRenderer renderer, double time)
    {
        using Bitmap image = Render(renderer, time);
        return ImageMetrics.ReadPixel(image, 48, 32);
    }

    private static Bitmap Render(SceneRenderer renderer, double time)
    {
        renderer.Render(renderer.Compositor.EvaluateGraphics(MediaTime.FromSeconds(time), new Rational(2, 1)));
        return renderer.Snapshot();
    }
}
