using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Cache;

[TestFixture]
public sealed class ComposedSceneRenderCacheTests
{
    private static readonly PixelSize s_frameSize = new(240, 160);
    private static readonly Rect s_frameBounds = new(default, s_frameSize.ToSize(1));

    [Test]
    public void ComposedScene_CacheHitAndDisabledRenderAreByteIdentical()
    {
        RenderThread.Dispatcher.Invoke(() =>
        {
            Drawable.Resource[] resources = CreateSceneResources();
            try
            {
                using var root = new DrawableRenderNode(resources[0]);
                using (var context = new GraphicsContext2D(root, s_frameSize.ToSize(1)))
                {
                    context.Clear();
                    foreach (Drawable.Resource resource in resources)
                    {
                        context.DrawDrawable(resource);
                    }
                }

                GeometryRenderNode? cacheableBackground = Descendants(root)
                    .OfType<GeometryRenderNode>()
                    .FirstOrDefault();
                Assert.That(cacheableBackground, Is.Not.Null,
                    "the composed fixture must include a cacheable shape subtree");
                cacheableBackground!.Cache.ReportRenderCount(RenderNodeCache.Count);

                var diagnostics = new RenderPipelineDiagnosticsState();
                using var cachedRenderer = CreateRenderer(root, useRenderCache: true, diagnostics);
                using RenderNodeRasterization first = cachedRenderer.Rasterize();
                using RenderNodeRasterization second = cachedRenderer.Rasterize();

                using var uncachedRenderer = CreateRenderer(root, useRenderCache: false);
                using RenderNodeRasterization control = uncachedRenderer.Rasterize();

                byte[] firstPixels = GetPixels(first);
                byte[] secondPixels = GetPixels(second);
                byte[] controlPixels = GetPixels(control);

                Assert.Multiple(() =>
                {
                    Assert.That(firstPixels, Has.Some.Not.Zero,
                        "the composed fixture must produce visible pixels");
                    Assert.That(second.Bounds, Is.EqualTo(first.Bounds));
                    Assert.That(control.Bounds, Is.EqualTo(first.Bounds));
                    Assert.That(secondPixels, Is.EqualTo(firstPixels),
                        "the cache-hit render must be byte-identical to the cache-miss render. "
                        + DescribeDifference(firstPixels, secondPixels));
                    Assert.That(controlPixels, Is.EqualTo(firstPixels),
                        "cache policy must not change the composed scene output. "
                        + DescribeDifference(firstPixels, controlPixels));
                    Assert.That(
                        diagnostics.Latest[RenderPipelineCounter.RenderCacheHits],
                        Is.GreaterThan(0),
                        "the second enabled render must exercise a persistent render-cache hit");
                });
            }
            finally
            {
                foreach (Drawable.Resource resource in resources)
                {
                    resource.Dispose();
                }
            }
        });
    }

    private static RenderNodeRenderer CreateRenderer(
        RenderNode root,
        bool useRenderCache,
        IRenderPipelineDiagnosticsState? diagnostics = null)
    {
        return new RenderNodeRenderer(
            root,
            new RenderNodeRendererOptions
            {
                TargetDomain = s_frameBounds,
                UseRenderCache = useRenderCache,
                RenderPurpose = RenderRequestPurpose.Frame,
                TargetFactory = new CpuTargetFactory(),
                Diagnostics = diagnostics,
            });
    }

    private static IEnumerable<RenderNode> Descendants(RenderNode node)
    {
        yield return node;
        if (node is ContainerRenderNode container)
        {
            foreach (RenderNode child in container.Children)
            {
                foreach (RenderNode descendant in Descendants(child))
                {
                    yield return descendant;
                }
            }
        }
    }

    private static byte[] GetPixels(RenderNodeRasterization rasterization)
    {
        Assert.That(rasterization.IsEmpty, Is.False);
        return rasterization.Bitmap!.GetPixelSpan().ToArray();
    }

    private static string DescribeDifference(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual)
    {
        int differing = 0;
        int first = -1;
        int maximum = 0;
        for (int i = 0; i < expected.Length; i++)
        {
            int delta = Math.Abs(expected[i] - actual[i]);
            if (delta == 0)
                continue;

            first = first < 0 ? i : first;
            differing++;
            maximum = Math.Max(maximum, delta);
        }

        return $"{differing} bytes differ; first index {first}; maximum byte delta {maximum}.";
    }

    private static Drawable.Resource[] CreateSceneResources()
    {
        var background = new RectShape
        {
            Width = { CurrentValue = 240 },
            Height = { CurrentValue = 160 },
            Fill = { CurrentValue = Brushes.CornflowerBlue },
        };

        var accent = new EllipseShape
        {
            Width = { CurrentValue = 76 },
            Height = { CurrentValue = 76 },
            Fill = { CurrentValue = Brushes.OrangeRed },
            FilterEffect =
            {
                CurrentValue = new Brightness
                {
                    Amount = { CurrentValue = 78 },
                },
            },
            Transform = { CurrentValue = new TranslateTransform(44, -18) },
        };

        var label = new TextBlock
        {
            FontFamily = { CurrentValue = FontFamily.Default },
            Size = { CurrentValue = 28 },
            Fill = { CurrentValue = Brushes.White },
            Text = { CurrentValue = "CACHE" },
            Transform = { CurrentValue = new TranslateTransform(-28, 30) },
        };

        CompositionContext context = CompositionContext.Default;
        return
        [
            background.ToResource(context),
            accent.ToResource(context),
            label.ToResource(context),
        ];
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
