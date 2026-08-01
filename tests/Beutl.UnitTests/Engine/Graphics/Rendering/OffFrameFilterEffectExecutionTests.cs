using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
public sealed class OffFrameFilterEffectExecutionTests
{
    private static readonly Rect s_frame = new(0, 0, 320, 180);

    [Test]
    public void FullyOffFrameBlur_IsEquivalentToOmittingTheElement()
    {
        using SceneGraph control = CreateScene(visibleCount: 1, includeOffFrameEffect: false);
        using SceneGraph actual = CreateScene(visibleCount: 1, includeOffFrameEffect: true);

        Assert.That(Render(actual.Root), Is.EqualTo(Render(control.Root)));
    }

    [Test]
    public void FullyOffFrameBlur_DoesNotSuppressFiveVisibleElements()
    {
        using SceneGraph control = CreateScene(visibleCount: 5, includeOffFrameEffect: false);
        using SceneGraph actual = CreateScene(visibleCount: 5, includeOffFrameEffect: true);

        byte[] expected = Render(control.Root);
        byte[] rendered = Render(actual.Root);
        Assert.Multiple(() =>
        {
            Assert.That(rendered, Has.Some.Not.Zero);
            Assert.That(rendered, Is.EqualTo(expected));
        });
    }

    [Test]
    public void StraddlingBlur_StillRendersItsVisibleFootprint()
    {
        const float sigma = 4;
        const int sampleX = 3;
        using SceneGraph scene = CreateScene(visibleCount: 0, includeOffFrameEffect: true, offFrameX: -78);

        byte[] rendered = Render(scene.Root);

        // The source body occupies x=-78..2. Column 3 is an on-frame tail sample less than
        // one sigma from the frame edge and contains no unblurred source coverage.
        float actualTail = MaximumAlphaInColumn(rendered, sampleX, yStart: 56, yEnd: 88);
        double sampleCenter = sampleX + 0.5;
        double expectedTail = GaussianIntervalCoverage(
            sourceStart: -78,
            sourceEnd: 2,
            sampleCenter,
            sigma);
        Assert.Multiple(() =>
        {
            Assert.That(
                actualTail,
                Is.GreaterThan(0.05f),
                "The visible blur tail inside one sigma of the frame edge was dropped.");
            Assert.That(
                actualTail,
                Is.EqualTo(expectedTail).Within(0.04),
                "The clipped on-frame tail must follow the Gaussian interval profile.");
        });
    }

    private static SceneGraph CreateScene(
        int visibleCount,
        bool includeOffFrameEffect,
        float offFrameX = -1_500)
    {
        var drawables = new List<Drawable.Resource>();
        for (int index = 0; index < visibleCount; index++)
        {
            float x = 12 + (index * 58);
            float y = 18 + ((index % 2) * 66);
            var visible = new RectShape
            {
                Width = { CurrentValue = 44 },
                Height = { CurrentValue = 52 },
                Fill =
                {
                    CurrentValue = index % 2 == 0
                        ? Brushes.White
                        : Brushes.OrangeRed,
                },
                Transform = { CurrentValue = new TranslateTransform(x, y) },
            };
            drawables.Add(visible.ToResource(CompositionContext.Default));
        }

        if (includeOffFrameEffect)
        {
            var offFrame = new RectShape
            {
                Width = { CurrentValue = 80 },
                Height = { CurrentValue = 64 },
                Fill = { CurrentValue = Brushes.CornflowerBlue },
                AlignmentX = { CurrentValue = AlignmentX.Left },
                AlignmentY = { CurrentValue = AlignmentY.Top },
                Transform = { CurrentValue = new TranslateTransform(offFrameX, 40) },
                FilterEffect =
                {
                    CurrentValue = new Blur
                    {
                        Sigma = { CurrentValue = new Size(4, 4) },
                    },
                },
            };
            drawables.Add(offFrame.ToResource(CompositionContext.Default));
        }

        if (drawables.Count == 0)
            throw new InvalidOperationException("The scene fixture must contain at least one drawable.");

        var root = new DrawableRenderNode(drawables[0]);
        using (var context = new GraphicsContext2D(root, s_frame.Size))
        {
            context.Clear();
            foreach (Drawable.Resource drawable in drawables)
            {
                context.DrawDrawable(drawable);
            }
        }

        return new SceneGraph(root, drawables);
    }

    private static byte[] Render(RenderNode root)
    {
        using var target = new CpuRenderTarget((int)s_frame.Width, (int)s_frame.Height);
        using var destination = new ImmediateCanvas(target, logicalSize: s_frame.Size);
        using var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    TargetDomain = s_frame,
                    RequestedRegion = s_frame,
                    OutputScale = 1,
                    MaxWorkingScale = 1,
                    UseRenderCache = false,
                },
                TargetFactory = new CpuTargetFactory(),
            });
        renderer.Render(destination);
        using Bitmap result = target.Snapshot();
        return result.GetPixelSpan().ToArray();
    }

    private static float MaximumAlphaInColumn(byte[] pixels, int x, int yStart, int yEnd)
    {
        float maximum = 0;
        for (int y = yStart; y < yEnd; y++)
        {
            maximum = Math.Max(maximum, ReadAlpha(pixels, x, y));
        }

        return maximum;
    }

    private static double GaussianIntervalCoverage(
        double sourceStart,
        double sourceEnd,
        double sampleCenter,
        double sigma)
    {
        static double NormalCdf(double value)
            => 0.5 * (1 + ErrorFunction(value / Math.Sqrt(2)));

        return NormalCdf((sourceEnd - sampleCenter) / sigma)
               - NormalCdf((sourceStart - sampleCenter) / sigma);
    }

    private static double ErrorFunction(double value)
    {
        const double p = 0.3275911;
        const double a1 = 0.254829592;
        const double a2 = -0.284496736;
        const double a3 = 1.421413741;
        const double a4 = -1.453152027;
        const double a5 = 1.061405429;

        double sign = Math.Sign(value);
        double x = Math.Abs(value);
        double t = 1 / (1 + p * x);
        double approximation = 1
                               - (((((a5 * t + a4) * t) + a3) * t + a2) * t + a1)
                               * t
                               * Math.Exp(-x * x);
        return sign * approximation;
    }

    private static float ReadAlpha(byte[] pixels, int x, int y)
    {
        int offset = ((y * (int)s_frame.Width) + x) * 8;
        ushort bits = BitConverter.ToUInt16(pixels, offset + 6);
        return (float)BitConverter.UInt16BitsToHalf(bits);
    }

    private sealed class SceneGraph(
        DrawableRenderNode root,
        IReadOnlyList<Drawable.Resource> drawables) : IDisposable
    {
        public DrawableRenderNode Root { get; } = root;

        public void Dispose()
        {
            Root.Dispose();
            foreach (Drawable.Resource drawable in drawables)
            {
                drawable.Dispose();
            }
        }
    }

    private sealed class CpuTargetFactory : IRenderTargetFactory
    {
        public RenderTarget Create(RenderTargetAllocationDescriptor allocation)
            => new CpuRenderTarget(allocation.DeviceSize.Width, allocation.DeviceSize.Height);
    }

    private sealed class CpuRenderTarget(int width, int height)
        : RenderTarget(
            SKSurface.Create(new SKImageInfo(
                width,
                height,
                SKColorType.RgbaF16,
                SKAlphaType.Premul,
                SKColorSpace.CreateSrgbLinear())),
            width,
            height);
}
