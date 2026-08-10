using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[NonParallelizable]
[TestFixture]
public class NodeCacheScaleTests
{
    private static readonly Rect s_bounds = new(0, 0, 100, 100);

    private static EllipseRenderNode CacheableEllipse()
    {
        var node = new EllipseRenderNode(s_bounds, Brushes.Resource.White, null);
        node.Cache.ReportRenderCount(RenderNodeCache.Count);
        return node;
    }

    [TestCase(0.5f)]
    [TestCase(1.0f)]
    public void FrameCache_RecordsResolvedDensity_WhileMetadataRemainsCacheIndependent(float outputScale)
    {
        using EllipseRenderNode node = CacheableEllipse();
        using var renderer = CreateFrameRenderer(
            node,
            outputScale,
            maxWorkingScale: 2f * outputScale);

        using (renderer.Rasterize())
        {
        }
        RenderNodeMeasurement measurement = renderer.Measure();

        Assert.Multiple(() =>
        {
            Assert.That(node.Cache.IsCached, Is.True);
            Assert.That(node.Cache.IdentityDensity, Is.EqualTo(outputScale));
            Assert.That(measurement.HasFragments, Is.True);
            Assert.That(measurement.EffectiveScale.IsUnbounded, Is.True,
                "metadata must retain the original graph instead of substituting a pixel cache");
        });
    }

    [Test]
    public void HighDensitySource_IsCachedAtItsResolvedSupplyDensity()
    {
        using var node = new ConcreteSourceNode();
        node.Cache.ReportRenderCount(RenderNodeCache.Count);
        using var renderer = CreateFrameRenderer(node, outputScale: 1f, maxWorkingScale: 8f);

        using (renderer.Rasterize())
        using (renderer.Rasterize())
        {
        }

        Assert.Multiple(() =>
        {
            Assert.That(node.Cache.IsCached, Is.True);
            Assert.That(node.Cache.IdentityDensity, Is.EqualTo(4f));
            Assert.That(node.ExecuteCount, Is.EqualTo(1), "the warm frame must use the cached producer output");
            Assert.That(renderer.Measure().EffectiveScale.Value, Is.EqualTo(4f));
        });
    }

    [Test]
    public void FrameCache_TargetSizeMatchesResolvedDensity()
    {
        using EllipseRenderNode node = CacheableEllipse();
        using var renderer = CreateFrameRenderer(node, outputScale: 0.5f, maxWorkingScale: 1f);

        using (renderer.Rasterize())
        {
        }

        Assert.That(node.Cache.IsCached, Is.True);
        foreach ((RenderTarget target, Rect bounds) in node.Cache.UseCache())
        {
            using (target)
            {
                PixelRect expectedDeviceBounds = RenderScaleUtilities.AddRasterApron(
                    PixelRect.FromRect(bounds, 0.5f));
                Assert.That(target.Width, Is.EqualTo(expectedDeviceBounds.Width));
                Assert.That(target.Height, Is.EqualTo(expectedDeviceBounds.Height));
            }
        }
    }

    [Test]
    public void CacheRuleBypass_IsRequestPolicyAndDoesNotPoisonLaterEligibleFrames()
    {
        using EllipseRenderNode node = CacheableEllipse();
        using (RenderNodeRenderer excluded = CreateFrameRenderer(
                   node,
                   outputScale: 1f,
                   maxWorkingScale: 1f,
                   cacheRules: new RenderCacheRules(9_999, 1)))
        using (excluded.Rasterize())
        {
        }

        Assert.That(node.Cache.IsCached, Is.False);

        using (RenderNodeRenderer eligible = CreateFrameRenderer(
                   node,
                   outputScale: 1f,
                   maxWorkingScale: 1f,
                   cacheRules: RenderCacheRules.Default))
        using (eligible.Rasterize())
        {
        }

        Assert.That(node.Cache.IsCached, Is.True);
    }

