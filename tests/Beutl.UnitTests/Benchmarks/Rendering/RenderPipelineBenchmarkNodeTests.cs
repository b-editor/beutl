using Beutl.Benchmarks.Rendering;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;
using Beutl.UnitTests.Engine.Graphics.Rendering;

using SkiaSharp;

namespace Beutl.UnitTests.Benchmarks.Rendering;

public sealed class RenderPipelineBenchmarkNodeTests
{
    [TestCase("LayerCustomEffect")]
    [TestCase("BlurCustomBlur")]
    [TestCase("StaticSpatialPrefixAnimatedBlurTail")]
    [NonParallelizable]
    public void NewScenes_WarmAndVerifyThroughProductionBenchmarkSession(string caseName)
    {
        VulkanTestEnvironment.EnsureAvailable();
        RenderThread.Dispatcher.Invoke(() =>
        {
            using var session = new RenderPipelineBenchmarkSession(caseName);
            session.WarmAndVerify();
            _ = session.RenderMeasuredFrame();
            RenderPipelineBenchmarkCounterRecord record = session.CreateCounterRecord();

            Assert.Multiple(() =>
            {
                Assert.That(record.OutputSha256, Is.Not.Empty);
                Assert.That(record.MeasuredOutputSha256, Is.Not.Empty);
                if (caseName == "StaticSpatialPrefixAnimatedBlurTail")
                {
                    Assert.That(record.MeasuredOutputSha256, Is.Not.EqualTo(record.OutputSha256));
                }
            });
        });
    }

    [Test]
    public void CustomEffectScenes_ShareSpatialSourceAndDeclareExactTopology()
    {
        RenderPipelineBenchmarkSceneDefinition spatialGroup =
            RenderPipelineBenchmarkScenes.Get("SpatialGroupChain");
        RenderPipelineBenchmarkSceneDefinition spatialNodes =
            RenderPipelineBenchmarkScenes.Get("SpatialNodeChain");
        RenderPipelineBenchmarkSceneDefinition custom =
            RenderPipelineBenchmarkScenes.Get("LayerCustomEffect");
        RenderPipelineBenchmarkSceneDefinition mixed =
            RenderPipelineBenchmarkScenes.Get("BlurCustomBlur");
        var customEffect = (FilterEffectGroup)RenderPipelineBenchmarkSession.CreateCustomEffectForTest(mixed: false);
        var mixedEffect = (FilterEffectGroup)RenderPipelineBenchmarkSession.CreateCustomEffectForTest(mixed: true);

        Assert.Multiple(() =>
        {
            Assert.That(
                new[] { spatialGroup.Seed, spatialNodes.Seed, custom.Seed, mixed.Seed }.Distinct().Count(),
                Is.EqualTo(1));
            Assert.That(custom.SemanticStageCount, Is.EqualTo(1));
            Assert.That(mixed.SemanticStageCount, Is.EqualTo(3));
            Assert.That(custom.Barrier, Is.EqualTo(RenderPipelineBenchmarkBarrier.CustomEffect));
            Assert.That(mixed.Barrier, Is.EqualTo(RenderPipelineBenchmarkBarrier.CustomEffect));
            Assert.That(customEffect.Children, Has.Count.EqualTo(1));
            Assert.That(customEffect.Children[0], Is.TypeOf<LayerEffect>());
            Assert.That(mixedEffect.Children.Select(static effect => effect.GetType()), Is.EqualTo(new[]
            {
                typeof(Blur),
                typeof(LayerEffect),
                typeof(Blur),
            }));
            Assert.That(CompileBoundaryReasons(customEffect), Does.Contain(ExecutionIslandBoundaryReason.LegacyCustomEffect));
            Assert.That(CompileBoundaryReasons(mixedEffect), Does.Contain(ExecutionIslandBoundaryReason.LegacyCustomEffect));
        });
    }

    [Test]
    public void StaticSpatialPrefix_AnimatedBlurTailChangesOutputAndKeepsStaticChildClean()
    {
        var source = new RectangleRenderNode(
            new Rect(12, 10, 40, 28),
            Brushes.Resource.White,
            null);
        var prefixEffect = new Blur { Sigma = { CurrentValue = new Size(3, 3) } };
        using FilterEffect.Resource prefixResource = prefixEffect.ToResource(CompositionContext.Default);
        var prefix = new FilterEffectRenderNode(prefixResource);
        prefix.AddChild(source);
        var boundary = new BenchmarkCacheBoundaryNode();
        boundary.AddChild(prefix);
        boundary.Cache.RecordStableRequests();
        var tailEffect = new Blur();
        using FilterEffect.Resource tailResource = tailEffect.ToResource(CompositionContext.Default);
        using var tail = new FilterEffectRenderNode(tailResource);
        tail.AddChild(boundary);
        var animation = new BenchmarkAnimatedBlurNode(tailEffect, tailResource, tail);
        using var renderer = CreateCpuRenderer(tail);

        animation.Apply(new RenderPipelineBenchmarkFrameState(0.75f, StructuralVariant: false));
        using RenderNodeRasterization first = renderer.Rasterize();
        byte[] firstPixels = first.Bitmap?.GetPixelSpan().ToArray()
            ?? throw new InvalidOperationException("The first animated Blur frame produced no pixels.");
        Assert.That(boundary.Cache.IsCached, Is.True);
        using RenderTarget cachedPrefix = boundary.Cache.UseCache(out Rect cachedBounds);
        animation.Apply(new RenderPipelineBenchmarkFrameState(1.25f, StructuralVariant: false));

        Assert.Multiple(() =>
        {
            Assert.That(tail.HasChanges, Is.True);
            Assert.That(boundary.HasChanges, Is.False);
            Assert.That(prefix.HasChanges, Is.False);
            Assert.That(source.HasChanges, Is.False);
        });

        using RenderNodeRasterization second = renderer.Rasterize();
        using RenderTarget retainedPrefix = boundary.Cache.UseCache(out Rect retainedBounds);
        Assert.Multiple(() =>
        {
            Assert.That(second.Bitmap, Is.Not.Null);
            Assert.That(second.Bitmap!.GetPixelSpan().SequenceEqual(firstPixels), Is.False);
            Assert.That(boundary.Cache.IsCached, Is.True);
            Assert.That(retainedPrefix.Value, Is.SameAs(cachedPrefix.Value));
            Assert.That(retainedBounds, Is.EqualTo(cachedBounds));
            Assert.That(tail.HasChanges, Is.False);
            Assert.That(prefix.HasChanges, Is.False);
        });
    }

