using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

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
            Drawable.Resource[] resources = RenderCacheTestSupport.CreateRepresentativeScene();
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
                cacheableBackground!.Cache.RecordStableRequests();

                using var cachedRenderer = CreateRenderer(root, useRenderCache: true);
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
                FontFamily = { Value = FontFamily.Default },
                Size = { Value = 72 },
                Fill = { Value = Brushes.White },
                Text = { Value = "ab" },
                AlignmentX = { Value = AlignmentX.Left },
                AlignmentY = { Value = AlignmentY.Top },
            };
            using Drawable.Resource resource = text.ToResource(CompositionContext.Default);

            AssertCacheAdmissionParity<TextRenderNode>(resource);
        });
    }

    [Test]
    public void FractionalDrawableBrush_DeviceGridDependentCacheCandidateIsBypassed()
    {
        RenderThread.Dispatcher.Invoke(() =>
        {
            using Drawable.Resource resource = CreateDrawableBrushHost(0.5f);

            AssertCacheAdmissionParity<GeometryRenderNode>(resource);
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
                FontFamily = { Value = FontFamily.Default },
                Size = { Value = 72 },
                Fill = { Value = Brushes.White },
                Text = { Value = "ab" },
                Transform = { Value = new TranslateTransform(0, 0) },
            };
            using Drawable.Resource resource = text.ToResource(CompositionContext.Default);

            AssertProductionCacheSequence(resource, RenderCacheOptions.Enabled);
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
                Width = { Value = 160 },
                Height = { Value = 160 },
                Fill = { Value = Brushes.White },
            });
            group.Children.Add(new EllipseShape
            {
                Width = { Value = 90 },
                Height = { Value = 90 },
                Fill = { Value = Brushes.White },
                BlendMode = { Value = blendMode },
            });
            using Drawable.Resource resource = group.ToResource(CompositionContext.Default);

            AssertProductionCacheSequence(resource, RenderCacheOptions.Enabled);
        });
    }

    [Test]
    public void PlainGroup_WithCenteredVectorContentIsConservativelyBypassed()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var group = new DrawableGroup();
            group.Children.Add(new RectShape
            {
                Width = { Value = 160 },
                Height = { Value = 160 },
                Fill = { Value = Brushes.White },
            });
            using Drawable.Resource resource = group.ToResource(CompositionContext.Default);

            AssertProductionCacheSequence(resource, RenderCacheOptions.Enabled);
        });
    }

    [Test]
    public void PlainGroup_DefaultRenderRequestDoesNotUsePersistentCache()
    {
        RenderThread.Dispatcher.Invoke(() =>
        {
            var group = new DrawableGroup();
            group.Children.Add(new RectShape
            {
                Width = { Value = 160 },
                Height = { Value = 160 },
                Fill = { Value = Brushes.White },
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
            cacheable!.Cache.RecordStableRequests();

            using var renderer = new RenderNodeRenderer(root, new RenderNodeRenderRequest
            {
                Intent = RenderIntent.Preview,
                TargetDomain = s_frameBounds,
                Purpose = RenderRequestPurpose.Frame,
            }, new CpuRenderTargetFactory());
            using RenderNodeRasterization first = renderer.Rasterize();
            using RenderNodeRasterization second = renderer.Rasterize();

            Assert.Multiple(() =>
            {
                Assert.That(cacheable.Cache.IsCached, Is.False,
                    "RenderCacheOptions.Default must not admit a plain group into the persistent cache.");
                Assert.That(GetPixels(second), Is.EqualTo(GetPixels(first)),
                    "Two frames of an unchanged group must agree whether or not a cache served them.");
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
                Width = { Value = 91 },
                Height = { Value = 73 },
                Fill = { Value = Brushes.White },
            };
            using Drawable.Resource resource = ellipse.ToResource(CompositionContext.Default);

            AssertProductionCacheSequence(resource, RenderCacheOptions.Default);
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

            AssertProductionCacheSequence(resource, RenderCacheOptions.Enabled);
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
                Width = { Value = width },
                Height = { Value = height },
                Fill = { Value = Brushes.White },
            }
            : new EllipseShape
            {
                Width = { Value = width },
                Height = { Value = height },
                Fill = { Value = Brushes.White },
            };
        var brush = new DrawableBrush(content)
        {
            Stretch = { Value = Stretch.Fill },
            TileMode = { Value = TileMode.None },
            DestinationRect = { Value = RelativeRect.Fill },
        };
        var host = new RectShape
        {
            Width = { Value = width },
            Height = { Value = height },
            Fill = { Value = brush },
            Transform = { Value = new TranslateTransform(translation, translation) },
        };
        return host.ToResource(CompositionContext.Default);
    }

    private static void AssertCacheAdmissionParity<TNode>(Drawable.Resource resource)
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
        cacheable!.Cache.RecordStableRequests();

        using var cachedRenderer = CreateRenderer(root, useRenderCache: true);
        using RenderNodeRasterization admission = cachedRenderer.Rasterize();
        using RenderNodeRasterization hit = cachedRenderer.Rasterize();
        using var uncachedRenderer = CreateRenderer(root, useRenderCache: false);
        using RenderNodeRasterization control = uncachedRenderer.Rasterize();

        byte[] admissionPixels = GetPixels(admission);
        byte[] hitPixels = GetPixels(hit);
        byte[] controlPixels = GetPixels(control);
        Assert.Multiple(() =>
        {
            Assert.That(cacheable.Cache.IsCached, Is.False, "the bypassed candidate must not be admitted");
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
        });
    }

    private static RenderNodeRenderer CreateRenderer(
        RenderNode root,
        bool useRenderCache)
    {
        return new RenderNodeRenderer(root, new RenderNodeRenderRequest
        {
            Intent = RenderIntent.Preview,
            TargetDomain = s_frameBounds,
            CacheOptions = new Beutl.Graphics.Rendering.Cache.RenderCacheOptions(useRenderCache, Beutl.Graphics.Rendering.Cache.RenderCacheRules.Default),
            Purpose = RenderRequestPurpose.Frame,
        }, new CpuRenderTargetFactory());
    }

    private static void AssertProductionCacheSequence(
        Drawable.Resource resource,
        RenderCacheOptions cacheOptions)
    {
        using var cachedRenderer = new Renderer(s_frameSize.Width, s_frameSize.Height, RenderIntent.Preview)
        {
            CacheOptions = cacheOptions,
        };
        using var uncachedRenderer = new Renderer(s_frameSize.Width, s_frameSize.Height, RenderIntent.Preview)
        {
            CacheOptions = RenderCacheOptions.Disabled,
        };
        var frameData = new CompositionFrame(
            [resource],
            new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
            s_frameSize,
            null);
        for (int frame = 0; frame < 6; frame++)
        {
            cachedRenderer.Render(frameData);
            uncachedRenderer.Render(frameData);
            using Bitmap actual = cachedRenderer.Snapshot();
            using Bitmap control = uncachedRenderer.Snapshot();
            byte[] expected = control.GetPixelSpan().ToArray();
            byte[] actualPixels = actual.GetPixelSpan().ToArray();
            Assert.That(
                HasFiniteVisibleContent(control),
                Is.True,
                $"cache parity control frame {frame} must contain finite visible content.");
            Assert.That(
                actualPixels,
                Is.EqualTo(expected),
                $"cache policy changed frame {frame}. {DescribeDifference(expected, actualPixels)}");
        }

    }

    private static bool HasFiniteVisibleContent(Bitmap bitmap)
    {
        ReadOnlySpan<ushort> pixels = bitmap.GetPixelSpan<ushort>();
        for (int i = 3; i < pixels.Length; i += 4)
        {
            float a = (float)BitConverter.UInt16BitsToHalf(pixels[i]);
            if (float.IsFinite(a) && a > 0f)
            {
                return true;
            }
        }
        return false;
    }

    private static Bitmap RenderComposedFrame(Drawable.Resource resource, bool useRenderCache)
    {
        using var renderer = new Renderer(s_frameSize.Width, s_frameSize.Height, RenderIntent.Preview)
        {
            CacheOptions = useRenderCache
                ? RenderCacheOptions.Enabled
                : RenderCacheOptions.Disabled,
        };
        renderer.Render(new CompositionFrame(
            [resource],
            new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
            s_frameSize,
            null));
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

    private readonly record struct Rgba(float Red, float Green, float Blue, float Alpha);
}
