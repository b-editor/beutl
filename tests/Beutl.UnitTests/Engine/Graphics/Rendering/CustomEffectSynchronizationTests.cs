using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
[NonParallelizable]
public sealed class CustomEffectSynchronizationTests
{
    private static readonly Rect s_sourceBounds = new(3, 4, 18, 14);
    private static readonly Rect s_targetDomain = new(0, 0, 28, 24);

    [Test]
    [Category("GpuPassFusionGpu")]
    public void ExecutorManagedCustomEffect_CpuTargetsDoNotFlushInitializedSharedContext()
    {
        VulkanTestEnvironment.EnsureAvailable();
        using FilterEffectRenderNode effectNode = CreateFilterNode(new CopyingCustomEffect());
        using RenderNodeRenderer effectRenderer = CreateRenderer(effectNode);
        using var referenceNode = new EllipseRenderNode(s_sourceBounds, Brushes.Resource.White, null);
        using RenderNodeRenderer referenceRenderer = CreateRenderer(referenceNode);

        using RenderNodeRasterization actual = effectRenderer.Rasterize();
        using RenderNodeRasterization expected = referenceRenderer.Rasterize();

        using var destination = new CpuRenderTarget(new PixelSize(
            (int)s_targetDomain.Width,
            (int)s_targetDomain.Height));
        using var canvas = new ImmediateCanvas(destination, logicalSize: s_targetDomain.Size);
        var flushes = new List<ImmediateCanvasFlushKind>();
        using (ImmediateCanvas.ObserveFlushes(flushes.Add))
            effectRenderer.Render(canvas);

        Assert.Multiple(() =>
        {
            Assert.That(actual.Bounds, Is.EqualTo(expected.Bounds));
            Assert.That(actual.Bitmap, Is.Not.Null);
            Assert.That(expected.Bitmap, Is.Not.Null);
            Assert.That(actual.Bitmap!.GetPixelSpan().SequenceEqual(expected.Bitmap!.GetPixelSpan()), Is.True,
                "A deferred draw must retain its source after the callback disposes the source target.");
            Assert.That(actual.Bitmap.GetPixelSpan().ToArray(), Has.Some.Not.Zero);
            Assert.That(flushes, Is.EqualTo(new[] { ImmediateCanvasFlushKind.SourceSurface }),
                "CPU executor canvases must not flush the initialized shared GPU context or submit a raster surface.");
        });
        AssertFlushCounts(
            flushes,
            canvasSubmit: 0,
            canvasClose: 0,
            sourceSurface: 1,
            prepareForSampling: 0);
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    public void ExecutorManagedCustomEffect_GpuDeferredDrawSurvivesSourceDisposeWithoutInternalFlush()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using FilterEffectRenderNode effectNode = CreateFilterNode(new CopyingCustomEffect());
            using RenderNodeRenderer effectRenderer = CreateGpuRenderer(effectNode);
            using FilterEffectRenderNode referenceNode = CreateFilterNode(
                new CopyingCustomEffect(synchronizeSource: true));
            using RenderNodeRenderer referenceRenderer = CreateGpuRenderer(referenceNode);
            var size = new PixelSize((int)s_targetDomain.Width, (int)s_targetDomain.Height);
            using RenderTarget actualTarget = RenderTarget.Create(size.Width, size.Height)
                ?? throw new InvalidOperationException("Could not create the GPU custom-effect target.");
            using RenderTarget expectedTarget = RenderTarget.Create(size.Width, size.Height)
                ?? throw new InvalidOperationException("Could not create the GPU reference target.");

            var actualCanvas = new ImmediateCanvas(actualTarget, logicalSize: s_targetDomain.Size);
            actualCanvas.Clear();
            var executionFlushes = new List<ImmediateCanvasFlushKind>();
            using (ImmediateCanvas.ObserveFlushes(executionFlushes.Add))
                effectRenderer.Render(actualCanvas);

            var callerCloseFlushes = new List<ImmediateCanvasFlushKind>();
            using (ImmediateCanvas.ObserveFlushes(callerCloseFlushes.Add))
                actualCanvas.Dispose();

            var readbackFlushes = new List<ImmediateCanvasFlushKind>();
            Bitmap actual;
            using (ImmediateCanvas.ObserveFlushes(readbackFlushes.Add))
                actual = actualTarget.Snapshot();
            using (actual)
            {
                using (var expectedCanvas = new ImmediateCanvas(
                           expectedTarget,
                           logicalSize: s_targetDomain.Size))
                {
                    expectedCanvas.Clear();
                    referenceRenderer.Render(expectedCanvas);
                }

                using Bitmap expected = expectedTarget.Snapshot();
                Assert.Multiple(() =>
                {
                    Assert.That(actual.ColorType, Is.EqualTo(BitmapColorType.RgbaF16));
                    Assert.That(expected.ColorType, Is.EqualTo(BitmapColorType.RgbaF16));
                    Assert.That(actual.GetPixelSpan().SequenceEqual(expected.GetPixelSpan()), Is.True,
                        "The queued GPU copy must remain byte-exact after the callback disposes its source.");
                    Assert.That(actual.GetPixelSpan().ToArray(), Has.Some.Not.Zero);
                    Assert.That(executionFlushes,
                        Is.EqualTo(new[]
                        {
                            ImmediateCanvasFlushKind.CanvasSubmit,
                            ImmediateCanvasFlushKind.CanvasSubmit,
                            ImmediateCanvasFlushKind.SourceSurface,
                        }),
                        "Each executor-owned GPU canvas submits its queued work; only the final caller-owned draw flushes a source.");
                    Assert.That(callerCloseFlushes,
                        Is.EqualTo(new[] { ImmediateCanvasFlushKind.CanvasClose }),
                        "The caller-owned canvas retains its explicit close-time synchronization.");
                    Assert.That(readbackFlushes,
                        Is.EqualTo(new[] { ImmediateCanvasFlushKind.PrepareForSampling }),
                        "The final CPU readback retains its explicit sampling synchronization.");
                });
                AssertFlushCounts(
                    executionFlushes,
                    canvasSubmit: 2,
                    canvasClose: 0,
                    sourceSurface: 1,
                    prepareForSampling: 0);
                AssertFlushCounts(
                    callerCloseFlushes,
                    canvasSubmit: 0,
                    canvasClose: 1,
                    sourceSurface: 0,
                    prepareForSampling: 0);
                AssertFlushCounts(
                    readbackFlushes,
                    canvasSubmit: 0,
                    canvasClose: 0,
                    sourceSurface: 0,
                    prepareForSampling: 1);
            }
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    public void ExecutorManagedCustomEffect_CrossContextCopyFlushesSourceThenSubmitsDestination()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var bounds = new Rect(0, 0, 16, 12);
            using var source = new CpuRenderTarget(new PixelSize(
                (int)bounds.Width,
                (int)bounds.Height));
            source.Value.Canvas.Clear(SKColors.OrangeRed);
            source.Value.Flush();
            using Bitmap expected = source.Snapshot();
            using var targets = new EffectTargets
            {
                new EffectTarget(source, bounds, EffectiveScale.At(1)),
            };
            var effect = new CopyingCustomEffect();
            using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
            using var context = new FilterEffectContext(bounds);
            context.ApplyTransactional(effect, resource);
            using var builder = new SKImageFilterBuilder();
            using var activator = new FilterEffectActivator(
                targets,
                builder,
                RenderIntent.Preview,
                RenderRequestPurpose.Auxiliary,
                outputScale: 1,
                workingScale: 1,
                maxWorkingScale: 1,
                deviceGridOffset: default,
                useExecutorManagedCanvas: true);
            var flushes = new List<ImmediateCanvasFlushKind>();

            Assert.That(source.Value.Context, Is.Null);
            using (ImmediateCanvas.ObserveFlushes(flushes.Add))
                activator.Apply(context);

            RenderTarget actualTarget = activator.CurrentTargets.Single().RenderTarget!;
            Assert.That(actualTarget.Value.Context, Is.Not.Null);
            using Bitmap actual = actualTarget.Snapshot();
            Assert.Multiple(() =>
            {
                Assert.That(actual.GetPixelSpan().SequenceEqual(expected.GetPixelSpan()), Is.True);
                Assert.That(flushes, Is.EqualTo(new[]
                {
                    ImmediateCanvasFlushKind.SourceSurface,
                    ImmediateCanvasFlushKind.CanvasSubmit,
                }), "A CPU source crossing to the GPU must flush before the GPU destination submits.");
            });
            AssertFlushCounts(
                flushes,
                canvasSubmit: 1,
                canvasClose: 0,
                sourceSurface: 1,
                prepareForSampling: 0);
        });
    }

    [Test]
    public void ExecutorManagedCustomEffect_ExplicitMappedInputSamplingStillPreparesSource()
    {
        using FilterEffectRenderNode node = CreateFilterNode(new SamplingCustomEffect());
        using RenderNodeRenderer renderer = CreateRenderer(node);
        using var destination = new CpuRenderTarget(new PixelSize(
            (int)s_targetDomain.Width,
            (int)s_targetDomain.Height));
        using var canvas = new ImmediateCanvas(destination, logicalSize: s_targetDomain.Size);
        var flushes = new List<ImmediateCanvasFlushKind>();

        using (ImmediateCanvas.ObserveFlushes(flushes.Add))
            renderer.Render(canvas);

        Assert.Multiple(() =>
        {
            Assert.That(flushes, Is.EqualTo(new[]
            {
                ImmediateCanvasFlushKind.PrepareForSampling,
                ImmediateCanvasFlushKind.SourceSurface,
            }), "Explicit shader sampling remains a synchronization boundary before the final source draw.");
        });
        AssertFlushCounts(
            flushes,
            canvasSubmit: 0,
            canvasClose: 0,
            sourceSurface: 1,
            prepareForSampling: 1);
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    public void PublicFilterEffectActivator_RetainsLegacyCanvasAndSourceFlushes()
    {
        VulkanTestEnvironment.EnsureAvailable();
        var bounds = new Rect(0, 0, 16, 12);
        using var source = new CpuRenderTarget(new PixelSize(
            (int)bounds.Width,
            (int)bounds.Height));
        source.Value.Canvas.Clear(SKColors.OrangeRed);
        source.Value.Flush();
        using Bitmap expected = source.Snapshot();
        using var targets = new EffectTargets
        {
            new EffectTarget(source, bounds, EffectiveScale.At(1)),
        };
        var effect = new CopyingCustomEffect();
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var context = new FilterEffectContext(bounds);
        context.ApplyTransactional(effect, resource);
        using var builder = new SKImageFilterBuilder();
        using var activator = new FilterEffectActivator(
            targets,
            builder,
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            outputScale: 1,
            workingScale: 1,
            maxWorkingScale: 1);
        var flushes = new List<ImmediateCanvasFlushKind>();

        using (ImmediateCanvas.ObserveFlushes(flushes.Add))
            activator.Apply(context);

        using Bitmap actual = activator.CurrentTargets.Single().RenderTarget!.Snapshot();
        Assert.Multiple(() =>
        {
            Assert.That(actual.GetPixelSpan().SequenceEqual(expected.GetPixelSpan()), Is.True);
            Assert.That(flushes, Is.EqualTo(new[]
            {
                ImmediateCanvasFlushKind.SourceSurface,
                ImmediateCanvasFlushKind.CanvasClose,
            }), "The public standalone activator must retain its legacy source and context flushes.");
        });
        AssertFlushCounts(
            flushes,
            canvasSubmit: 0,
            canvasClose: 1,
            sourceSurface: 1,
            prepareForSampling: 0);
    }

    private static void AssertFlushCounts(
        IReadOnlyCollection<ImmediateCanvasFlushKind> flushes,
        int canvasSubmit,
        int canvasClose,
        int sourceSurface,
        int prepareForSampling)
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                flushes.Count(static item => item == ImmediateCanvasFlushKind.CanvasSubmit),
                Is.EqualTo(canvasSubmit));
            Assert.That(
                flushes.Count(static item => item == ImmediateCanvasFlushKind.CanvasClose),
                Is.EqualTo(canvasClose));
            Assert.That(
                flushes.Count(static item => item == ImmediateCanvasFlushKind.SourceSurface),
                Is.EqualTo(sourceSurface));
            Assert.That(
                flushes.Count(static item => item == ImmediateCanvasFlushKind.PrepareForSampling),
                Is.EqualTo(prepareForSampling));
        });
    }

    private static FilterEffectRenderNode CreateFilterNode(FilterEffect effect)
    {
        var node = new FilterEffectRenderNode(effect.ToResource(CompositionContext.Default));
        node.AddChild(new EllipseRenderNode(s_sourceBounds, Brushes.Resource.White, null));
        return node;
    }

    private static RenderNodeRenderer CreateRenderer(RenderNode node)
        => new(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    TargetDomain = s_targetDomain,
                    OutputScale = 1,
                    MaxWorkingScale = 1,
                    CacheOptions = RenderCacheOptions.Disabled,
                    Purpose = RenderRequestPurpose.Frame,
                },
                TargetFactory = new CpuTargetFactory(),
            });

    private static RenderNodeRenderer CreateGpuRenderer(RenderNode node)
        => new(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    TargetDomain = s_targetDomain,
                    OutputScale = 1,
                    MaxWorkingScale = 1,
                    CacheOptions = RenderCacheOptions.Disabled,
                    Purpose = RenderRequestPurpose.Frame,
                },
            });

    [SuppressResourceClassGeneration]
    private sealed partial class CopyingCustomEffect(bool synchronizeSource = false) : FilterEffect
    {
        public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
        {
            context.CustomEffect(
                synchronizeSource,
                static (synchronize, execution) => execution.ForEach((_, source) =>
                {
                    if (synchronize)
                    {
                        using Bitmap snapshot = source.RenderTarget!.Snapshot();
                    }

                    EffectTarget replacement = execution.CreateTargetLike(source);
                    using (ImmediateCanvas canvas = execution.Open(replacement))
                    {
                        canvas.Clear();
                        source.Draw(canvas);
                    }

                    return replacement;
                }),
                static (_, bounds) => bounds);
        }

        public override Resource ToResource(CompositionContext context)
        {
            var resource = new Resource();
            bool updateOnly = false;
            resource.Update(this, context, ref updateOnly);
            return resource;
        }

        public new sealed class Resource : FilterEffect.Resource;
    }

    [SuppressResourceClassGeneration]
    private sealed partial class SamplingCustomEffect : FilterEffect
    {
        public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
        {
            context.CustomEffect(
                0,
                static (_, execution) => execution.ForEach((_, source) =>
                {
                    using EffectTarget destination = execution.CreateTargetLike(source);
                    bool sampled = execution.UseMappedInputShader(
                        source,
                        destination,
                        0,
                        static (_, _) => { });
                    if (!sampled)
                    {
                        throw new InvalidOperationException("The CPU source could not be sampled.");
                    }

                    return source;
                }),
                static (_, bounds) => bounds);
        }

        public override Resource ToResource(CompositionContext context)
        {
            var resource = new Resource();
            bool updateOnly = false;
            resource.Update(this, context, ref updateOnly);
            return resource;
        }

        public new sealed class Resource : FilterEffect.Resource;
    }

    private sealed class CpuTargetFactory : IRenderTargetFactory
    {
        public int GetMaximumDimension(RenderTargetAllocationDescriptor allocation)
            => RenderScaleUtilities.MaxBufferDimension;

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
                ?? throw new InvalidOperationException("Could not create a CPU custom-effect test surface."),
            size.Width,
            size.Height);
}
