using System.Collections.Concurrent;
using System.Collections.Immutable;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Graphics.Shaders;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;
using static Beutl.UnitTests.Engine.Graphics.Rendering.Fusion.FusionBoundaryExecutionTestSupport;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Fusion;

[TestFixture]
[NonParallelizable]
public sealed class ExecutionIslandAuthorityTests
{
    private static readonly Rect s_bounds = new(0, 0, 24, 16);

    [Test]
    public void TerminalOpacity_DispatchesTheCompiledRunBeforeSemanticReplay()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using RenderTarget source = FusionBoundaryExecutionTestSupport.CreatePatternSource(s_bounds);
            using var node = new TerminalOpacityNode(source);
            using var renderer = CreateRenderer(node, s_bounds, FusionMode.Enabled);

            using RenderNodeRasterization result = renderer.Rasterize();

            Assert.Multiple(() =>
            {
                Assert.That(result.Bitmap, Is.Not.Null);
                Assert.That(FusionBoundaryExecutionTestSupport.SumAbsoluteChannels(result.Bitmap!),
                    Is.GreaterThan(1));
                Assert.That(renderer.LastExecutionStatistics.ShaderRunExecutions, Is.EqualTo(1));
                Assert.That(renderer.LastExecutionStatistics.ShaderStageExecutions, Is.EqualTo(2));
                Assert.That(renderer.LastExecutionStatistics.FusedShaderRunExecutions, Is.EqualTo(1));
                Assert.That(renderer.LastExecutionStatistics.SpirvShaderRunExecutions, Is.Zero);
            });
        });
    }

    [Test]
    public void SameBackendCompiledRun_HasNoExecutorManagedFlush()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using RenderTarget source = FusionBoundaryExecutionTestSupport.CreatePatternSource(s_bounds);
            using RenderTarget destination = RenderTarget.Create((int)s_bounds.Width, (int)s_bounds.Height)
                ?? throw new InvalidOperationException("Could not allocate the flush-test destination.");
            using var canvas = new ImmediateCanvas(destination, RenderIntent.Preview, logicalSize: s_bounds.Size);
            using var node = new TerminalOpacityNode(source);
            using var renderer = CreateRenderer(node, s_bounds, FusionMode.Enabled);
            var observed = new ConcurrentQueue<ImmediateCanvasFlushKind>();

            using (ImmediateCanvas.ObserveFlushes(observed.Enqueue))
                renderer.Render(canvas);

            Assert.Multiple(() =>
            {
                Assert.That(renderer.LastExecutionStatistics.Synchronizations, Is.Zero);
                Assert.That(observed, Is.Empty,
                    "A same-backend compiled run must not hide synchronization behind canvas disposal or blits.");
            });
        });
    }

    [Test]
    public void OpacityOnly_IsPlannedAsASemanticGpuPassIsland()
    {
        using CompiledRenderRequest compiled = CompileOpacityOnly();
        RenderFragmentReference opacity = compiled.Roots.Single();
        Assert.Multiple(() =>
        {
            Assert.That(compiled.ExecutionPlan.ShaderRuns, Is.Empty);
            Assert.That(compiled.ExecutionPlan.Islands, Has.Exactly(1).Items);
            Assert.That(compiled.ExecutionPlan.TryGetMembership(
                    compiled.Graph,
                    opacity,
                    out ExecutionIslandMembership membership),
                Is.True);
            Assert.That(membership.Island.ShaderRun, Is.Null);
        });
    }

    [Test]
    public void OpacityOnly_RuntimeUsesSemanticReplayWithOneGpuPass()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using RenderTarget source = FusionBoundaryExecutionTestSupport.CreatePatternSource(s_bounds);
            using Bitmap sourceBitmap = source.Snapshot();
            using var node = new OpacityOnlyNode(source);
            using var renderer = CreateRenderer(node, s_bounds, FusionMode.Enabled);

            using RenderNodeRasterization result = renderer.Rasterize();
            double maximumDifference = MaximumOpacityDifference(sourceBitmap, result.Bitmap!, 0.625f);

            Assert.Multiple(() =>
            {
                Assert.That(FusionBoundaryExecutionTestSupport.SumAbsoluteChannels(result.Bitmap!),
                    Is.GreaterThan(1));
                Assert.That(renderer.LastExecutionStatistics.ShaderRunExecutions, Is.Zero);
                Assert.That(renderer.LastExecutionStatistics.ShaderStageExecutions, Is.Zero);
                Assert.That(maximumDifference, Is.LessThan(0.002),
                    "Semantic opacity replay must scale every premultiplied channel and alpha by 0.625.");
            });
        });
    }

    private static double MaximumOpacityDifference(Bitmap source, Bitmap actual, float opacity)
    {
        ReadOnlySpan<ushort> sourcePixels = source.GetPixelSpan<ushort>();
        ReadOnlySpan<ushort> actualPixels = actual.GetPixelSpan<ushort>();
        Assert.That(actualPixels.Length, Is.EqualTo(sourcePixels.Length));
        double maximum = 0;
        for (int index = 0; index < sourcePixels.Length; index++)
        {
            float sourceValue = (float)BitConverter.UInt16BitsToHalf(sourcePixels[index]);
            float actualValue = (float)BitConverter.UInt16BitsToHalf(actualPixels[index]);
            maximum = Math.Max(maximum, Math.Abs(actualValue - (sourceValue * opacity)));
        }

        return maximum;
    }

    [Test]
    public void DeclaredInputReadback_IsPlannedAndCountedOnlyAtActualUse()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using RenderTarget source = FusionBoundaryExecutionTestSupport.CreatePatternSource(s_bounds);
            using var node = new DeclaredInputReadbackNode(source);
            using FusionBoundaryExecutionResult result = FusionBoundaryExecutionTestSupport.Execute(
                node,
                s_bounds,
                FusionMode.Enabled);

            Assert.Multiple(() =>
            {
                Assert.That(FusionBoundaryExecutionTestSupport.SumAbsoluteChannels(result.Bitmap),
                    Is.GreaterThan(1));
                Assert.That(result.Statistics.Synchronizations, Is.EqualTo(1));
            });
        });
    }

    [Test]
    public void PlanLedger_RejectsDirectExecutionOfANonTerminalShaderStage()
    {
        using CompiledRenderRequest compiled = CompileTerminalOpacity();
        CompiledShaderRun run = compiled.ExecutionPlan.ShaderRuns.Single();
        RenderFragmentReference interior = run.GetStage(compiled.Graph, 0);
        ExecutionIslandExecutionLedger ledger = compiled.ExecutionPlan.CreateExecutionLedger(compiled.Graph);

        Assert.That(
            () => ledger.Begin(interior),
            Throws.InvalidOperationException.With.Message.Contains("non-terminal"));
    }

    [Test]
    public void PlanLedger_RejectsDuplicateIslandExecution()
    {
        using CompiledRenderRequest compiled = CompileTerminalOpacity();
        CompiledShaderRun run = compiled.ExecutionPlan.ShaderRuns.Single();
        ExecutionIslandExecutionLedger ledger = compiled.ExecutionPlan.CreateExecutionLedger(compiled.Graph);
        RenderFragmentReference output = run.GetOutput(compiled.Graph);

        ExecutionIsland island = ledger.Begin(output);
        ledger.Complete(island);

        Assert.That(
            () => ledger.Begin(output),
            Throws.InvalidOperationException.With.Message.Contains("more than once"));
    }

    [Test]
    public void PlanLedger_RejectsAnExecutableFragmentMissingFromThePlan()
    {
        using CompiledRenderRequest compiled = CompileTerminalOpacity();
        CompiledShaderRun run = compiled.ExecutionPlan.ShaderRuns.Single();
        var invalid = new ExecutionIslandPlan(compiled.Graph.Fragments.Length, []);
        ExecutionIslandExecutionLedger ledger = invalid.CreateExecutionLedger(compiled.Graph);

        Assert.That(
            () => ledger.Begin(run.GetOutput(compiled.Graph)),
            Throws.InvalidOperationException.With.Message.Contains("not assigned"));
    }

    [Test]
    public void Plan_RejectsOneFragmentAssignedToMultipleIslands()
    {
        Assert.That(
            () => new ExecutionIslandPlan(
            1,
            [
                new ExecutionIsland(
                    0,
                    [0]),
                new ExecutionIsland(
                    1,
                    [0]),
            ]),
            Throws.ArgumentException.With.Message.Contains("more than one execution island"));
    }

    [Test]
    public void PlanLedger_RejectsIncompleteSuccessfulExecution()
    {
        var fixture = CreateReversePublicationFixture();
        ExecutionIslandExecutionLedger ledger = fixture.Plan.CreateExecutionLedger(fixture.Graph);

        ExecutionIsland second = ledger.Begin(fixture.Second);
        ledger.Complete(second);

        Assert.That(
            () => ledger.ValidateCompleted(),
            Throws.InvalidOperationException.With.Message.Contains("must complete"));
    }

    [Test]
    public void Plan_RejectsSameOrdinalFragmentFromAnotherGraph()
    {
        var fixture = CreateReversePublicationFixture();
        RecordedRenderGraph otherGraph = BuildGraph(
            fixture.Graph.RequestId,
            [
                Fragment(RenderFragmentKind.MaterializedInput, s_bounds, EffectiveScale.At(1), payload: null),
                Fragment(RenderFragmentKind.FilterEffectSegment, s_bounds, EffectiveScale.At(1), payload: null),
                Fragment(RenderFragmentKind.MaterializedInput, s_bounds, EffectiveScale.At(1), payload: null),
                Fragment(RenderFragmentKind.FilterEffectSegment, s_bounds, EffectiveScale.At(1), payload: null),
            ],
            []);

        Assert.That(
            fixture.Plan.TryGetMembership(fixture.Graph, otherGraph.Fragments[3], out _),
            Is.False);
    }

    [Test]
    public void Plan_CreatesIndependentExecutionLedgers()
    {
        var fixture = CreateReversePublicationFixture();
        ExecutionIslandExecutionLedger firstLedger = fixture.Plan.CreateExecutionLedger(fixture.Graph);
        ExecutionIslandExecutionLedger secondLedger = fixture.Plan.CreateExecutionLedger(fixture.Graph);

        ExecutionIsland firstIsland = firstLedger.Begin(fixture.First);
        ExecutionIsland secondIsland = secondLedger.Begin(fixture.First);
        firstLedger.Complete(firstIsland);
        secondLedger.Complete(secondIsland);

        Assert.Multiple(() =>
        {
            Assert.That(() => firstLedger.ValidateCompleted(allowSkippedIslands: true), Throws.Nothing);
            Assert.That(() => secondLedger.ValidateCompleted(allowSkippedIslands: true), Throws.Nothing);
        });
    }

    private static CompiledRenderRequest CompileTerminalOpacity()
        => CompileGraph(
            new RenderRequestOptions(
                RenderIntent.Preview,
                RenderRequestPurpose.Frame,
                targetDomain: s_bounds,
                cachePolicy: RenderCacheOptions.Disabled,
                fusionMode: FusionMode.Enabled),
            requestId =>
            {
                RenderFragmentReference source = Fragment(
                    RenderFragmentKind.MaterializedInput,
                    s_bounds,
                    EffectiveScale.At(1),
                    payload: null);
                ShaderDescription shader = ShaderDescription.CurrentPixel(
                    "half4 apply(half4 color) { return half4(color.bgr, color.a); }");
                RenderFragmentReference stage = Fragment(
                    RenderFragmentKind.Shader,
                    s_bounds,
                    EffectiveScale.Unbounded,
                    new ShaderRenderFragmentPayload(shader),
                    source);
                RenderFragmentReference opacity = Fragment(
                    RenderFragmentKind.Opacity,
                    s_bounds,
                    EffectiveScale.Unbounded,
                    new OpacityRenderFragmentPayload(
                        0.625f,
                        OpacityRenderNode.CreateFusionDescription(0.625f)),
                    stage);
                return BuildGraph(requestId, [source, stage, opacity], [opacity]);
            });

    private static CompiledRenderRequest CompileOpacityOnly()
        => CompileGraph(
            new RenderRequestOptions(
                RenderIntent.Preview,
                RenderRequestPurpose.Frame,
                targetDomain: s_bounds,
                cachePolicy: RenderCacheOptions.Disabled,
                fusionMode: FusionMode.Enabled),
            requestId =>
            {
                RenderFragmentReference source = Fragment(
                    RenderFragmentKind.MaterializedInput,
                    s_bounds,
                    EffectiveScale.At(1),
                    payload: null);
                RenderFragmentReference opacity = Fragment(
                    RenderFragmentKind.Opacity,
                    s_bounds,
                    EffectiveScale.At(1),
                    new OpacityRenderFragmentPayload(
                        0.625f,
                        OpacityRenderNode.CreateFusionDescription(0.625f)),
                    source);
                return BuildGraph(requestId, [source, opacity], [opacity]);
            });

    private static (
        RecordedRenderGraph Graph,
        ExecutionIslandPlan Plan,
        RenderFragmentReference First,
        RenderFragmentReference Second) CreateReversePublicationFixture()
    {
        var requestId = new RenderRequestId(1);
        RenderFragmentReference firstSource = Fragment(
            RenderFragmentKind.MaterializedInput,
            s_bounds,
            EffectiveScale.At(1),
            payload: null);
        RenderFragmentReference first = Fragment(
            RenderFragmentKind.FilterEffectSegment,
            s_bounds,
            EffectiveScale.At(1),
            payload: null,
            firstSource);
        RenderFragmentReference secondSource = Fragment(
            RenderFragmentKind.MaterializedInput,
            s_bounds,
            EffectiveScale.At(1),
            payload: null);
        RenderFragmentReference second = Fragment(
            RenderFragmentKind.FilterEffectSegment,
            s_bounds,
            EffectiveScale.At(1),
            payload: null,
            secondSource);
        ImmutableArray<RenderFragmentReference> roots = [second, first];
        RecordedRenderGraph graph = BuildGraph(
            requestId,
            [firstSource, first, secondSource, second],
            roots);
        var plan = new ExecutionIslandPlan(
            graph.Fragments.Length,
            [
                new ExecutionIsland(
                    0,
                    [1]),
                new ExecutionIsland(
                    1,
                    [3]),
            ]);
        return (graph, plan, first, second);
    }

    private sealed class TerminalOpacityNode(RenderTarget source) : RenderNode
    {
        private static readonly ShaderDescription s_shader = ShaderDescription.CurrentPixel(
            "half4 apply(half4 color) { return half4(color.bgr, color.a); }");

        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle current = BorrowMaterializedInput(context, source, s_bounds);
            current = context.Shader(current, s_shader);
            current = context.Opacity(current, 0.625f);
            context.Publish(current);
        }
    }

    private sealed class OpacityOnlyNode(RenderTarget source) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle current = BorrowMaterializedInput(context, source, s_bounds);
            current = context.Opacity(current, 0.625f);
            context.Publish(current);
        }
    }

    private sealed class DeclaredInputReadbackNode(RenderTarget source) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle current = BorrowMaterializedInput(context, source, s_bounds);
            current = context.OpaqueMap(
                current,
                OpaqueRenderDescription.Create(
                    "opaque-readback",
                    static (session, _) =>
                    {
                        RenderExecutionInput input = session.Inputs.Single();
                        input.UseSnapshot(static _ => { });
                        using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                        output.Canvas.Use(input.Draw);
                        session.Publish(output);
                    },
                    OpaqueRenderBoundsContract.Map(RenderBoundsContract.Identity),
                    RenderHitTestContract.AnyInput,
                    RenderValueCardinality.Single,
                    RenderScaleContract.PreserveInputSupply,
                    inputReadbacks: [RenderInputReadback.All]));
            context.Publish(current);
        }
    }
}
