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
        using SceneGraph scene = CreateScene(visibleCount: 0, includeOffFrameEffect: true, offFrameX: -40);
        using SceneGraph reference = CreateScene(visibleCount: 0, includeOffFrameEffect: true, offFrameX: 40);

        byte[] rendered = Render(scene.Root);
        byte[] expected = Render(reference.Root);

        // The body occupies x=-40..40, so x=41..51 contains only its on-frame blur tail.
        float tailMaximum = 0;
        for (int y = 48; y < 96; y++)
        {
            for (int x = 41; x <= 51; x++)
            {
                tailMaximum = Math.Max(tailMaximum, ReadAlpha(rendered, x, y));
            }
        }

        // The reference is the same drawable shifted 80 logical/device pixels onto the frame.
        // A near-tail column must therefore retain the same Gaussian coverage after clipping.
        float actualBorder = MaximumAlphaInColumn(rendered, x: 49, yStart: 48, yEnd: 96);
        float expectedBorder = MaximumAlphaInColumn(expected, x: 129, yStart: 48, yEnd: 96);
        Assert.Multiple(() =>
        {
            Assert.That(tailMaximum, Is.GreaterThan(0.001f), "The visible blur tail was dropped.");
            Assert.That(expectedBorder, Is.GreaterThan(0.001f), "The reference column must sample the blur tail.");
            Assert.That(
                actualBorder,
                Is.EqualTo(expectedBorder).Within(0.0005f),
                "Clipping a straddling blur must preserve its on-frame tail coverage.");
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
                TargetDomain = s_frame,
                RequestedRegion = s_frame,
                OutputScale = 1,
                MaxWorkingScale = 1,
                UseRenderCache = false,
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
        public RenderTarget Create(PixelSize deviceSize)
            => new CpuRenderTarget(deviceSize.Width, deviceSize.Height);
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
