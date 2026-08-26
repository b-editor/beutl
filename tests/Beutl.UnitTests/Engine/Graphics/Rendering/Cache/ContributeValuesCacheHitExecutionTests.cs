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

    private const int Contributors = 8;

    [Test]
    public void ContributeValuesCacheHit_DoesNotCompleteThePrunedProducerInput()
    {
        var producer = new EmptyCombineContributionNode();
        producer.Cache.RecordStableRequests();
        using var node = new ValueConsumerNode(producer);
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    Intent = RenderIntent.Preview,
                    TargetDomain = s_bounds,
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Enabled,
                    Purpose = RenderRequestPurpose.Frame,
                    FusionMode = FusionMode.Disabled,
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
        });
    }

    [Test]
    public void OpaqueExpandCache_PreservesIndependentOutputDensities()
    {
        var producer = new IndependentDensityProducerNode();
        producer.Cache.RecordStableRequests();
        using var node = new IndependentDensityObserverNode(producer);
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    Intent = RenderIntent.Preview,
                    TargetDomain = s_bounds,
                    MaxWorkingScale = 4,
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Enabled,
                    Purpose = RenderRequestPurpose.Frame,
                    FusionMode = FusionMode.Disabled,
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
        });
    }

    [Test]
    public void OpaqueExpandCache_RejectsActualOutputsOutsidePixelRule()
    {
        using var node = new IndependentDensityProducerNode();
        node.Cache.RecordStableRequests();
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    Intent = RenderIntent.Preview,
                    TargetDomain = s_bounds,
                    MaxWorkingScale = 4,
                    CacheOptions = new RenderCacheOptions(
                        true,
                        new RenderCacheRules(MaxPixels: 200, MinPixels: 1)),
                    Purpose = RenderRequestPurpose.Frame,
                    FusionMode = FusionMode.Disabled,
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
        });
    }

    /// <remarks>
    /// A replay branch that materializes has to report the use complete, or the input's values stay on the
    /// ledger and their pooled target stays leased for the rest of the request. ContributeValues is the one
    /// branch that materializes inline instead of delegating to a method whose finally does it, so a chain of
    /// them held one live intermediate per link where the whole chain needs one.
    /// </remarks>
    [Test]
    public void ChainedContributeValues_HandBackEachIntermediateAsItIsDrawn()
    {
        using var root = new ContainerRenderNode();
        for (int index = 0; index < Contributors; index++)
            root.AddChild(new EmptyCombineContributionNode());

        var factory = new CountingTargetFactory();
        using var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    Intent = RenderIntent.Preview,
                    TargetDomain = s_bounds,
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                    Purpose = RenderRequestPurpose.Frame,
                    FusionMode = FusionMode.Disabled,
                },
                TargetFactory = factory,
            });

        renderer.Rasterize().Dispose();

        // Two: the frame's own target and the one intermediate the contributors take turns with. Holding
        // each contributor's target open instead cost one per link, measured at nine for the eight here.
        Assert.That(factory.Creates, Is.EqualTo(2),
            $"{Contributors} contributors of one size must share the pool, not hold one target each.");
    }

    private sealed class CountingTargetFactory : IRenderTargetFactory
    {
        public int Creates { get; private set; }

        public RenderTarget Create(RenderTargetAllocationDescriptor allocation)
        {
            Creates++;
            return new CpuRenderTarget(allocation.DeviceSize.Width, allocation.DeviceSize.Height);
        }
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
                RenderScaleContract.MaterializeAtWorkingScale);
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
            RenderResource<ExecutionProbe> probeResource = context.Borrow(_probe);
            RenderFragmentHandle combined = context.OpaqueCombine([], OpaqueRenderDescription.Create(
                typeof(EmptyCombineContributionNode),
                static (session, _) => session.UseResource(ContributeValuesCacheHitExecutionSlots.Probe, probe =>
                {
                    probe.Record();
                    using OpaqueRenderOutput output = session.CreateOutput(s_bounds);
                    output.Canvas.Use(canvas => canvas.Clear(Colors.White));
                    session.Publish(output);
                }),
                OpaqueRenderBoundsContract.FullInputs(
                    static _ => s_bounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.ZeroOrOne,
                RenderScaleContract.MaterializeAtWorkingScale,
                resources: [ContributeValuesCacheHitExecutionSlots.Probe.Bind(probeResource)]));
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
            RenderResource<ExecutionProbe> probeResource = context.Borrow(_probe);
            OpaqueRenderDescription description = OpaqueRenderDescription.Create(
                typeof(IndependentDensityProducerNode),
                static (session, _) => session.UseResource(ContributeValuesCacheHitExecutionSlots.Probe, probe =>
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
                resources: [ContributeValuesCacheHitExecutionSlots.Probe.Bind(probeResource)]);
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
            RenderResource<RecordingProbe<float[]>> probeResource = context.Borrow(_scaleProbe);
            OpaqueRenderDescription description = OpaqueRenderDescription.Create(
                typeof(IndependentDensityObserverNode),
                static (session, _) => session.UseResource(ContributeValuesCacheHitExecutionSlots.ScaleProbe, probe =>
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
                resources: [ContributeValuesCacheHitExecutionSlots.ScaleProbe.Bind(probeResource)]);
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

internal static class ContributeValuesCacheHitExecutionSlots
{
    internal static readonly RenderResourceSlot<RecordingProbe<float[]>> ScaleProbe = new();
    internal static readonly RenderResourceSlot<ExecutionProbe> Probe = new();
}