    [Test]
    public void ApronedDirectReplayCache_ColdAndWarmUsePlannedClampedDensity()
    {
        var bounds = new Rect(0, 0, RenderScaleUtilities.MaxBufferDimension, 1);
        float expectedDensity =
            RenderScaleUtilities.ClampWorkingScaleToRasterApronBudget(bounds, 1);
        using var node = new RasterApronSourceNode(bounds);
        node.Cache.ReportRenderCount(RenderNodeCache.Count);
        var diagnostics = new RenderPipelineDiagnosticsState();
        using var renderer = CreateFrameRenderer(
            node,
            outputScale: 1,
            maxWorkingScale: 1,
            targetDomain: bounds,
            diagnostics: diagnostics);

        using RenderNodeRasterization cold = renderer.Rasterize();
        using RenderNodeRasterization warm = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(expectedDensity, Is.LessThan(1));
            Assert.That(cold.IsEmpty, Is.False);
            Assert.That(warm.IsEmpty, Is.False);
            Assert.That(node.Cache.IsCached, Is.True);
            Assert.That(node.Cache.IdentityDensity, Is.EqualTo(expectedDensity));
            Assert.That(node.ExecuteCount, Is.EqualTo(1));
            Assert.That(
                diagnostics.Latest[RenderPipelineCounter.RenderCacheHits],
                Is.EqualTo(1));
        });
    }

    [Test]
    public void BoundedValueReplayCache_PartialRoiUsesCompleteApronedDensity()
    {
        var bounds = new Rect(0, 0, RenderScaleUtilities.MaxBufferDimension, 1);
        var requestedRegion = new Rect(
            0,
            0,
            RenderScaleUtilities.MaxBufferDimension / 2,
            1);
        float expectedDensity =
            RenderScaleUtilities.ClampWorkingScaleToRasterApronBudget(bounds, 1);
        using var node = new BoundedValueReplayNode(bounds);
        node.Cache.ReportRenderCount(RenderNodeCache.Count);
        var diagnostics = new RenderPipelineDiagnosticsState();
        using var renderer = CreateFrameRenderer(
            node,
            outputScale: 1,
            maxWorkingScale: 1,
            targetDomain: bounds,
            requestedRegion: requestedRegion,
            diagnostics: diagnostics);

        using RenderNodeRasterization cold = renderer.Rasterize();
        using RenderNodeRasterization warm = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(expectedDensity, Is.LessThan(1));
            Assert.That(cold.Bounds, Is.EqualTo(requestedRegion));
            Assert.That(warm.Bounds, Is.EqualTo(requestedRegion));
            Assert.That(node.Cache.IsCached, Is.True);
            Assert.That(node.Cache.IdentityDensity, Is.EqualTo(expectedDensity));
            Assert.That(node.ExecuteCount, Is.EqualTo(1));
            Assert.That(
                diagnostics.Latest[RenderPipelineCounter.RenderCacheHits],
                Is.EqualTo(1));
        });
    }

    private static RenderNodeRenderer CreateFrameRenderer(
        RenderNode node,
        float outputScale,
        float maxWorkingScale,
        RenderCacheRules? cacheRules = null,
        Rect? targetDomain = null,
        Rect? requestedRegion = null,
        IRenderPipelineDiagnosticsState? diagnostics = null)
        => new(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    TargetDomain = targetDomain ?? s_bounds,
                    RequestedRegion = requestedRegion,
                    OutputScale = outputScale,
                    MaxWorkingScale = maxWorkingScale,
                    CacheOptions = new RenderCacheOptions(
                        true,
                        cacheRules ?? RenderCacheRules.Default),
                    Purpose = RenderRequestPurpose.Frame,
                    Diagnostics = diagnostics,
                },
                TargetFactory = new CpuTargetFactory(),
            });

    private sealed class ConcreteSourceNode : RenderNode
    {
        private readonly ExecutionProbe _probe = new();

        public int ExecuteCount => _probe.Count;

        public override void Process(RenderNodeContext context)
        {
            Brush.Resource fill = Brushes.Resource.White;
            RenderResource<Brush.Resource> fillResource = context.Borrow(
                fill,
                fill.GetOriginal().Id,
                fill.Version);
            RenderResource<ExecutionProbe> probeResource = context.Borrow(
                _probe,
                typeof(ConcreteSourceNode));
            OpaqueRenderDescription description = OpaqueRenderDescription.Create(
                s_bounds,
                static (session, bounds) =>
                    session.UseDeclaredResource<ExecutionProbe>("probe", probe =>
                    {
                        probe.Record();
                        session.UseDeclaredResource<Brush.Resource>("fill", currentFill =>
                        {
                            using OpaqueRenderOutput output = session.CreateOutput(bounds);
                            output.Canvas.Use(canvas => canvas.DrawRectangle(bounds, currentFill, null));
                            session.Publish(output);
                        });
                    }),
                bounds: OpaqueRenderBoundsContract.Source(s_bounds),
                hitTest: RenderHitTestContract.None,
                valueCardinality: RenderValueCardinality.Single,
                scale: RenderScaleContract.Custom(
                    static _ => 4f),
                resources: [fillResource.Bind("fill"), probeResource.Bind("probe")]);
            context.Publish(context.OpaqueSource(description));
        }
    }

    private sealed class RasterApronSourceNode(Rect bounds) : RenderNode
    {
        private readonly ExecutionProbe _probe = new();

        public int ExecuteCount => _probe.Count;

        public override void Process(RenderNodeContext context)
        {
            OpaqueRenderDescription description = OpaqueRenderDescription.CreateEngineSource(
                execute: session =>
                {
                    _probe.Record();
                    using OpaqueRenderOutput output = session.CreateOutput(bounds);
                    output.Canvas.Use(static canvas => canvas.Clear());
                    session.Publish(output);
                },
                directReplay: static session => session.Canvas.Clear(),
                bounds: OpaqueRenderBoundsContract.Source(bounds),
                hitTest: RenderHitTestContract.OutputBounds,
                scale: RenderScaleContract.Vector,
                deviceGridSensitivity: RenderDeviceGridSensitivity.Insensitive,
                structuralKey: typeof(RasterApronSourceNode),
                runtimeIdentity: new RenderRuntimeIdentity((bounds, _probe)));
            context.Publish(context.OpaqueSource(description));
        }
    }

    private sealed class BoundedValueReplayNode(Rect bounds) : RenderNode
    {
        private readonly ExecutionProbe _probe = new();

        public int ExecuteCount => _probe.Count;

        public override void Process(RenderNodeContext context)
        {
            RenderResource<ExecutionProbe> probeResource = context.Borrow(
                _probe,
                (typeof(BoundedValueReplayNode), bounds));
            OpaqueRenderDescription sourceDescription = OpaqueRenderDescription.Create(
                bounds,
                static (session, currentBounds) =>
                    session.UseDeclaredResource<ExecutionProbe>("probe", probe =>
                    {
                        probe.Record();
                        using OpaqueRenderOutput output = session.CreateOutput(currentBounds);
                        output.Canvas.Use(static canvas => canvas.Clear());
                        session.Publish(output);
                    }),
                bounds: OpaqueRenderBoundsContract.Source(bounds),
                hitTest: RenderHitTestContract.OutputBounds,
                valueCardinality: RenderValueCardinality.Single,
                scale: RenderScaleContract.Custom(
                    static _ => 1),
                resources: [probeResource.Bind("probe")]);
            RenderFragmentHandle source = context.OpaqueSource(sourceDescription);
            TargetScopeDescription replayDescription = TargetScopeDescription.CreateValueReplayMap(
                static session => session.Canvas.Use(_ => session.ReplayInput()),
                RenderBoundsContract.Identity,
                RenderHitTestContract.AnyInput,
                RenderScaleContract.PreserveInputSupply,
                RenderDeviceGridSensitivity.Insensitive,
                RenderDeviceGridMapping.Preserved,
                structuralKey: typeof(BoundedValueReplayNode),
                runtimeIdentity: new RenderRuntimeIdentity(bounds));
            context.Publish(context.TargetScope(source, replayDescription));
        }
    }

    private sealed class CpuTargetFactory : IRenderTargetFactory
    {
        private static readonly SKColorSpace s_colorSpace = SKColorSpace.CreateSrgbLinear();

        public int GetMaximumDimension(RenderTargetAllocationDescriptor allocation)
            => RenderScaleUtilities.MaxBufferDimension;

        public RenderTarget Create(RenderTargetAllocationDescriptor allocation)
            => new CpuRenderTarget(allocation.DeviceSize);

        private sealed class CpuRenderTarget : RenderTarget
        {
            public CpuRenderTarget(PixelSize size)
                : base(
                    SKSurface.Create(new SKImageInfo(
                        size.Width,
                        size.Height,
                        SKColorType.RgbaF16,
                        SKAlphaType.Premul,
                        s_colorSpace))
                    ?? throw new InvalidOperationException("Could not create a CPU cache-scale test target."),
                    size.Width,
                    size.Height)
            {
            }
        }
    }
}