    [Test]
    public void AnimatedTail_ChangedAmountsInvalidateTailAndPreserveStaticPrefixCache()
    {
        var prefix = new BenchmarkCacheBoundaryNode();
        var tail = new BenchmarkAnimatedShaderNode();
        tail.AddChild(prefix);
        using var root = new BenchmarkShaderNode(BenchmarkShader.ChannelRotate);
        root.AddChild(tail);
        Rect bounds = new(0, 0, 1, 1);

        RenderNodeCache.PublishAtomically(
        [
            RenderCacheTestSupport.CreatePublication(
                prefix.Cache,
                RenderTarget.CreateNull(1, 1),
                bounds,
                name: "static-prefix"),
            RenderCacheTestSupport.CreatePublication(
                tail.Cache,
                RenderTarget.CreateNull(1, 1),
                bounds,
                name: "animated-tail"),
        ]);

        var first = new RenderPipelineBenchmarkFrameState(0.75f, StructuralVariant: false);
        tail.Apply(first);
        RenderNodeCacheLifecycle lifecycle = RenderNodeCacheHelper.BeginLifecycle(root);

        Assert.Multiple(() =>
        {
            Assert.That(tail.Cache.IsCached, Is.False);
            Assert.That(prefix.Cache.IsCached, Is.True);
        });

        lifecycle.CompleteSuccessfully(advanceWarmup: true);
        RenderNodeCache.PublishAtomically(
        [
            RenderCacheTestSupport.CreatePublication(
                tail.Cache,
                RenderTarget.CreateNull(1, 1),
                bounds,
                name: "animated-tail"),
        ]);

        var second = new RenderPipelineBenchmarkFrameState(0.8f, StructuralVariant: false);
        tail.Apply(second);
        lifecycle = RenderNodeCacheHelper.BeginLifecycle(root);

        Assert.Multiple(() =>
        {
            Assert.That(tail.Cache.IsCached, Is.False);
            Assert.That(prefix.Cache.IsCached, Is.True);
        });

        lifecycle.CompleteSuccessfully(advanceWarmup: true);
        tail.Apply(second);

        Assert.That(tail.HasChanges, Is.False);
    }

    private static RenderNodeRenderer CreateCpuRenderer(RenderNode node)
        => new(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    TargetDomain = new Rect(0, 0, 64, 48),
                    OutputScale = 1,
                    MaxWorkingScale = 1,
                    CacheOptions = RenderCacheOptions.Enabled,
                    Purpose = RenderRequestPurpose.Frame,
                },
                TargetFactory = new CpuTargetFactory(),
            });

    private static IEnumerable<ExecutionIslandBoundaryReason> CompileBoundaryReasons(FilterEffect effect)
    {
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var node = new FilterEffectRenderNode(resource);
        node.AddChild(new RectangleRenderNode(
            new Rect(0, 0, 64, 48),
            Brushes.Resource.White,
            null));
        var request = new RenderRequest(new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            targetDomain: new Rect(0, 0, 64, 48),
            cachePolicy: RenderCacheOptions.Disabled));
        try
        {
            RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(node);
            using CompiledRenderRequest compiled = new RenderRequestCompiler().Compile(request, graph);
            return compiled.ExecutionPlan.Boundaries
                .Select(static boundary => boundary.Reason)
                .ToArray();
        }
        catch
        {
            request.Dispose();
            throw;
        }
    }

    private sealed class CpuTargetFactory : IRenderTargetFactory
    {
        public RenderTarget Create(RenderTargetAllocationDescriptor allocation)
            => new CpuRenderTarget(allocation.DeviceSize);
    }

    private sealed class CpuRenderTarget(PixelSize size)
        : RenderTarget(
            SKSurface.Create(new SKImageInfo(
                    size.Width,
                    size.Height,
                    SKColorType.RgbaF16,
                    SKAlphaType.Premul,
                    SKColorSpace.CreateSrgbLinear()))
                ?? throw new InvalidOperationException("Could not create the CPU benchmark-test surface."),
            size.Width,
            size.Height);
}
