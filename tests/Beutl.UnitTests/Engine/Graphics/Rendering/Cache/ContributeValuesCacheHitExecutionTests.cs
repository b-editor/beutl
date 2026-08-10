using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Cache;

[TestFixture]
public sealed class ContributeValuesCacheHitExecutionTests
{
    private static readonly Rect s_bounds = new(0, 0, 16, 12);

    [Test]
    public void ContributeValuesCacheHit_DoesNotCompleteThePrunedProducerInput()
    {
        var producer = new EmptyCombineContributionNode();
        producer.Cache.ReportRenderCount(RenderNodeCache.Count);
        using var node = new ValueConsumerNode(producer);
        var diagnostics = new RenderPipelineDiagnosticsState();
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    TargetDomain = s_bounds,
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Enabled,
                    Purpose = RenderRequestPurpose.Frame,
                    FusionMode = FusionMode.Disabled,
                    Diagnostics = diagnostics,
                },
                TargetFactory = new CpuTargetFactory(),
            });

        using RenderNodeRasterization miss = renderer.Rasterize();
        Assert.That(producer.Cache.IsCached, Is.True,
            "the first render must publish the ContributeValues cache candidate");

        using RenderNodeRasterization hit = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(miss.Bounds, Is.EqualTo(s_bounds));
            Assert.That(hit.Bounds, Is.EqualTo(s_bounds));
            Assert.That(producer.ExecuteCount, Is.EqualTo(1),
                "the ContributeValues cache hit must prune the producer callback");
            Assert.That(diagnostics.Latest[RenderPipelineCounter.RenderCacheHits], Is.EqualTo(1));
        });
    }

    [Test]
    public void OpaqueExpandCache_PreservesIndependentOutputDensities()
    {
        var producer = new IndependentDensityProducerNode();
        producer.Cache.ReportRenderCount(RenderNodeCache.Count);
        using var node = new IndependentDensityObserverNode(producer);
        var diagnostics = new RenderPipelineDiagnosticsState();
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    TargetDomain = s_bounds,
                    MaxWorkingScale = 4,
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Enabled,
                    Purpose = RenderRequestPurpose.Frame,
                    FusionMode = FusionMode.Disabled,
                    Diagnostics = diagnostics,
                },
                TargetFactory = new CpuTargetFactory(),
            });

        using RenderNodeRasterization miss = renderer.Rasterize();
        using RenderNodeRasterization hit = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(miss.IsEmpty, Is.False);
            Assert.That(hit.IsEmpty, Is.False);
            Assert.That(producer.Cache.IsCached, Is.True);
            Assert.That(producer.ExecuteCount, Is.EqualTo(1));
            Assert.That(node.ObservedScales, Is.EqualTo(new[]
            {
                new[] { 1f, 2f },
                new[] { 1f, 2f },
            }), "the cold values and cached replay must retain each output's independent density");
            Assert.That(diagnostics.Latest[RenderPipelineCounter.RenderCacheHits], Is.EqualTo(1));
        });
    }

    [Test]
    public void OpaqueExpandCache_RejectsActualOutputsOutsidePixelRule()
    {
        using var node = new IndependentDensityProducerNode();
        node.Cache.ReportRenderCount(RenderNodeCache.Count);
        var diagnostics = new RenderPipelineDiagnosticsState();
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    TargetDomain = s_bounds,
                    MaxWorkingScale = 4,
                    CacheOptions = new RenderCacheOptions(
                        true,
                        new RenderCacheRules(MaxPixels: 200, MinPixels: 1)),
                    Purpose = RenderRequestPurpose.Frame,
                    FusionMode = FusionMode.Disabled,
                    Diagnostics = diagnostics,
                },
                TargetFactory = new CpuTargetFactory(),
            });

        using RenderNodeRasterization first = renderer.Rasterize();
        using RenderNodeRasterization second = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(first.IsEmpty, Is.False);
            Assert.That(second.IsEmpty, Is.False);
            Assert.That(node.Cache.IsCached, Is.False);
            Assert.That(node.ExecuteCount, Is.EqualTo(2));
            Assert.That(diagnostics.Latest[RenderPipelineCounter.RenderCacheHits], Is.Zero);
            Assert.That(diagnostics.Latest[RenderPipelineCounter.RejectedRenderCacheCaptures], Is.EqualTo(1));
        });
    }

    private sealed class ValueConsumerNode(RenderNode producer) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle input = context.RecordNode(producer, []).Single();
            OpaqueRenderDescription description = OpaqueRenderDescription.Create(
                nameof(ValueConsumerNode),
                static (session, _) =>
                {
                    using OpaqueRenderOutput output = session.CreateOutput(s_bounds);
                    output.Canvas.Use(session.Inputs[0].Draw);
                    session.Publish(output);
                },
                OpaqueRenderBoundsContract.Map(RenderBoundsContract.Identity),
                RenderHitTestContract.AnyInput,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                structuralKey: nameof(ValueConsumerNode));
            context.Publish(context.OpaqueMap(input, description));
        }

        protected override void OnDispose(bool disposing)
        {
            producer.Dispose();
            base.OnDispose(disposing);
        }
    }

    private sealed class EmptyCombineContributionNode : RenderNode
    {
        private readonly ExecutionProbe _probe = new();
        private readonly object _probeKey = new();

        public int ExecuteCount => _probe.Count;

        public override void Process(RenderNodeContext context)
        {
            RenderResource<ExecutionProbe> probeResource = context.Borrow(_probe, _probeKey, version: 1);
            RenderFragmentHandle combined = context.OpaqueCombine([], OpaqueRenderDescription.Create(
                typeof(EmptyCombineContributionNode),
                static (session, _) => session.UseDeclaredResource<ExecutionProbe>("probe", probe =>
                {
                    probe.Record();
                    using OpaqueRenderOutput output = session.CreateOutput(s_bounds);
                    output.Canvas.Use(canvas => canvas.Clear(Colors.White));
                    session.Publish(output);
                }),
                OpaqueRenderBoundsContract.FullInputs(
                    static _ => s_bounds,
                    "empty-combine-bounds"),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.ZeroOrOne,
                RenderScaleContract.MaterializeAtWorkingScale,
                structuralKey: nameof(EmptyCombineContributionNode),
                resources: [probeResource.Bind("probe")]));
            context.Publish(context.ContributeValues(combined));
        }
    }

    private sealed class IndependentDensityProducerNode : RenderNode
    {
        private readonly ExecutionProbe _probe = new();
        private readonly object _probeKey = new();

        public int ExecuteCount => _probe.Count;

        public override void Process(RenderNodeContext context)
        {
            RenderResource<ExecutionProbe> probeResource = context.Borrow(_probe, _probeKey, version: 1);
            OpaqueRenderDescription description = OpaqueRenderDescription.Create(
                typeof(IndependentDensityProducerNode),
                static (session, _) => session.UseDeclaredResource<ExecutionProbe>("probe", probe =>
                {
                    probe.Record();
                    using OpaqueRenderOutput left = session.CreateOutput(new Rect(0, 0, 8, 12), density: 1);
                    using OpaqueRenderOutput right = session.CreateOutput(new Rect(8, 0, 8, 12), density: 2);
                    left.Canvas.Use(canvas => canvas.Clear(Colors.Red));
                    right.Canvas.Use(canvas => canvas.Clear(Colors.Blue));
                    session.Publish(left);
                    session.Publish(right);
                }),
                OpaqueRenderBoundsContract.FullInputs(static _ => s_bounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Dynamic,
                RenderScaleContract.MaterializeAtWorkingScale,
                resources: [probeResource.Bind("probe")]);
            RenderFragmentHandle expanded = context.OpaqueExpand([], description);
            context.Publish(context.ContributeValues(expanded));
        }
    }

    private sealed class IndependentDensityObserverNode(IndependentDensityProducerNode producer) : RenderNode
    {
        private readonly RecordingProbe<float[]> _scaleProbe = new();
        private readonly object _probeKey = new();

        public IReadOnlyList<float[]> ObservedScales => _scaleProbe.Records;

        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle input = context.RecordNode(producer, []).Single();
            RenderResource<RecordingProbe<float[]>> probeResource = context.Borrow(
                _scaleProbe,
                _probeKey,
                version: 1);
            OpaqueRenderDescription description = OpaqueRenderDescription.Create(
                typeof(IndependentDensityObserverNode),
                static (session, _) => session.UseDeclaredResource<RecordingProbe<float[]>>("probe", probe =>
                {
                    probe.Record(session.Inputs
                        .Select(static item => item.EffectiveScale.Value)
                        .ToArray());
                    using OpaqueRenderOutput output = session.CreateOutput(s_bounds);
                    output.Canvas.Use(canvas =>
                    {
                        foreach (RenderExecutionInput item in session.Inputs)
                            item.Draw(canvas);
                    });
                    session.Publish(output);
                }),
                OpaqueRenderBoundsContract.FullInputs(static _ => s_bounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                resources: [probeResource.Bind("probe")]);
            context.Publish(context.OpaqueCombine([input], description));
        }

        protected override void OnDispose(bool disposing)
        {
            producer.Dispose();
            base.OnDispose(disposing);
        }
    }

    private sealed class CpuTargetFactory : IRenderTargetFactory
    {
        public int GetMaximumDimension(RenderTargetAllocationDescriptor allocation) => RenderScaleUtilities.MaxBufferDimension;

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
