using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.Media.Source;
using Beutl.Serialization;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

public sealed class DirectSkiaFilterReplayTests
{
    private static readonly Rect s_sourceBounds = new(24, 20, 40, 32);
    private static readonly float[] s_blurSigmas = [1, 2, 3];
    private static readonly PixelSize s_deepPatternSize = new(384, 216);
    private static readonly Rect s_deepTargetDomain = new(default, s_deepPatternSize.ToSize(1));
    private const int DeepAlternatingPairCount = 4;
    private const float DeepAlternatingBlurSigma = 3;

    [Test]
    public void PureBuiltInBlurGroup_ReplaysDirectlyWithoutIntermediateTargets()
    {
        using FilterEffectRenderNode node = CreateFilterNode(CreateBlurGroup());
        using RenderNodeRenderer renderer = CreateRenderer(node);

        using RenderNodeRasterization result = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsEmpty, Is.False);
            Assert.That(result.Bitmap!.GetPixelSpan().ToArray(), Has.Some.Not.Zero);
            Assert.That(
                renderer.LastExecutionStatistics.IntermediateTargetAcquisitions,
                Is.Zero,
                "A root pure built-in Skia chain should replay its vector input directly into the destination.");
        });
    }

    [Test]
    public void SeparateBuiltInBlurNodes_ReplayWithoutSynchronousCanvasFlushes()
    {
        using FilterEffectRenderNode node = CreateSerialBlurNodes();
        using RenderNodeRenderer renderer = CreateRenderer(node);
        Rect bounds = GetBlurChainBounds();
        PixelRect deviceBounds = PixelRect.FromRect(bounds, 1);
        using RenderTarget target = new CpuRenderTarget(deviceBounds.Size);
        using var canvas = new ImmediateCanvas(
            target,
            density: 1,
            maxWorkingScale: 4,
            logicalSize: deviceBounds.ToRect(1).Size);
        var flushes = new List<ImmediateCanvasFlushKind>();

        using (ImmediateCanvas.ObserveFlushes(flushes.Add))
        using (canvas.PushTransform(Matrix.CreateTranslation(-bounds.X, -bounds.Y)))
            renderer.Render(canvas);

        Assert.That(flushes, Is.Empty,
            "A direct Blur chain must not submit or synchronously flush an intermediate canvas.");
    }

    [Test]
    public void PureBuiltInBlurGroup_MatchesNestedSkiaImageFilterReferenceExactly()
    {
        using FilterEffectRenderNode node = CreateFilterNode(CreateBlurGroup());
        using RenderNodeRenderer renderer = CreateRenderer(node);
        using RenderNodeRasterization actual = renderer.Rasterize();
        Rect expectedBounds = GetBlurChainBounds();
        using Bitmap expected = RenderNestedSkiaReference(expectedBounds);

        Assert.Multiple(() =>
        {
            Assert.That(actual.Bounds, Is.EqualTo(expectedBounds));
            Assert.That(actual.Bitmap, Is.Not.Null);
            Assert.That(actual.Bitmap!.ColorType, Is.EqualTo(BitmapColorType.RgbaF16));
            Assert.That(actual.Bitmap.Width, Is.EqualTo(expected.Width));
            Assert.That(actual.Bitmap.Height, Is.EqualTo(expected.Height));
            Assert.That(
                actual.Bitmap.GetPixelSpan().SequenceEqual(expected.GetPixelSpan()),
                Is.True,
                "Direct replay must be byte-identical to drawing the same source under the nested Skia filter chain.");
        });
    }

    [Test]
    public void SeparateBuiltInBlurNodes_ReplayDirectlyWithExactNestedSkiaPixels()
    {
        using FilterEffectRenderNode node = CreateSerialBlurNodes();
        using RenderNodeRenderer renderer = CreateRenderer(node);

        using RenderNodeRasterization actual = renderer.Rasterize();
        Rect expectedBounds = GetBlurChainBounds();
        using Bitmap expected = RenderNestedSkiaReference(expectedBounds);

        Assert.Multiple(() =>
        {
            Assert.That(actual.Bounds, Is.EqualTo(expectedBounds));
            Assert.That(actual.Bitmap, Is.Not.Null);
            Assert.That(actual.Bitmap!.ColorType, Is.EqualTo(BitmapColorType.RgbaF16));
            Assert.That(actual.Bitmap.Width, Is.EqualTo(expected.Width));
            Assert.That(actual.Bitmap.Height, Is.EqualTo(expected.Height));
            Assert.That(
                actual.Bitmap.GetPixelSpan().SequenceEqual(expected.GetPixelSpan()),
                Is.True,
                "Separate Blur nodes must preserve the same nested Skia image-filter pixels.");
            Assert.That(
                renderer.LastExecutionStatistics.IntermediateTargetAcquisitions,
                Is.Zero,
                "Serial pure built-in Skia nodes should recursively replay into the root destination.");
        });
    }

    [Test]
    public void ConcreteImageSource_BuiltInBlurGroup_ReplaysDirectlyWithExactNestedSkiaPixels()
    {
        using ImageSource.Resource source = CreateImageSourceResource();
        Rect sourceBounds = new(default, source.FrameSize.ToSize(1));
        using var node = new FilterEffectRenderNode(
            CreateBlurGroup().ToResource(CompositionContext.Default));
        node.AddChild(new ImageSourceRenderNode(source, Brushes.Resource.White, null));
        using RenderNodeRenderer renderer = CreateRenderer(node);

        using RenderNodeRasterization actual = renderer.Rasterize();
        Rect expectedBounds = GetBlurChainBounds(sourceBounds);
        using Bitmap expected = RenderNestedSkiaImageReference(
            source,
            expectedBounds);

        Assert.Multiple(() =>
        {
            Assert.That(renderer.Measure().EffectiveScale, Is.EqualTo(EffectiveScale.At(1)));
            Assert.That(actual.Bounds, Is.EqualTo(expectedBounds));
            Assert.That(actual.Bitmap, Is.Not.Null);
            Assert.That(actual.Bitmap!.ColorType, Is.EqualTo(BitmapColorType.RgbaF16));
            Assert.That(actual.Bitmap.GetPixelSpan().ToArray(), Has.Some.Not.Zero);
            Assert.That(
                actual.Bitmap.GetPixelSpan().SequenceEqual(expected.GetPixelSpan()),
                Is.True,
                "A native-density image source under Blur must match the same nested Skia image-filter draw.");
            Assert.That(
                renderer.LastExecutionStatistics.IntermediateTargetAcquisitions,
                Is.Zero,
                "A native-density image source should replay through the pure built-in Blur chain into the destination.");
        });
    }

    [Test]
    public void ConcreteImageSource_DestinationDensityMismatch_RemainsMaterialized()
    {
        const float sourceDensity = 1;
        const float destinationDensity = 2;
        using ImageSource.Resource source = CreateImageSourceResource();
        using var image = new ImageSourceRenderNode(source, Brushes.Resource.White, null);
        using RenderNodeRenderer sourceRenderer = CreateRenderer(image);
        Assert.That(sourceRenderer.Measure().EffectiveScale, Is.EqualTo(EffectiveScale.At(sourceDensity)));

        using var node = new FilterEffectRenderNode(
            CreateBlurGroup().ToResource(CompositionContext.Default));
        node.AddChild(new ImageSourceRenderNode(source, Brushes.Resource.White, null));
        using RenderNodeRenderer renderer = CreateRenderer(
            node,
            outputDensity: destinationDensity,
            maxWorkingDensity: destinationDensity);

        RenderNodeMeasurement measurement = renderer.Measure();
        using RenderNodeRasterization result = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsEmpty, Is.False);
            Assert.That(measurement.EffectiveScale, Is.EqualTo(EffectiveScale.At(destinationDensity)));
            Assert.That(
                renderer.LastExecutionStatistics.IntermediateTargetAcquisitions,
                Is.GreaterThan(0),
                "A native-density image cannot replay directly when the destination uses a different density.");
        });
    }

    [Test]
    public void PureBuiltInBlurGroup_PartialRequestedRegion_ReplaysDirectlyWithExactPixels()
    {
        Rect completeBounds = GetBlurChainBounds();
        var requestedRegion = new Rect(18, 16, 28, 24);
        using FilterEffectRenderNode node = CreateFilterNode(CreateBlurGroup());
        using RenderNodeRenderer renderer = CreateRenderer(
            node,
            requestedRegion: requestedRegion);

        using RenderNodeRasterization actual = renderer.Rasterize();
        using Bitmap completeReference = RenderNestedSkiaReference(completeBounds);
        using Bitmap expected = ExtractGlobalRegion(
            completeReference,
            completeBounds,
            requestedRegion);

        Assert.Multiple(() =>
        {
            Assert.That(actual.Bounds, Is.EqualTo(requestedRegion));
            Assert.That(actual.Bitmap, Is.Not.Null);
            Assert.That(actual.Bitmap!.ColorType, Is.EqualTo(BitmapColorType.RgbaF16));
            Assert.That(actual.Bitmap.GetPixelSpan().ToArray(), Has.Some.Not.Zero);
            Assert.That(
                actual.Bitmap.GetPixelSpan().SequenceEqual(expected.GetPixelSpan()),
                Is.True,
                "A clipped root request must match the same region of the complete nested Skia render.");
            Assert.That(
                renderer.LastExecutionStatistics.IntermediateTargetAcquisitions,
                Is.Zero,
                "A partial root request must not force a pure built-in Skia segment to materialize.");
        });
    }

    [Test]
    public void SelectedStaticPrefixCache_IsReusedBeforeDynamicBlurTail()
    {
        var prefix = new CacheableEllipseSourceNode();
        prefix.Cache.RecordStableRequests();
        Blur blur = CreateBlur(1);
        FilterEffect.Resource resource = blur.ToResource(CompositionContext.Default);
        using var tail = new FilterEffectRenderNode(resource);
        tail.AddChild(prefix);
        using RenderNodeRenderer renderer = CreateRenderer(
            tail,
            cacheOptions: RenderCacheOptions.Enabled);

        using RenderNodeRasterization cold = renderer.Rasterize();
        blur.Sigma.CurrentValue = new Size(4, 4);
        bool updateOnly = false;
        resource.Update(blur, CompositionContext.Default, ref updateOnly);
        Assert.That(tail.Update(resource), Is.True);
        using RenderNodeRasterization warm = renderer.Rasterize();

        var referencePrefix = new CacheableEllipseSourceNode();
        using var referenceTail = new FilterEffectRenderNode(
            CreateBlur(4).ToResource(CompositionContext.Default));
        referenceTail.AddChild(referencePrefix);
        using RenderNodeRenderer referenceRenderer = CreateRenderer(referenceTail);
        using RenderNodeRasterization expected = referenceRenderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(prefix.Cache.IsCached, Is.True);
            Assert.That(prefix.ExecuteCount, Is.EqualTo(1),
                "Changing the Blur tail must reuse the selected static-prefix cache entry.");
            Assert.That(cold.Bitmap, Is.Not.Null);
            Assert.That(warm.Bounds, Is.EqualTo(expected.Bounds));
            Assert.That(warm.Bitmap, Is.Not.Null);
            Assert.That(expected.Bitmap, Is.Not.Null);
            Assert.That(
                warm.Bitmap!.GetPixelSpan().SequenceEqual(expected.Bitmap!.GetPixelSpan()),
                Is.True,
                "A cached prefix followed by a changed Blur tail must match an uncached render of that tail.");
            Assert.That(
                warm.Bitmap.GetPixelSpan().SequenceEqual(cold.Bitmap!.GetPixelSpan()),
                Is.False,
                "The changed Blur sigma must make the cache-reuse fixture observably dynamic.");
        });
    }

    [Test]
    public void CachedBuiltInBlurPrefix_AnimatedBlurTail_MatchesUncachedFramesExactly()
    {
        var source = new CacheableEllipseSourceNode();
        using var prefix = new FilterEffectRenderNode(
            CreateBlur(3).ToResource(CompositionContext.Default));
        prefix.AddChild(source);
        prefix.SettleConstruction();
        prefix.Cache.RecordStableRequests();

        Blur tailEffect = CreateBlur(1);
        FilterEffect.Resource tailResource = tailEffect.ToResource(CompositionContext.Default);
        using var tail = new FilterEffectRenderNode(tailResource);
        tail.AddChild(prefix);
        using RenderNodeRenderer renderer = CreateRenderer(
            tail,
            cacheOptions: RenderCacheOptions.Enabled);

        using RenderNodeRasterization cold = renderer.Rasterize();
        using RenderNodeRasterization coldReference = RasterizeMaterializedBlurPrefixTail(
            prefixSigma: 3,
            tailSigma: 1);

        tailEffect.Sigma.CurrentValue = new Size(4, 4);
        bool updateOnly = false;
        tailResource.Update(tailEffect, CompositionContext.Default, ref updateOnly);
        Assert.That(tail.Update(tailResource), Is.True);

        using RenderNodeRasterization warm = renderer.Rasterize();
        using RenderNodeRasterization warmReference = RasterizeMaterializedBlurPrefixTail(
            prefixSigma: 3,
            tailSigma: 4);

        Assert.Multiple(() =>
        {
            Assert.That(prefix.Cache.IsCached, Is.True);
            Assert.That(source.ExecuteCount, Is.EqualTo(1),
                "The warmed static Blur prefix must be replayed from its selected cache entry.");
            Assert.That(cold.Bounds, Is.EqualTo(coldReference.Bounds));
            Assert.That(warm.Bounds, Is.EqualTo(warmReference.Bounds));
            Assert.That(cold.Bitmap, Is.Not.Null);
            Assert.That(warm.Bitmap, Is.Not.Null);
            Assert.That(coldReference.Bitmap, Is.Not.Null);
            Assert.That(warmReference.Bitmap, Is.Not.Null);
            Assert.That(
                cold.Bitmap!.GetPixelSpan().SequenceEqual(coldReference.Bitmap!.GetPixelSpan()),
                Is.True,
                "The direct cache-capture miss must match an uncached render byte for byte.");
            Assert.That(
                warm.Bitmap!.GetPixelSpan().SequenceEqual(warmReference.Bitmap!.GetPixelSpan()),
                Is.True,
                "The cached Blur prefix followed by the changed tail must match an uncached render.");
            Assert.That(
                warm.Bitmap.GetPixelSpan().SequenceEqual(cold.Bitmap.GetPixelSpan()),
                Is.False,
                "Changing the outer Blur must make the two cached frames visibly different.");
        });
    }

    [Test]
    public void BlurCustomBlurGroup_MaterializesAndInvokesCustomCallbackOnce()
    {
        int callbackCount = 0;
        var group = new FilterEffectGroup();
        group.Children.Add(CreateBlur(1));
        group.Children.Add(new CallbackCustomEffect(() => callbackCount++));
        group.Children.Add(CreateBlur(2));
        using FilterEffectRenderNode node = CreateFilterNode(group);
        using RenderNodeRenderer renderer = CreateRenderer(node);

        using RenderNodeRasterization result = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsEmpty, Is.False);
            Assert.That(callbackCount, Is.EqualTo(1));
            Assert.That(
                renderer.LastExecutionStatistics.IntermediateTargetAcquisitions,
                Is.GreaterThan(0),
                "A segment containing CustomEffect must retain its materialized compatibility boundary.");
        });
    }

    [Test]
    public void BuiltInBlur_DynamicCustomInputProducingNoValues_CompletesWithoutDoubleUse()
    {
        int callbackCount = 0;
        using FilterEffectRenderNode node = CreateDynamicCustomBlurChain(
            outputCount: 0,
            CreateBlur(2),
            () => callbackCount++);
        using RenderNodeRenderer renderer = CreateRenderer(node);

        RenderNodeMeasurement measurement = renderer.Measure();
        using RenderNodeRasterization result = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(measurement.ValueCardinality.Maximum, Is.Null,
                "The outer Blur must probe the dynamically declared CustomEffect at execution time.");
            Assert.That(result.Bitmap, Is.Not.Null);
            Assert.That(result.Bitmap!.GetPixelSpan().ToArray(), Has.All.Zero,
                "A runtime-empty value sequence must contribute no pixels to the premeasured output bounds.");
            Assert.That(callbackCount, Is.EqualTo(1));
            Assert.That(renderer.TargetPoolStatistics.LeasedTargets, Is.Zero,
                "Completing the empty dynamic input must not leave a second fragment use outstanding.");
        });
    }

    [Test]
    public void BuiltInBlur_DynamicCustomInputProducingOneValue_MatchesMaterializedReferenceExactly()
    {
        int callbackCount = 0;
        int referenceCallbackCount = 0;
        using FilterEffectRenderNode actualNode = CreateDynamicCustomBlurChain(
            outputCount: 1,
            CreateBlur(2),
            () => callbackCount++);
        using FilterEffectRenderNode referenceNode = CreateDynamicCustomBlurChain(
            outputCount: 1,
            new PublicSkiaBlurEffect(2),
            () => referenceCallbackCount++);
        using RenderNodeRenderer actualRenderer = CreateRenderer(actualNode);
        using RenderNodeRenderer referenceRenderer = CreateRenderer(referenceNode);

        RenderNodeMeasurement measurement = actualRenderer.Measure();
        using RenderNodeRasterization actual = actualRenderer.Rasterize();
        using RenderNodeRasterization expected = referenceRenderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(measurement.ValueCardinality.Maximum, Is.Null,
                "The outer Blur must probe the dynamically declared CustomEffect at execution time.");
            Assert.That(callbackCount, Is.EqualTo(1));
            Assert.That(referenceCallbackCount, Is.EqualTo(1));
            Assert.That(actual.Bounds, Is.EqualTo(expected.Bounds));
            Assert.That(actual.Bitmap, Is.Not.Null);
            Assert.That(expected.Bitmap, Is.Not.Null);
            Assert.That(actual.Bitmap!.ColorType, Is.EqualTo(BitmapColorType.RgbaF16));
            Assert.That(actual.Bitmap.Width, Is.EqualTo(expected.Bitmap!.Width));
            Assert.That(actual.Bitmap.Height, Is.EqualTo(expected.Bitmap.Height));
            Assert.That(
                actual.Bitmap.GetPixelSpan().SequenceEqual(expected.Bitmap.GetPixelSpan()),
                Is.True,
                "A runtime-single CustomEffect result must match the materialized public-Skia semantics byte for byte.");
            Assert.That(actualRenderer.TargetPoolStatistics.LeasedTargets, Is.Zero);
        });
    }

    [Test]
    public void BuiltInBlur_DynamicCustomInputProducingTwoValues_StaysMaterializedAndMatchesReferenceExactly()
    {
        int callbackCount = 0;
        int referenceCallbackCount = 0;
        using FilterEffectRenderNode actualNode = CreateDynamicCustomBlurChain(
            outputCount: 2,
            CreateBlur(2),
            () => callbackCount++);
        using FilterEffectRenderNode referenceNode = CreateDynamicCustomBlurChain(
            outputCount: 2,
            new PublicSkiaBlurEffect(2),
            () => referenceCallbackCount++);
        using RenderNodeRenderer actualRenderer = CreateRenderer(actualNode);
        using RenderNodeRenderer referenceRenderer = CreateRenderer(referenceNode);

        RenderNodeMeasurement measurement = actualRenderer.Measure();
        using RenderNodeRasterization actual = actualRenderer.Rasterize();
        using RenderNodeRasterization expected = referenceRenderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(measurement.ValueCardinality.Maximum, Is.Null,
                "The outer Blur must probe the dynamically declared CustomEffect at execution time.");
            Assert.That(callbackCount, Is.EqualTo(1));
            Assert.That(referenceCallbackCount, Is.EqualTo(1));
            Assert.That(actual.Bounds, Is.EqualTo(expected.Bounds));
            Assert.That(actual.Bitmap, Is.Not.Null);
            Assert.That(expected.Bitmap, Is.Not.Null);
            Assert.That(actual.Bitmap!.ColorType, Is.EqualTo(BitmapColorType.RgbaF16));
            Assert.That(actual.Bitmap.Width, Is.EqualTo(expected.Bitmap!.Width));
            Assert.That(actual.Bitmap.Height, Is.EqualTo(expected.Bitmap.Height));
            Assert.That(
                actual.Bitmap.GetPixelSpan().SequenceEqual(expected.Bitmap.GetPixelSpan()),
                Is.True,
                "A runtime-multiple CustomEffect result must retain per-value materialized Blur semantics.");
            Assert.That(
                actualRenderer.LastExecutionStatistics.IntermediateTargetAcquisitions,
                Is.EqualTo(referenceRenderer.LastExecutionStatistics.IntermediateTargetAcquisitions),
                "Runtime-multiple values must keep the same materialization boundary as the public-Skia fallback.");
            Assert.That(
                actualRenderer.LastExecutionStatistics.IntermediateTargetAcquisitions,
                Is.GreaterThan(0),
                "Runtime-multiple values must not collapse into one direct destination replay.");
            Assert.That(actualRenderer.TargetPoolStatistics.LeasedTargets, Is.Zero);
        });
    }

    [Test]
    public void PublicSkiaFactory_RemainsOnTheMaterializedCompatibilityPath()
    {
        using FilterEffectRenderNode node = CreateFilterNode(new PublicSkiaBlurEffect());
        using RenderNodeRenderer renderer = CreateRenderer(node);

        using RenderNodeRasterization result = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsEmpty, Is.False);
            Assert.That(
                renderer.LastExecutionStatistics.IntermediateTargetAcquisitions,
                Is.GreaterThan(0),
                "A public Skia factory can observe its activator and must not be assumed pure.");
        });
    }

    [Test]
    public void PureBuiltInBlurGroup_WithDifferentWorkingDensity_RemainsMaterialized()
    {
        const float outputDensity = 1;
        const float workingDensity = 2;
        using var node = new FixedWorkingScaleFilterRenderNode(
            CreateBlurGroup().ToResource(CompositionContext.Default),
            workingDensity);
        node.AddChild(CreateSource());
        using RenderNodeRenderer renderer = CreateRenderer(
            node,
            outputDensity,
            maxWorkingDensity: workingDensity);

        RenderNodeMeasurement measurement = renderer.Measure();
        using RenderNodeRasterization result = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsEmpty, Is.False);
            Assert.That(measurement.EffectiveScale, Is.EqualTo(EffectiveScale.At(workingDensity)));
            Assert.That(result.OutputScale, Is.EqualTo(outputDensity));
            Assert.That(
                renderer.LastExecutionStatistics.IntermediateTargetAcquisitions,
                Is.GreaterThan(0),
                "Direct replay cannot substitute a segment whose working density differs from the destination.");
        });
    }

    [Test]
    public void BuiltInBlur_MultipleLeafValues_MatchesMaterializedPublicSkiaFallbackExactly()
    {
        using var actualNode = new FilterEffectRenderNode(
            CreateBlur(2).ToResource(CompositionContext.Default));
        actualNode.AddChild(new TwoValueExpansionNode());
        using RenderNodeRenderer actualRenderer = CreateRenderer(actualNode);

        using var referenceNode = new FilterEffectRenderNode(
            new PublicSkiaBlurEffect().ToResource(CompositionContext.Default));
        referenceNode.AddChild(new TwoValueExpansionNode());
        using RenderNodeRenderer referenceRenderer = CreateRenderer(referenceNode);

        using RenderNodeRasterization actual = actualRenderer.Rasterize();
        using RenderNodeRasterization expected = referenceRenderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(actual.Bounds, Is.EqualTo(expected.Bounds));
            Assert.That(actual.Bitmap, Is.Not.Null);
            Assert.That(expected.Bitmap, Is.Not.Null);
            Assert.That(actual.Bitmap!.ColorType, Is.EqualTo(BitmapColorType.RgbaF16));
            Assert.That(actual.Bitmap.Width, Is.EqualTo(expected.Bitmap!.Width));
            Assert.That(actual.Bitmap.Height, Is.EqualTo(expected.Bitmap.Height));
            Assert.That(
                actual.Bitmap.GetPixelSpan().SequenceEqual(expected.Bitmap.GetPixelSpan()),
                Is.True,
                "Blur over a multi-value leaf must preserve the materialized per-value filter semantics.");
            Assert.That(
                actualRenderer.LastExecutionStatistics.IntermediateTargetAcquisitions,
                Is.GreaterThan(0),
                "A leaf that can yield multiple values cannot be replayed directly through one Blur paint.");
            Assert.That(
                actualRenderer.LastExecutionStatistics.IntermediateTargetAcquisitions,
                Is.EqualTo(referenceRenderer.LastExecutionStatistics.IntermediateTargetAcquisitions),
                "Built-in Blur must retain the same materialization boundary as the public Skia fallback.");
        });
    }

    [Test]
    public void DeepAlternatingBuiltInBlurAndCombiningCopy_MatchesExplicitlySynchronizedControlExactly()
    {
        int actualCopyCount = 0;
        int referenceCopyCount = 0;
        using ImageSource.Resource source = CreatePatternImageSourceResource();
        using FilterEffectRenderNode actualNode = CreateDeepAlternatingBlurCopyChain(
            source,
            synchronizeSource: false,
            () => actualCopyCount++);
        using FilterEffectRenderNode referenceNode = CreateDeepAlternatingBlurCopyChain(
            source,
            synchronizeSource: true,
            () => referenceCopyCount++);
        using RenderNodeRenderer actualRenderer = CreateRenderer(
            actualNode,
            maxWorkingDensity: 1,
            requestedRegion: s_deepTargetDomain,
            targetDomain: s_deepTargetDomain);
        using RenderNodeRenderer referenceRenderer = CreateRenderer(
            referenceNode,
            maxWorkingDensity: 1,
            requestedRegion: s_deepTargetDomain,
            targetDomain: s_deepTargetDomain);

        var actualFlushes = new List<ImmediateCanvasFlushKind>();
        var referenceFlushes = new List<ImmediateCanvasFlushKind>();
        using RenderNodeRasterization actual = RasterizeWithObservedFlushes(actualRenderer, actualFlushes);
        using RenderNodeRasterization expected = RasterizeWithObservedFlushes(referenceRenderer, referenceFlushes);

        AssertMatchingRgbaF16Rasterization(
            actual,
            expected,
            "Removing synchronous source waits from a deep known-bounds custom chain must not change its pixels.");
        Assert.Multiple(() =>
        {
            Assert.That(actualCopyCount, Is.EqualTo(DeepAlternatingPairCount));
            Assert.That(referenceCopyCount, Is.EqualTo(DeepAlternatingPairCount));
            Assert.That(actual.Bitmap!.GetPixelSpan().ToArray(), Has.Some.Not.Zero);
            Assert.That(
                referenceFlushes.Count(static item => item == ImmediateCanvasFlushKind.PrepareForSampling),
                Is.EqualTo(
                    actualFlushes.Count(static item => item == ImmediateCanvasFlushKind.PrepareForSampling)
                    + DeepAlternatingPairCount),
                "The control must synchronously sample once at every custom-copy boundary.");
            Assert.That(
                actualRenderer.LastExecutionStatistics.IntermediateTargetAcquisitions,
                Is.GreaterThan(0),
                "Each custom-copy boundary must remain materialized.");
            Assert.That(
                actualRenderer.LastExecutionStatistics.IntermediateTargetAcquisitions,
                Is.EqualTo(referenceRenderer.LastExecutionStatistics.IntermediateTargetAcquisitions));
            Assert.That(actualRenderer.TargetPoolStatistics.LeasedTargets, Is.Zero);
            Assert.That(referenceRenderer.TargetPoolStatistics.LeasedTargets, Is.Zero);
        });
    }

    [Test]
    public void DeepAlternatingBuiltInBlurAndCombiningCopy_PartialRequestedRegionMatchesExplicitlySynchronizedControlExactly()
    {
        var requestedRegion = new Rect(53, 31, 191, 113);
        int actualCopyCount = 0;
        int referenceCopyCount = 0;
        using ImageSource.Resource source = CreatePatternImageSourceResource();
        using FilterEffectRenderNode actualNode = CreateDeepAlternatingBlurCopyChain(
            source,
            synchronizeSource: false,
            () => actualCopyCount++);
        using FilterEffectRenderNode referenceNode = CreateDeepAlternatingBlurCopyChain(
            source,
            synchronizeSource: true,
            () => referenceCopyCount++);
        using RenderNodeRenderer actualRenderer = CreateRenderer(
            actualNode,
            maxWorkingDensity: 1,
            requestedRegion: requestedRegion,
            targetDomain: s_deepTargetDomain);
        using RenderNodeRenderer referenceRenderer = CreateRenderer(
            referenceNode,
            maxWorkingDensity: 1,
            requestedRegion: requestedRegion,
            targetDomain: s_deepTargetDomain);

        var actualFlushes = new List<ImmediateCanvasFlushKind>();
        var referenceFlushes = new List<ImmediateCanvasFlushKind>();
        using RenderNodeRasterization actual = RasterizeWithObservedFlushes(actualRenderer, actualFlushes);
        using RenderNodeRasterization expected = RasterizeWithObservedFlushes(referenceRenderer, referenceFlushes);

        AssertMatchingRgbaF16Rasterization(
            actual,
            expected,
            "Removing synchronous source waits from a partial deep known-bounds custom request must not change its pixels.");
        Assert.Multiple(() =>
        {
            Assert.That(actual.Bounds, Is.EqualTo(requestedRegion));
            Assert.That(actualCopyCount, Is.EqualTo(DeepAlternatingPairCount));
            Assert.That(referenceCopyCount, Is.EqualTo(DeepAlternatingPairCount));
            Assert.That(actual.Bitmap!.GetPixelSpan().ToArray(), Has.Some.Not.Zero);
            Assert.That(
                referenceFlushes.Count(static item => item == ImmediateCanvasFlushKind.PrepareForSampling),
                Is.EqualTo(
                    actualFlushes.Count(static item => item == ImmediateCanvasFlushKind.PrepareForSampling)
                    + DeepAlternatingPairCount),
                "The cropped control must synchronously sample once at every custom-copy boundary.");
            Assert.That(
                actualRenderer.LastExecutionStatistics.IntermediateTargetAcquisitions,
                Is.EqualTo(referenceRenderer.LastExecutionStatistics.IntermediateTargetAcquisitions));
            Assert.That(actualRenderer.TargetPoolStatistics.LeasedTargets, Is.Zero);
            Assert.That(referenceRenderer.TargetPoolStatistics.LeasedTargets, Is.Zero);
        });
    }

    private static FilterEffectRenderNode CreateFilterNode(FilterEffect effect)
    {
        var node = new FilterEffectRenderNode(effect.ToResource(CompositionContext.Default));
        node.AddChild(CreateSource());
        return node;
    }

    private static FilterEffectRenderNode CreateSerialBlurNodes()
    {
        RenderNode current = CreateSource();
        FilterEffectRenderNode? outer = null;
        foreach (float sigma in s_blurSigmas)
        {
            outer = new FilterEffectRenderNode(
                CreateBlur(sigma).ToResource(CompositionContext.Default));
            outer.AddChild(current);
            current = outer;
        }

        return outer!;
    }

    private static FilterEffectRenderNode CreateDynamicCustomBlurChain(
        int outputCount,
        FilterEffect blur,
        Action callback)
    {
        var custom = new FilterEffectRenderNode(
            new RuntimeCardinalityCustomEffect(outputCount, callback)
                .ToResource(CompositionContext.Default));
        custom.AddChild(CreateSource());

        var outer = new FilterEffectRenderNode(
            blur.ToResource(CompositionContext.Default));
        outer.AddChild(custom);
        return outer;
    }

    private static FilterEffectRenderNode CreateDeepAlternatingBlurCopyChain(
        ImageSource.Resource source,
        bool synchronizeSource,
        Action copyCallback)
    {
        RenderNode current = new ImageSourceRenderNode(source, Brushes.Resource.White, null);
        FilterEffectRenderNode? outer = null;
        for (int i = 0; i < DeepAlternatingPairCount; i++)
        {
            outer = WrapFilter(current, CreateBlur(DeepAlternatingBlurSigma));
            current = outer;

            outer = WrapFilter(current, new CombiningCopyCustomEffect(
                synchronizeSource,
                copyCallback));
            current = outer;
        }

        return outer!;
    }

    private static FilterEffectRenderNode WrapFilter(RenderNode input, FilterEffect effect)
    {
        var node = new FilterEffectRenderNode(effect.ToResource(CompositionContext.Default));
        node.AddChild(input);
        return node;
    }

    private static EllipseRenderNode CreateSource()
        => new(s_sourceBounds, Brushes.Resource.White, null);

    private static FilterEffectGroup CreateBlurGroup()
    {
        var group = new FilterEffectGroup();
        foreach (float sigma in s_blurSigmas)
            group.Children.Add(CreateBlur(sigma));
        return group;
    }

    private static Rect GetBlurChainBounds()
        => GetBlurChainBounds(s_sourceBounds);

    private static Rect GetBlurChainBounds(Rect sourceBounds)
        => sourceBounds.Inflate(new Thickness(s_blurSigmas.Sum() * 3));

    private static Blur CreateBlur(float sigma)
    {
        var blur = new Blur();
        blur.Sigma.CurrentValue = new Size(sigma, sigma);
        return blur;
    }

    private static RenderNodeRenderer CreateRenderer(
        RenderNode node,
        float outputDensity = 1,
        float maxWorkingDensity = 4,
        Rect? requestedRegion = null,
        RenderCacheOptions? cacheOptions = null,
        Rect? targetDomain = null)
        => new(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    Intent = RenderIntent.Preview,
                    OutputScale = outputDensity,
                    MaxWorkingScale = maxWorkingDensity,
                    TargetDomain = targetDomain,
                    RequestedRegion = requestedRegion,
                    CacheOptions = cacheOptions ?? RenderCacheOptions.Disabled,
                    Purpose = RenderRequestPurpose.Frame,
                },
                TargetFactory = new CpuTargetFactory(),
            });

    private static void AssertMatchingRgbaF16Rasterization(
        RenderNodeRasterization actual,
        RenderNodeRasterization expected,
        string message)
    {
        Assert.Multiple(() =>
        {
            Assert.That(actual.Bounds, Is.EqualTo(expected.Bounds));
            Assert.That(actual.Bitmap, Is.Not.Null);
            Assert.That(expected.Bitmap, Is.Not.Null);
            Assert.That(actual.Bitmap!.ColorType, Is.EqualTo(BitmapColorType.RgbaF16));
            Assert.That(expected.Bitmap!.ColorType, Is.EqualTo(BitmapColorType.RgbaF16));
            Assert.That(actual.Bitmap.Width, Is.EqualTo(expected.Bitmap.Width));
            Assert.That(actual.Bitmap.Height, Is.EqualTo(expected.Bitmap.Height));
            Assert.That(
                actual.Bitmap.GetPixelSpan().SequenceEqual(expected.Bitmap.GetPixelSpan()),
                Is.True,
                $"{message} {DescribePixelDifference(actual.Bitmap, expected.Bitmap)}");
        });
    }

    private static RenderNodeRasterization RasterizeWithObservedFlushes(
        RenderNodeRenderer renderer,
        ICollection<ImmediateCanvasFlushKind> flushes)
    {
        using (ImmediateCanvas.ObserveFlushes(flushes.Add))
            return renderer.Rasterize();
    }

    private static string DescribePixelDifference(Bitmap actual, Bitmap expected)
    {
        ReadOnlySpan<ushort> actualChannels = actual.GetPixelSpan<ushort>();
        ReadOnlySpan<ushort> expectedChannels = expected.GetPixelSpan<ushort>();
        int differingChannels = 0;
        int differingPixels = 0;
        int maximumBitDelta = 0;
        for (int i = 0; i < actualChannels.Length; i += 4)
        {
            bool pixelDiffers = false;
            for (int channel = 0; channel < 4; channel++)
            {
                int delta = Math.Abs(actualChannels[i + channel] - expectedChannels[i + channel]);
                if (delta == 0)
                    continue;

                differingChannels++;
                pixelDiffers = true;
                maximumBitDelta = Math.Max(maximumBitDelta, delta);
            }

            if (pixelDiffers)
                differingPixels++;
        }

        return $"Differing RGBAF16 channels: {differingChannels}; pixels: {differingPixels}; maximum half-bit delta: {maximumBitDelta}.";
    }

    private static RenderNodeRasterization RasterizeMaterializedBlurPrefixTail(
        float prefixSigma,
        float tailSigma)
    {
        using var prefix = new FilterEffectRenderNode(
            new PublicSkiaBlurEffect(prefixSigma).ToResource(CompositionContext.Default));
        prefix.AddChild(CreateSource());
        using var tail = new FilterEffectRenderNode(
            CreateBlur(tailSigma).ToResource(CompositionContext.Default));
        tail.AddChild(prefix);
        using RenderNodeRenderer renderer = CreateRenderer(tail);
        return renderer.Rasterize();
    }

    private static Bitmap ExtractGlobalRegion(
        Bitmap complete,
        Rect completeBounds,
        Rect requestedRegion)
    {
        PixelRect completePixels = PixelRect.FromRect(completeBounds, 1);
        PixelRect requestedPixels = PixelRect.FromRect(requestedRegion, 1);
        return complete.ExtractSubset(new PixelRect(
            requestedPixels.X - completePixels.X,
            requestedPixels.Y - completePixels.Y,
            requestedPixels.Width,
            requestedPixels.Height));
    }

    private static Bitmap RenderNestedSkiaReference(Rect outputBounds)
    {
        PixelRect deviceBounds = PixelRect.FromRect(outputBounds, 1);
        using RenderTarget target = new CpuRenderTarget(deviceBounds.Size);
        using var canvas = new ImmediateCanvas(
            target,
            density: 1,
            maxWorkingScale: 4,
            logicalSize: deviceBounds.ToRect(1).Size);
        canvas.Clear();

        using SKImageFilter inner = SKImageFilter.CreateBlur(
            s_blurSigmas[0],
            s_blurSigmas[0]);
        using SKImageFilter middle = SKImageFilter.CreateBlur(
            s_blurSigmas[1],
            s_blurSigmas[1],
            inner);
        using SKImageFilter outer = SKImageFilter.CreateBlur(
            s_blurSigmas[2],
            s_blurSigmas[2],
            middle);
        using var paint = new SKPaint { ImageFilter = outer };
        Rect rasterBounds = deviceBounds.ToRect(1);
        using (canvas.PushTransform(Matrix.CreateTranslation(-rasterBounds.X, -rasterBounds.Y)))
        using (canvas.PushBlendMode(BlendMode.SrcOver))
        using (canvas.PushTransform(Matrix.Identity))
        using (canvas.PushPaint(paint))
        {
            canvas.DrawEllipse(s_sourceBounds, Brushes.Resource.White, null);
        }

        return target.Snapshot();
    }

    private static Bitmap RenderNestedSkiaImageReference(
        ImageSource.Resource source,
        Rect outputBounds)
    {
        PixelRect deviceBounds = PixelRect.FromRect(outputBounds, 1);
        using RenderTarget target = new CpuRenderTarget(deviceBounds.Size);
        using var canvas = new ImmediateCanvas(
            target,
            density: 1,
            maxWorkingScale: 4,
            logicalSize: deviceBounds.ToRect(1).Size);
        canvas.Clear();

        using SKImageFilter inner = SKImageFilter.CreateBlur(
            s_blurSigmas[0],
            s_blurSigmas[0]);
        using SKImageFilter middle = SKImageFilter.CreateBlur(
            s_blurSigmas[1],
            s_blurSigmas[1],
            inner);
        using SKImageFilter outer = SKImageFilter.CreateBlur(
            s_blurSigmas[2],
            s_blurSigmas[2],
            middle);
        using var paint = new SKPaint { ImageFilter = outer };
        Rect rasterBounds = deviceBounds.ToRect(1);
        using (canvas.PushTransform(Matrix.CreateTranslation(-rasterBounds.X, -rasterBounds.Y)))
        using (canvas.PushBlendMode(BlendMode.SrcOver))
        using (canvas.PushTransform(Matrix.Identity))
        using (canvas.PushPaint(paint))
        {
            canvas.DrawImageSource(source, Brushes.Resource.White, null);
        }

        return target.Snapshot();
    }

    private static ImageSource.Resource CreateImageSourceResource()
    {
        var source = new ImageSource();
        source.ReadFrom(TestMediaHelper.CreateTestImageUri(40, 32, Colors.White));
        return source.ToResource(CompositionContext.Default);
    }

    private static ImageSource.Resource CreatePatternImageSourceResource()
    {
        using var bitmap = new Bitmap(
            s_deepPatternSize.Width,
            s_deepPatternSize.Height,
            BitmapColorType.Bgra8888,
            BitmapAlphaType.Premul,
            BitmapColorSpace.Srgb);
        Span<Bgra8888> pixels = bitmap.GetPixelSpan<Bgra8888>();
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                uint value = MixPatternCoordinates((uint)x, (uint)y);
                pixels[(y * bitmap.Width) + x] = new Bgra8888(
                    (byte)value,
                    (byte)(value >> 8),
                    (byte)(value >> 16),
                    byte.MaxValue);
            }
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, EncodedImageFormat.Png);
        var source = new ImageSource();
        source.ReadFrom(UriHelper.CreateBase64DataUri("image/png", stream.ToArray()));
        return source.ToResource(CompositionContext.Default);
    }

    private static uint MixPatternCoordinates(uint x, uint y)
    {
        uint value = 20_040_719u ^ (x * 0x9e37_79b9u) ^ (y * 0x85eb_ca6bu);
        value ^= value >> 16;
        value *= 0x7feb_352du;
        value ^= value >> 15;
        value *= 0x846c_a68bu;
        return value ^ (value >> 16);
    }

    private sealed class FixedWorkingScaleFilterRenderNode(
        FilterEffect.Resource effect,
        float workingDensity) : FilterEffectRenderNode(effect)
    {
        private readonly RenderScaleContract _scale = RenderScaleContract.Custom(
            _ => workingDensity);

        protected override RenderScaleContract? GetWorkingScaleContract() => _scale;
    }

    private sealed class CacheableEllipseSourceNode : RenderNode
    {
        private static readonly RenderResourceSlot<Brush.Resource> s_fillSlot = new();
        private static readonly RenderResourceSlot<ExecutionProbe> s_probeSlot = new();
        private readonly ExecutionProbe _probe = new();

        public int ExecuteCount => _probe.Count;

        public override void Process(RenderNodeContext context)
        {
            Brush.Resource fillResource = Brushes.Resource.White;
            RenderResource<Brush.Resource> fill = context.Borrow(fillResource);
            RenderResource<ExecutionProbe> probe = context.Borrow(_probe);
            OpaqueRenderDescription description = OpaqueRenderDescription.Create(
                "cacheable-ellipse",
                static (session, _) => session.UseResource(s_probeSlot, currentProbe =>
                {
                    currentProbe.Record();
                    session.UseResource(s_fillSlot, currentFill =>
                    {
                        using OpaqueRenderOutput output = session.CreateOutput(s_sourceBounds);
                        output.Canvas.Use(canvas =>
                            canvas.DrawEllipse(s_sourceBounds, currentFill, null));
                        session.Publish(output);
                    });
                }),
                OpaqueRenderBoundsContract.Source(s_sourceBounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                resources: [s_fillSlot.Bind(fill), s_probeSlot.Bind(probe)]);
            context.Publish(context.OpaqueSource(description));
        }
    }

    private sealed class TwoValueExpansionNode : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle seed = context.OpaqueSource(
                OpaqueRenderDescription.CreateRequestLocal(
                    static session =>
                    {
                        using OpaqueRenderOutput output = session.CreateOutput(s_sourceBounds);
                        output.Canvas.Use(canvas => canvas.Clear(Colors.Transparent));
                        session.Publish(output);
                    },
                    OpaqueRenderBoundsContract.Source(s_sourceBounds),
                    RenderHitTestContract.OutputBounds,
                    RenderValueCardinality.Single,
                    RenderScaleContract.MaterializeAtWorkingScale));
            RenderFragmentHandle expanded = context.OpaqueExpand(
                [seed],
                OpaqueRenderDescription.CreateRequestLocal(
                    static session =>
                    {
                        using OpaqueRenderOutput red = session.CreateOutput(session.OutputBounds);
                        red.Canvas.Use(canvas => canvas.Clear(Colors.Red));
                        session.Publish(red);

                        using OpaqueRenderOutput blue = session.CreateOutput(session.OutputBounds);
                        blue.Canvas.Use(canvas => canvas.Clear(Colors.Blue));
                        session.Publish(blue);
                    },
                    OpaqueRenderBoundsContract.FullInputs(static inputs => inputs.Single()),
                    RenderHitTestContract.AnyInput,
                    RenderValueCardinality.Exactly(2),
                    RenderScaleContract.MaterializeAtWorkingScale));
            context.Publish(expanded);
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
                ?? throw new InvalidOperationException("Could not create the CPU direct-replay test surface."),
            size.Width,
            size.Height);

    [SuppressResourceClassGeneration]
    private sealed partial class CallbackCustomEffect(Action callback) : FilterEffect
    {
        public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
        {
            context.CustomEffect(
                0,
                (_, _) => callback(),
                static (_, bounds) => bounds);
        }

        public override Resource ToResource(CompositionContext context)
        {
            var resource = new Resource();
            bool updateOnly = false;
            resource.Update(this, context, ref updateOnly);
            return resource;
        }

        public new sealed class Resource : FilterEffect.Resource
        {
            public Resource()
            {
            }
        }
    }

    [SuppressResourceClassGeneration]
    private sealed partial class RuntimeCardinalityCustomEffect(
        int outputCount,
        Action callback) : FilterEffect
    {
        public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
        {
            context.CustomEffect(
                (OutputCount: outputCount, Callback: callback),
                static (state, execution) =>
                {
                    state.Callback();
                    switch (state.OutputCount)
                    {
                        case 0:
                            while (execution.Targets.Count > 0)
                            {
                                int index = execution.Targets.Count - 1;
                                execution.Targets[index].Dispose();
                                execution.Targets.RemoveAt(index);
                            }

                            break;
                        case 1:
                            break;
                        case 2:
                            execution.Targets.Add(execution.Targets.Single().Clone());
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(
                                nameof(state.OutputCount),
                                state.OutputCount,
                                "The test effect supports zero, one, or two outputs.");
                    }
                },
                static (_, bounds) => bounds);
        }

        public override Resource ToResource(CompositionContext context)
        {
            var resource = new Resource();
            bool updateOnly = false;
            resource.Update(this, context, ref updateOnly);
            return resource;
        }

        public new sealed class Resource : FilterEffect.Resource
        {
            public Resource()
            {
            }
        }
    }

    [SuppressResourceClassGeneration]
    private sealed partial class CombiningCopyCustomEffect(
        bool synchronizeSource,
        Action callback) : FilterEffect
    {
        public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
        {
            context.CustomEffect(
                0,
                (_, execution) =>
                {
                    callback();
                    Rect combinedBounds = execution.Targets.CalculateBounds();
                    EffectTarget combined = execution.CreateTarget(combinedBounds);
                    using (ImmediateCanvas canvas = execution.Open(combined))
                    {
                        canvas.Clear();
                        foreach (EffectTarget source in execution.Targets)
                        {
                            if (synchronizeSource)
                            {
                                using Bitmap snapshot = source.RenderTarget!.Snapshot();
                            }

                            using (canvas.PushTransform(Matrix.CreateTranslation(
                                       source.Bounds.Position - combinedBounds.Position)))
                            {
                                source.Draw(canvas);
                            }
                        }
                    }

                    for (int i = execution.Targets.Count - 1; i >= 0; i--)
                    {
                        execution.Targets[i].Dispose();
                        execution.Targets.RemoveAt(i);
                    }

                    execution.Targets.Add(combined);
                },
                static (_, bounds) => bounds);
        }

        public override Resource ToResource(CompositionContext context)
        {
            var resource = new Resource();
            bool updateOnly = false;
            resource.Update(this, context, ref updateOnly);
            return resource;
        }

        public new sealed class Resource : FilterEffect.Resource
        {
            public Resource()
            {
            }
        }
    }

    [SuppressResourceClassGeneration]
    private sealed partial class PublicSkiaBlurEffect(float sigma = 2) : FilterEffect
    {
        public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
        {
            context.AppendSkiaFilter(
                new Size(sigma, sigma),
                static (sigma, input, _) => SKImageFilter.CreateBlur(sigma.Width, sigma.Height, input),
                static (sigma, bounds) => bounds.Inflate(new Thickness(sigma.Width * 3, sigma.Height * 3)),
                static (sigma, region) => region.Inflate(new Thickness(sigma.Width * 3, sigma.Height * 3)));
        }

        public override Resource ToResource(CompositionContext context)
        {
            var resource = new Resource();
            bool updateOnly = false;
            resource.Update(this, context, ref updateOnly);
            return resource;
        }

        public new sealed class Resource : FilterEffect.Resource
        {
            public Resource()
            {
            }
        }
    }
}
