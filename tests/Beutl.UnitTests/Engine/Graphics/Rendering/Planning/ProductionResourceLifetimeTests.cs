using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Planning;

[TestFixture]
public sealed class ProductionResourceLifetimeTests
{
    private static readonly Rect s_bounds = new(0, 0, 16, 16);

    [TestCase(3)]
    [TestCase(10)]
    public void LinearOpaqueChain_KeepsPeakLiveConstantAndWarmsExactSizeSlots(int stageCount)
    {
        var diagnostics = new RenderPipelineDiagnosticsState();
        using var node = new LinearOpaqueChainNode(stageCount);
        using var renderer = CreateRenderer(node, diagnostics);
        using RenderTarget target = new CpuRenderTarget(16, 16);
        using var canvas = new ImmediateCanvas(target);

        renderer.Render(canvas);
        RenderPipelineDiagnosticSnapshot first = diagnostics.Latest;
        renderer.Render(canvas);
        RenderPipelineDiagnosticSnapshot warmed = diagnostics.Latest;

        Assert.Multiple(() =>
        {
            Assert.That(first.Succeeded, Is.True);
            Assert.That(first[RenderPipelineCounter.IntermediateAcquires], Is.EqualTo(stageCount + 1));
            Assert.That(first[RenderPipelineCounter.IntermediateCreates], Is.EqualTo(2));
            Assert.That(first[RenderPipelineCounter.PeakLiveIntermediates], Is.EqualTo(2));
            Assert.That(first[RenderPipelineCounter.IntermediateDischarges], Is.EqualTo(stageCount + 1));
            Assert.That(warmed.Succeeded, Is.True);
            Assert.That(warmed[RenderPipelineCounter.IntermediateAcquires], Is.EqualTo(stageCount + 1));
            Assert.That(warmed[RenderPipelineCounter.IntermediateCreates], Is.Zero);
            Assert.That(warmed[RenderPipelineCounter.PoolHits], Is.EqualTo(stageCount + 1));
            Assert.That(warmed[RenderPipelineCounter.PeakLiveIntermediates], Is.EqualTo(2));
            Assert.That(renderer.TargetPoolStatistics.LeasedTargets, Is.Zero);
        });
    }

    [Test]
    public void FanOut_RetainsProducerThroughEveryConsumerThenReusesItsSlot()
    {
        var diagnostics = new RenderPipelineDiagnosticsState();
        using var node = new FanOutOpaqueNode();
        using var renderer = CreateRenderer(node, diagnostics);
        using RenderTarget target = new CpuRenderTarget(16, 16);
        using var canvas = new ImmediateCanvas(target);

        Assert.That(() => renderer.Render(canvas), Throws.Nothing);
        RenderPipelineDiagnosticSnapshot first = diagnostics.Latest;
        renderer.Render(canvas);
        RenderPipelineDiagnosticSnapshot warmed = diagnostics.Latest;

        Assert.Multiple(() =>
        {
            Assert.That(node.ExecutionCount, Is.EqualTo(8));
            Assert.That(first[RenderPipelineCounter.IntermediateAcquires], Is.EqualTo(4));
            Assert.That(first[RenderPipelineCounter.IntermediateCreates], Is.EqualTo(3));
            Assert.That(first[RenderPipelineCounter.PeakLiveIntermediates], Is.EqualTo(3));
            Assert.That(first[RenderPipelineCounter.IntermediateDischarges], Is.EqualTo(4));
            Assert.That(warmed[RenderPipelineCounter.IntermediateCreates], Is.Zero);
            Assert.That(warmed[RenderPipelineCounter.PoolHits], Is.EqualTo(4));
            Assert.That(warmed[RenderPipelineCounter.PeakLiveIntermediates], Is.EqualTo(3));
            Assert.That(renderer.TargetPoolStatistics.LeasedTargets, Is.Zero);
        });
    }

    private static RenderNodeRenderer CreateRenderer(
        RenderNode node,
        IRenderPipelineDiagnosticsState diagnostics)
        => new(node, new RenderNodeRendererOptions
        {
            TargetDomain = s_bounds,
            OutputScale = 1,
            MaxWorkingScale = 1,
            Diagnostics = diagnostics,
            UseRenderCache = false,
            TargetFactory = new CpuTargetFactory(),
        });

    private sealed class LinearOpaqueChainNode(int stageCount) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle current = context.OpaqueSource(CreateSourceDescription("linear-source"));
            for (int index = 0; index < stageCount; index++)
                current = context.OpaqueMap(current, CreateMapDescription(("linear-map", index)));
            context.Publish(current);
        }
    }

    private sealed class FanOutOpaqueNode : RenderNode
    {
        public int ExecutionCount { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle source = context.OpaqueSource(CreateSourceDescription(
                "fan-out-source",
                () => ExecutionCount++));
            RenderFragmentHandle left = context.OpaqueMap(
                source,
                CreateMapDescription("fan-out-left", () => ExecutionCount++));
            RenderFragmentHandle right = context.OpaqueMap(
                source,
                CreateMapDescription("fan-out-right", () => ExecutionCount++));
            context.Publish(context.OpaqueCombine(
                [left, right],
                CreateCombineDescription("fan-out-combine", () => ExecutionCount++)));
        }
    }

    private static OpaqueRenderDescription CreateSourceDescription(
        object key,
        Action? onExecute = null)
        => OpaqueRenderDescription.Create(
            session =>
            {
                onExecute?.Invoke();
                using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                output.Canvas.Use(static canvas => canvas.Clear(Colors.CornflowerBlue));
                session.Publish(output);
            },
            RenderOperationBoundsContract.Source(s_bounds),
            RenderHitTestContract.OutputBounds,
            RenderValueCardinality.Single,
            RenderScaleContract.MaterializeAtWorkingScale,
            structuralKey: key,
            runtimeIdentity: new RenderRuntimeIdentity(key));

    private static OpaqueRenderDescription CreateMapDescription(
        object key,
        Action? onExecute = null)
        => OpaqueRenderDescription.Create(
            session =>
            {
                onExecute?.Invoke();
                using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                output.Canvas.Use(session.Inputs.Single().Draw);
                session.Publish(output);
            },
            RenderOperationBoundsContract.Map(RenderBoundsContract.Identity),
            RenderHitTestContract.AnyInput,
            RenderValueCardinality.Single,
            RenderScaleContract.PreserveInputSupply,
            structuralKey: key,
            runtimeIdentity: new RenderRuntimeIdentity(key));

    private static OpaqueRenderDescription CreateCombineDescription(
        object key,
        Action? onExecute = null)
        => OpaqueRenderDescription.Create(
            session =>
            {
                onExecute?.Invoke();
                using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                output.Canvas.Use(canvas =>
                {
                    foreach (RenderExecutionInput input in session.Inputs)
                        input.Draw(canvas);
                });
                session.Publish(output);
            },
            RenderOperationBoundsContract.FullInputs(
                static inputs => inputs.Aggregate(Rect.Empty, static (result, input) => result.Union(input)),
                key),
            RenderHitTestContract.AnyInput,
            RenderValueCardinality.Single,
            RenderScaleContract.MaterializeAtWorkingScale,
            structuralKey: key,
            runtimeIdentity: new RenderRuntimeIdentity(key));

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
