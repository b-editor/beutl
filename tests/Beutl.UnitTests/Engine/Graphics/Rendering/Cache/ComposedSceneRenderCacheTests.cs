using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Cache;

[NonParallelizable]
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

    [Test]
    public void Text_DeviceGridDependentCacheCandidateIsBypassedAndMatchesDisabledRender()
    {
        RenderThread.Dispatcher.Invoke(() =>
        {
            var text = new TextBlock
            {
                FontFamily = { CurrentValue = FontFamily.Default },
                Size = { CurrentValue = 72 },
                Fill = { CurrentValue = Brushes.White },
                Text = { CurrentValue = "ab" },
                AlignmentX = { CurrentValue = AlignmentX.Left },
                AlignmentY = { CurrentValue = AlignmentY.Top },
            };
            using Drawable.Resource resource = text.ToResource(CompositionContext.Default);

            AssertCacheAdmissionParity<TextRenderNode>(resource, expectCacheHit: false);
        });
    }

    [Test]
    public void FractionalDrawableBrush_DeviceGridDependentCacheCandidateIsBypassed()
    {
        RenderThread.Dispatcher.Invoke(() =>
        {
            using Drawable.Resource resource = CreateDrawableBrushHost(0.5f);

            AssertCacheAdmissionParity<GeometryRenderNode>(resource, expectCacheHit: false);
        });
    }

    [Test]
    public void IntegerPhaseText_CacheAdmissionAndReplayAreByteIdenticalToDisabledRender()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var text = new TextBlock
            {
                FontFamily = { CurrentValue = FontFamily.Default },
                Size = { CurrentValue = 72 },
                Fill = { CurrentValue = Brushes.White },
                Text = { CurrentValue = "ab" },
                Transform = { CurrentValue = new TranslateTransform(0, 0) },
            };
            using Drawable.Resource resource = text.ToResource(CompositionContext.Default);

            AssertProductionCacheSequenceParity(
                resource,
                RenderCacheOptions.Enabled,
                expectCacheHit: false);
        });
    }

    [TestCase(BlendMode.DstIn)]
    [TestCase(BlendMode.SrcIn)]
    [TestCase(BlendMode.DstATop)]
    public void PhaseUnsafeMaskScope_IsBypassedAndMatchesDisabledRender(BlendMode blendMode)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var group = new DrawableGroup();
            group.Children.Add(new RectShape
            {
                Width = { CurrentValue = 160 },
                Height = { CurrentValue = 160 },
                Fill = { CurrentValue = Brushes.White },
            });
            group.Children.Add(new EllipseShape
            {
                Width = { CurrentValue = 90 },
                Height = { CurrentValue = 90 },
                Fill = { CurrentValue = Brushes.White },
                BlendMode = { CurrentValue = blendMode },
            });
            using Drawable.Resource resource = group.ToResource(CompositionContext.Default);

            AssertProductionCacheSequenceParity(
                resource,
                RenderCacheOptions.Enabled,
                expectCacheHit: false);
        });
    }

    [Test]
    public void PlainGroup_IsAdmittedWhenCacheIsExplicitlyEnabled()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var group = new DrawableGroup();
            group.Children.Add(new RectShape
            {
                Width = { CurrentValue = 160 },
                Height = { CurrentValue = 160 },
                Fill = { CurrentValue = Brushes.White },
            });
            using Drawable.Resource resource = group.ToResource(CompositionContext.Default);

            AssertProductionCacheSequence(
                resource,
                RenderCacheOptions.Enabled,
                expectCacheHit: true,
                assertPixelParity: false);
        });
    }

    [Test]
    public void PlainGroup_DefaultRenderNodeRendererOptionsDoNotUsePersistentCache()
    {
        RenderThread.Dispatcher.Invoke(() =>
        {
            var group = new DrawableGroup();
            group.Children.Add(new RectShape
            {
                Width = { CurrentValue = 160 },
                Height = { CurrentValue = 160 },
                Fill = { CurrentValue = Brushes.White },
            });
            using Drawable.Resource resource = group.ToResource(CompositionContext.Default);
            using var root = new DrawableRenderNode(resource);
            using (var context = new GraphicsContext2D(root, s_frameSize.ToSize(1)))
            {
                context.Clear();
                context.DrawDrawable(resource);
            }

            GeometryRenderNode? cacheable = Descendants(root)
                .OfType<GeometryRenderNode>()
                .FirstOrDefault();
            Assert.That(cacheable, Is.Not.Null, "the plain group must contain an eligible geometry node");
            cacheable!.Cache.ReportRenderCount(RenderNodeCache.Count);

            var diagnostics = new RenderPipelineDiagnosticsState();
            var completedRequests = new List<RenderPipelineDiagnosticSnapshot>();
            diagnostics.RequestCompleted += completedRequests.Add;
            using var renderer = new RenderNodeRenderer(
                root,
                new RenderNodeRendererOptions
                {
                    DefaultRequest = new RenderNodeRenderRequest
                    {
                        TargetDomain = s_frameBounds,
                        Purpose = RenderRequestPurpose.Frame,
                        Diagnostics = diagnostics,
                    },
                    TargetFactory = new CpuTargetFactory(),
                });
            using RenderNodeRasterization first = renderer.Rasterize();
            using RenderNodeRasterization second = renderer.Rasterize();

            Assert.Multiple(() =>
            {
                Assert.That(
                    completedRequests.Sum(
                        static snapshot => snapshot[RenderPipelineCounter.RenderCacheCaptures]),
                    Is.Zero,
                    "default renderer options must not publish persistent cache entries");
                Assert.That(
                    completedRequests.Sum(
                        static snapshot => snapshot[RenderPipelineCounter.RenderCacheHits]),
                    Is.Zero,
                    "default renderer options must not read persistent cache entries");
            });
        });
    }

    [Test]
    public void DefaultPolicy_DoesNotAdmitPlainAntialiasedGeometryOnGpu()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var ellipse = new EllipseShape
            {
                Width = { CurrentValue = 91 },
                Height = { CurrentValue = 73 },
                Fill = { CurrentValue = Brushes.White },
            };
            using Drawable.Resource resource = ellipse.ToResource(CompositionContext.Default);

            AssertProductionCacheSequenceParity(
                resource,
                RenderCacheOptions.Default,
                expectCacheHit: false);
        });
    }

    [Test]
    public void FractionalDrawableBrush_UncachedRenderPreservesAnalyticRectangleCoverage()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Drawable.Resource resource = CreateDrawableBrushHost(
                translation: 0.25f,
                width: 120,
                height: 120,
                rectangularContent: true);
            using Bitmap bitmap = RenderComposedFrame(resource, useRenderCache: false);

            Rgba leftEdge = ReadPixel(bitmap, 60, 80);
            Rgba topEdge = ReadPixel(bitmap, 120, 20);
            Assert.Multiple(() =>
            {
                Assert.That(
                    leftEdge.Alpha,
                    Is.EqualTo(0.75f).Within(0.01f),
                    "The leading edge must retain 75% device-pixel coverage.");
                Assert.That(
                    topEdge.Alpha,
                    Is.EqualTo(0.75f).Within(0.01f),
                    "The top edge must retain 75% device-pixel coverage.");
            });
        });
    }

    [Test]
    public void FractionalDrawableBrush_HasNoCacheAdmissionFrameFlicker()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Drawable.Resource resource = CreateDrawableBrushHost(
                translation: 0.25f,
                width: 120,
                height: 120);

            AssertProductionCacheSequenceParity(
                resource,
                RenderCacheOptions.Enabled,
                expectCacheHit: false);
        });
    }

    private static Drawable.Resource CreateDrawableBrushHost(
        float translation,
        float width = 40,
        float height = 30,
        bool rectangularContent = false)
    {
        Drawable content = rectangularContent
            ? new RectShape
            {
                Width = { CurrentValue = width },
                Height = { CurrentValue = height },
                Fill = { CurrentValue = Brushes.White },
            }
            : new EllipseShape
            {
                Width = { CurrentValue = width },
                Height = { CurrentValue = height },
                Fill = { CurrentValue = Brushes.White },
            };
        var brush = new DrawableBrush(content)
        {
            Stretch = { CurrentValue = Stretch.Fill },
            TileMode = { CurrentValue = TileMode.None },
            DestinationRect = { CurrentValue = RelativeRect.Fill },
        };
        var host = new RectShape
        {
            Width = { CurrentValue = width },
            Height = { CurrentValue = height },
            Fill = { CurrentValue = brush },
            Transform = { CurrentValue = new TranslateTransform(translation, translation) },
        };
        return host.ToResource(CompositionContext.Default);
    }

    private static void AssertCacheAdmissionParity<TNode>(
        Drawable.Resource resource,
        bool expectCacheHit)
        where TNode : RenderNode
    {
        using var root = new DrawableRenderNode(resource);
        using (var context = new GraphicsContext2D(root, s_frameSize.ToSize(1)))
        {
            context.Clear();
            context.DrawDrawable(resource);
        }

        TNode? cacheable = Descendants(root).OfType<TNode>().FirstOrDefault();
        Assert.That(cacheable, Is.Not.Null, $"the fixture must contain a {typeof(TNode).Name}");
        cacheable!.Cache.ReportRenderCount(RenderNodeCache.Count);

        var diagnostics = new RenderPipelineDiagnosticsState();
        using var cachedRenderer = CreateRenderer(root, useRenderCache: true, diagnostics);
        using RenderNodeRasterization admission = cachedRenderer.Rasterize();
        using RenderNodeRasterization hit = cachedRenderer.Rasterize();
        using var uncachedRenderer = CreateRenderer(root, useRenderCache: false);
        using RenderNodeRasterization control = uncachedRenderer.Rasterize();

        byte[] admissionPixels = GetPixels(admission);
        byte[] hitPixels = GetPixels(hit);
        byte[] controlPixels = GetPixels(control);
        Assert.Multiple(() =>
        {
            Assert.That(
                admissionPixels,
                Is.EqualTo(controlPixels),
                "cache admission must not change output. "
                + DescribeDifference(controlPixels, admissionPixels));
            Assert.That(
                hitPixels,
                Is.EqualTo(controlPixels),
                "cache replay must not change output. "
                + DescribeDifference(controlPixels, hitPixels));
            Assert.That(
                diagnostics.Latest[RenderPipelineCounter.RenderCacheHits],
                expectCacheHit ? Is.GreaterThan(0) : Is.EqualTo(0),
                expectCacheHit
                    ? "the second enabled render must exercise a persistent render-cache hit"
                    : "a transformed device-grid-dependent source must bypass persistent caching");
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
                DefaultRequest = new RenderNodeRenderRequest
                {
                    TargetDomain = s_frameBounds,
                    UseRenderCache = useRenderCache,
                    Purpose = RenderRequestPurpose.Frame,
                    Diagnostics = diagnostics,
                },
                TargetFactory = new CpuTargetFactory(),
            });
    }

    private static void AssertProductionCacheSequenceParity(
        Drawable.Resource resource,
        RenderCacheOptions cacheOptions,
        bool expectCacheHit)
        => AssertProductionCacheSequence(
            resource,
            cacheOptions,
            expectCacheHit,
            assertPixelParity: true);

    private static void AssertProductionCacheSequence(
        Drawable.Resource resource,
        RenderCacheOptions cacheOptions,
        bool expectCacheHit,
        bool assertPixelParity)
    {
        var diagnostics = new RenderPipelineDiagnosticsState();
        var completedRequests = new List<RenderPipelineDiagnosticSnapshot>();
        diagnostics.RequestCompleted += completedRequests.Add;
        using var cachedRenderer = new Renderer(
            s_frameSize.Width,
            s_frameSize.Height,
            renderScale: 1,
            maxWorkingScale: float.PositiveInfinity,
            diagnostics: diagnostics,
            surface: null)
        {
            CacheOptions = cacheOptions,
        };
        using var uncachedRenderer = new Renderer(s_frameSize.Width, s_frameSize.Height)
        {
            CacheOptions = RenderCacheOptions.Disabled,
        };
        var frameData = new CompositionFrame(
            [resource],
            new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
            s_frameSize);
        for (int frame = 0; frame < 6; frame++)
        {
            cachedRenderer.Render(frameData);
            uncachedRenderer.Render(frameData);
            using Bitmap actual = cachedRenderer.Snapshot();
            using Bitmap control = uncachedRenderer.Snapshot();
            byte[] expected = control.GetPixelSpan().ToArray();
            byte[] actualPixels = actual.GetPixelSpan().ToArray();
            if (assertPixelParity)
            {
                Assert.That(
                    actualPixels,
                    Is.EqualTo(expected),
                    $"cache policy changed frame {frame}. {DescribeDifference(expected, actualPixels)}");
            }
        }

        long hits = completedRequests.Sum(
            static snapshot => snapshot[RenderPipelineCounter.RenderCacheHits]);
        Assert.That(
            hits,
            expectCacheHit ? Is.GreaterThan(0) : Is.EqualTo(0),
            expectCacheHit
                ? "The explicitly enabled cached arm must execute a persistent cache hit."
                : "The default-disabled or phase-unsafe arm must not execute a cache hit.");
    }

    private static Bitmap RenderComposedFrame(Drawable.Resource resource, bool useRenderCache)
    {
        using var renderer = new Renderer(s_frameSize.Width, s_frameSize.Height)
        {
            CacheOptions = useRenderCache
                ? RenderCacheOptions.Enabled
                : RenderCacheOptions.Disabled,
        };
        renderer.Render(new CompositionFrame(
            [resource],
            new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
            s_frameSize));
        return renderer.Snapshot();
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

    private static Rgba ReadPixel(Bitmap bitmap, int x, int y)
    {
        ReadOnlySpan<ushort> pixels = bitmap.GetPixelSpan<ushort>();
        int offset = ((y * bitmap.Width) + x) * 4;
        return new Rgba(
            (float)BitConverter.UInt16BitsToHalf(pixels[offset]),
            (float)BitConverter.UInt16BitsToHalf(pixels[offset + 1]),
            (float)BitConverter.UInt16BitsToHalf(pixels[offset + 2]),
            (float)BitConverter.UInt16BitsToHalf(pixels[offset + 3]));
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

    private readonly record struct Rgba(float Red, float Green, float Blue, float Alpha);
}
