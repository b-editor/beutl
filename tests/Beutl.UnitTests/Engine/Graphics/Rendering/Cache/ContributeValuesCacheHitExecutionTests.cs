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
                TargetDomain = s_bounds,
                TargetFactory = new CpuTargetFactory(),
                UseRenderCache = true,
                RenderPurpose = RenderRequestPurpose.Frame,
                FusionMode = FusionMode.Disabled,
                Diagnostics = diagnostics,
            });

        using RenderNodeRasterization miss = renderer.Rasterize();
        Assert.That(producer.Cache.IsCached, Is.True,
            "the first render must publish the ContributeValues cache candidate");

        using RenderNodeRasterization hit = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(miss.Bounds, Is.EqualTo(s_bounds));
            Assert.That(hit.Bounds, Is.EqualTo(s_bounds));
            Assert.That(diagnostics.Latest[RenderPipelineCounter.RenderCacheHits], Is.EqualTo(1));
        });
    }

    private sealed class ValueConsumerNode(RenderNode producer) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle input = context.RecordNode(producer, []).Single();
            OpaqueRenderDescription description = OpaqueRenderDescription.Create(
                session =>
                {
                    using OpaqueRenderOutput output = session.CreateOutput(s_bounds);
                    output.Canvas.Use(session.Inputs[0].Draw);
                    session.Publish(output);
                },
                OpaqueRenderBoundsContract.Map(RenderBoundsContract.Identity),
                RenderHitTestContract.AnyInput,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                structuralKey: nameof(ValueConsumerNode),
                runtimeIdentity: new RenderRuntimeIdentity(nameof(ValueConsumerNode)));
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
        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle combined = context.OpaqueCombine([], OpaqueRenderDescription.Create(
                session =>
                {
                    using OpaqueRenderOutput output = session.CreateOutput(s_bounds);
                    output.Canvas.Use(canvas => canvas.Clear(Colors.White));
                    session.Publish(output);
                },
                OpaqueRenderBoundsContract.FullInputs(
                    static _ => s_bounds,
                    "empty-combine-bounds"),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.ZeroOrOne,
                RenderScaleContract.MaterializeAtWorkingScale,
                structuralKey: nameof(EmptyCombineContributionNode),
                runtimeIdentity: new RenderRuntimeIdentity(nameof(EmptyCombineContributionNode))));
            context.Publish(context.ContributeValues(combined));
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
