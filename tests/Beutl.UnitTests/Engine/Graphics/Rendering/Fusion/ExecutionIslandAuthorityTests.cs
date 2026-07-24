using System.Collections.Concurrent;
using System.Collections.Immutable;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Fusion;

[TestFixture]
[NonParallelizable]
public sealed class ExecutionIslandAuthorityTests
{
    private static readonly Rect s_bounds = new(0, 0, 24, 16);

    [Test]
    [Category("GpuPassFusionGpu")]
    public void TerminalOpacity_DispatchesTheCompiledRunBeforeSemanticReplay()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using RenderTarget source = FusionBoundaryExecutionTestSupport.CreatePatternSource(s_bounds);
            using var node = new TerminalOpacityNode(source);
            using var renderer = CreateRenderer(node);

            using RenderNodeRasterization result = renderer.Rasterize();

            Assert.Multiple(() =>
            {
                Assert.That(result.Bitmap, Is.Not.Null);
                Assert.That(FusionBoundaryExecutionTestSupport.SumAbsoluteChannels(result.Bitmap!),
                    Is.GreaterThan(1));
                Assert.That(renderer.LastExecutionStatistics.ShaderRunExecutions, Is.EqualTo(1));
                Assert.That(renderer.LastExecutionStatistics.ShaderStageExecutions, Is.EqualTo(2));
                Assert.That(renderer.LastExecutionStatistics.FusedShaderRunExecutions, Is.EqualTo(1));
            });
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    public void SameBackendCompiledRun_HasNoExecutorManagedFlush()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using RenderTarget source = FusionBoundaryExecutionTestSupport.CreatePatternSource(s_bounds);
            using RenderTarget destination = RenderTarget.Create((int)s_bounds.Width, (int)s_bounds.Height)
                ?? throw new InvalidOperationException("Could not allocate the flush-test destination.");
            using var canvas = new ImmediateCanvas(destination, logicalSize: s_bounds.Size);
            using var node = new TerminalOpacityNode(source);
            using var renderer = CreateRenderer(node);
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
            Assert.That(compiled.ExecutionPlan.TryGetMembership(opacity, out ExecutionIslandMembership membership),
                Is.True);
            Assert.That(membership.Island.Kind, Is.EqualTo(ExecutionIslandKind.Compatibility));
            Assert.That(membership.Island.PlansGpuPass, Is.True);
            Assert.That(compiled.ExecutionPlan.Boundaries,
                Has.Some.Matches<ExecutionIslandBoundary>(static boundary =>
                    boundary.Reason == ExecutionIslandBoundaryReason.SemanticComposite));
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    public void OpacityOnly_RuntimeUsesSemanticReplayWithOneGpuPass()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using RenderTarget source = FusionBoundaryExecutionTestSupport.CreatePatternSource(s_bounds);
            using var node = new OpacityOnlyNode(source);
            var diagnostics = new RenderPipelineDiagnosticsState();
            using var renderer = CreateRenderer(node, diagnostics);

            using RenderNodeRasterization result = renderer.Rasterize();

            Assert.Multiple(() =>
            {
                Assert.That(FusionBoundaryExecutionTestSupport.SumAbsoluteChannels(result.Bitmap!),
                    Is.GreaterThan(1));
                Assert.That(renderer.LastExecutionStatistics.ShaderRunExecutions, Is.Zero);
                Assert.That(renderer.LastExecutionStatistics.ShaderStageExecutions, Is.Zero);
                Assert.That(diagnostics.Latest[RenderPipelineCounter.PlannedGpuPasses], Is.EqualTo(1));
                Assert.That(diagnostics.Latest[RenderPipelineCounter.ExecutedGpuPasses], Is.EqualTo(1));
            });
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    [Category("GpuPassFusionGpu")]
    public void DeclaredInputReadback_IsPlannedAndCountedOnlyAtActualUse(bool opaque)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using RenderTarget source = FusionBoundaryExecutionTestSupport.CreatePatternSource(s_bounds);
            using var node = new DeclaredInputReadbackNode(source, opaque);
            using FusionBoundaryExecutionResult result = FusionBoundaryExecutionTestSupport.Execute(
                node,
                s_bounds,
                FusionMode.Enabled);

            ExecutionIslandBoundaryReason semanticReason = opaque
                ? ExecutionIslandBoundaryReason.Opaque
                : ExecutionIslandBoundaryReason.Geometry;
            Assert.Multiple(() =>
            {
                Assert.That(FusionBoundaryExecutionTestSupport.SumAbsoluteChannels(result.Bitmap),
                    Is.GreaterThan(1));
                Assert.That(result.Statistics.Synchronizations, Is.EqualTo(1));
                Assert.That(result.Diagnostics[RenderPipelineCounter.Synchronizations], Is.EqualTo(1));
                Assert.That(result.Diagnostics[RenderPipelineCounter.PlannedGpuPasses], Is.EqualTo(1));
                Assert.That(result.Diagnostics[RenderPipelineCounter.ExecutedGpuPasses], Is.EqualTo(1));
                Assert.That(result.Diagnostics.Events,
                    Has.Some.Matches<RenderPipelineDiagnosticEvent>(item =>
                        item.Kind == RenderPipelineDiagnosticEventKind.BoundaryPlanned
                        && item.BoundaryReason == RenderPipelineBoundaryReason.Readback));
                Assert.That(result.Diagnostics.Events,
                    Has.Some.Matches<RenderPipelineDiagnosticEvent>(item =>
                        item.Kind == RenderPipelineDiagnosticEventKind.BoundaryPlanned
                        && item.BoundaryReason == (semanticReason == ExecutionIslandBoundaryReason.Geometry
                            ? RenderPipelineBoundaryReason.Geometry
                            : RenderPipelineBoundaryReason.Opaque)));
            });
        });
    }

    [Test]
    public void PlanLedger_RejectsDirectExecutionOfANonTerminalShaderStage()
    {
        using CompiledRenderRequest compiled = CompileTerminalOpacity();
        CompiledShaderRun run = compiled.ExecutionPlan.ShaderRuns.Single();
        RenderFragmentReference interior = Find(compiled.Graph, run.Stages[0].FragmentId);
        ExecutionIslandExecutionLedger ledger = compiled.ExecutionPlan.CreateExecutionLedger(
            compiled.Graph,
            compiled.Roots,
            compiled.CacheResolution);

        Assert.That(
            () => ledger.Begin(interior),
            Throws.InvalidOperationException.With.Message.Contains("non-terminal"));
    }

    [Test]
    public void PlanLedger_RejectsDuplicateIslandExecution()
    {
        using CompiledRenderRequest compiled = CompileTerminalOpacity();
        CompiledShaderRun run = compiled.ExecutionPlan.ShaderRuns.Single();
        ExecutionIslandExecutionLedger ledger = compiled.ExecutionPlan.CreateExecutionLedger(
            compiled.Graph,
            compiled.Roots,
            compiled.CacheResolution);

        ExecutionIsland island = ledger.Begin(run.Output);
        ledger.Complete(island);

        Assert.That(
            () => ledger.Begin(run.Output),
            Throws.InvalidOperationException.With.Message.Contains("more than once"));
    }

    [Test]
    public void PlanLedger_RejectsAReachableExecutableFragmentMissingFromThePlan()
    {
        using CompiledRenderRequest compiled = CompileTerminalOpacity();
        var invalid = new ExecutionIslandPlan([], compiled.ExecutionPlan.Boundaries);

        Assert.That(
            () => invalid.CreateExecutionLedger(
                compiled.Graph,
                compiled.Roots,
                compiled.CacheResolution),
            Throws.InvalidOperationException.With.Message.Contains("not assigned"));
    }

    [Test]
    public void Plan_RejectsOneFragmentAssignedToMultipleIslands()
    {
        var requestId = new RenderRequestId(1);
        var fragmentId = new RenderFragmentId(requestId, 1);

        Assert.That(
            () => new ExecutionIslandPlan(
            [
                new ExecutionIsland(
                    new ExecutionIslandId(1),
                    ExecutionIslandKind.Compatibility,
                    [fragmentId],
                    plansGpuPass: false),
                new ExecutionIsland(
                    new ExecutionIslandId(2),
                    ExecutionIslandKind.Compatibility,
                    [fragmentId],
                    plansGpuPass: false),
            ],
            []),
            Throws.ArgumentException.With.Message.Contains("more than one execution island"));
    }

    [Test]
    public void PlanLedger_UsesPublicationOrderInsteadOfAuthoredIslandOrder()
    {
        var fixture = CreateReversePublicationFixture();
        ExecutionIslandExecutionLedger ledger = fixture.Plan.CreateExecutionLedger(
            fixture.Graph,
            fixture.Roots,
            new RenderCacheResolution([]));

        ExecutionIsland second = ledger.Begin(fixture.Second);
        ledger.Complete(second);
        ExecutionIsland first = ledger.Begin(fixture.First);
        ledger.Complete(first);

        Assert.That(() => ledger.ValidateCompleted(), Throws.Nothing);

        ExecutionIslandExecutionLedger reversed = fixture.Plan.CreateExecutionLedger(
            fixture.Graph,
            fixture.Roots,
            new RenderCacheResolution([]));
        ExecutionIsland authoredFirst = reversed.Begin(fixture.First);
        reversed.Complete(authoredFirst);
        ExecutionIsland authoredSecond = reversed.Begin(fixture.Second);
        Assert.That(
            () => reversed.Complete(authoredSecond),
            Throws.InvalidOperationException.With.Message.Contains("painter order"));
    }

    [Test]
    public void PlanLedger_VisitsOpacityMaskDependenciesBeforePrimaryReplay()
    {
        var requestId = new RenderRequestId(1);
        RenderFragmentReference primarySource = Fragment(
            RenderFragmentKind.MaterializedInput,
            EffectiveScale.At(1),
            payload: null);
        RenderFragmentReference primary = Fragment(
            RenderFragmentKind.Geometry,
            EffectiveScale.At(1),
            payload: null,
            primarySource);
        RenderFragmentReference maskSource = Fragment(
            RenderFragmentKind.MaterializedInput,
            EffectiveScale.At(1),
            payload: null);
        RenderFragmentReference maskDependency = Fragment(
            RenderFragmentKind.Geometry,
            EffectiveScale.At(1),
            payload: null,
            maskSource);
        RenderFragmentReference opacityMask = Fragment(
            RenderFragmentKind.OpacityMask,
            EffectiveScale.At(1),
            payload: null,
            primary,
            maskDependency);
        ImmutableArray<RenderFragmentReference> roots = [opacityMask];
        RecordedRenderGraph graph = BuildGraph(
            requestId,
            [primarySource, primary, maskSource, maskDependency, opacityMask],
            roots);
        var plan = new ExecutionIslandPlan(
            [
                new ExecutionIsland(
                    new ExecutionIslandId(1),
                    ExecutionIslandKind.Compatibility,
                    [primary.Id!.Value],
                    plansGpuPass: true),
                new ExecutionIsland(
                    new ExecutionIslandId(2),
                    ExecutionIslandKind.Compatibility,
                    [maskDependency.Id!.Value],
                    plansGpuPass: true),
                new ExecutionIsland(
                    new ExecutionIslandId(3),
                    ExecutionIslandKind.Compatibility,
                    [opacityMask.Id!.Value],
                    plansGpuPass: true),
            ],
            []);
        ExecutionIslandExecutionLedger ledger = plan.CreateExecutionLedger(
            graph,
            roots,
            new RenderCacheResolution([]));

        ExecutionIsland dependencyIsland = ledger.Begin(maskDependency);
        ledger.Complete(dependencyIsland);
        ExecutionIsland primaryIsland = ledger.Begin(primary);
        ledger.Complete(primaryIsland);
        ExecutionIsland maskIsland = ledger.Begin(opacityMask);
        ledger.Complete(maskIsland);

        Assert.That(() => ledger.ValidateCompleted(), Throws.Nothing);
    }

    [Test]
    public void PlanLedger_RejectsIncompleteSuccessfulExecution()
    {
        var fixture = CreateReversePublicationFixture();
        ExecutionIslandExecutionLedger ledger = fixture.Plan.CreateExecutionLedger(
            fixture.Graph,
            fixture.Roots,
            new RenderCacheResolution([]));

        ExecutionIsland second = ledger.Begin(fixture.Second);
        ledger.Complete(second);

        Assert.That(
            () => ledger.ValidateCompleted(),
            Throws.InvalidOperationException.With.Message.Contains("must complete"));
    }

    private static RenderNodeRenderer CreateRenderer(
        RenderNode node,
        IRenderPipelineDiagnosticsState? diagnostics = null)
        => new(
            node,
            new RenderNodeRendererOptions
            {
                TargetDomain = s_bounds,
                OutputScale = 1,
                MaxWorkingScale = 1,
                UseRenderCache = false,
                FusionMode = FusionMode.Enabled,
                RenderPurpose = RenderRequestPurpose.Frame,
                Diagnostics = diagnostics,
            });

    private static CompiledRenderRequest CompileTerminalOpacity()
    {
        var options = new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Frame,
            targetDomain: s_bounds,
            cachePolicy: RenderCacheOptions.Disabled,
            fusionMode: FusionMode.Enabled);
        var request = new RenderRequest(options);
        RenderFragmentReference source = Fragment(
            RenderFragmentKind.MaterializedInput,
            EffectiveScale.At(1),
            payload: null);
        ShaderDescription shader = ShaderDescription.CurrentPixel(
            "half4 apply(half4 color) { return half4(color.bgr, color.a); }");
        RenderFragmentReference stage = Fragment(
            RenderFragmentKind.Shader,
            EffectiveScale.Unbounded,
            new ShaderRenderFragmentPayload(shader, shader.CreateRuntimeIdentity()),
            source);
        RenderFragmentReference opacity = Fragment(
            RenderFragmentKind.Opacity,
            EffectiveScale.Unbounded,
            new OpacityRenderFragmentPayload(
                0.625f,
                OpacityRenderNode.CreateFusionDescription(0.625f)),
            stage);
        RecordedRenderGraph graph = BuildGraph(request.Id, [source, stage, opacity], [opacity]);
        request.TransitionTo(RenderRequestState.Recording);
        request.TransitionTo(RenderRequestState.Recorded);
        return new RenderRequestCompiler().Compile(request, graph);
    }

    private static CompiledRenderRequest CompileOpacityOnly()
    {
        var options = new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Frame,
            targetDomain: s_bounds,
            cachePolicy: RenderCacheOptions.Disabled,
            fusionMode: FusionMode.Enabled);
        var request = new RenderRequest(options);
        RenderFragmentReference source = Fragment(
            RenderFragmentKind.MaterializedInput,
            EffectiveScale.At(1),
            payload: null);
        RenderFragmentReference opacity = Fragment(
            RenderFragmentKind.Opacity,
            EffectiveScale.At(1),
            new OpacityRenderFragmentPayload(
                0.625f,
                OpacityRenderNode.CreateFusionDescription(0.625f)),
            source);
        RecordedRenderGraph graph = BuildGraph(request.Id, [source, opacity], [opacity]);
        request.TransitionTo(RenderRequestState.Recording);
        request.TransitionTo(RenderRequestState.Recorded);
        return new RenderRequestCompiler().Compile(request, graph);
    }

    private static (
        RecordedRenderGraph Graph,
        ImmutableArray<RenderFragmentReference> Roots,
        ExecutionIslandPlan Plan,
        RenderFragmentReference First,
        RenderFragmentReference Second) CreateReversePublicationFixture()
    {
        var requestId = new RenderRequestId(1);
        RenderFragmentReference firstSource = Fragment(
            RenderFragmentKind.MaterializedInput,
            EffectiveScale.At(1),
            payload: null);
        RenderFragmentReference first = Fragment(
            RenderFragmentKind.Geometry,
            EffectiveScale.At(1),
            payload: null,
            firstSource);
        RenderFragmentReference secondSource = Fragment(
            RenderFragmentKind.MaterializedInput,
            EffectiveScale.At(1),
            payload: null);
        RenderFragmentReference second = Fragment(
            RenderFragmentKind.Geometry,
            EffectiveScale.At(1),
            payload: null,
            secondSource);
        ImmutableArray<RenderFragmentReference> roots = [second, first];
        RecordedRenderGraph graph = BuildGraph(
            requestId,
            [firstSource, first, secondSource, second],
            roots);
        var plan = new ExecutionIslandPlan(
            [
                new ExecutionIsland(
                    new ExecutionIslandId(1),
                    ExecutionIslandKind.Compatibility,
                    [first.Id!.Value],
                    plansGpuPass: true),
                new ExecutionIsland(
                    new ExecutionIslandId(2),
                    ExecutionIslandKind.Compatibility,
                    [second.Id!.Value],
                    plansGpuPass: true),
            ],
            []);
        return (graph, roots, plan, first, second);
    }

    private static RenderFragmentReference Find(RecordedRenderGraph graph, RenderFragmentId id)
        => (RenderFragmentReference)graph.Fragments.Single(fragment => fragment.Id == id).Payload!;

    private static RenderFragmentReference Fragment(
        RenderFragmentKind kind,
        EffectiveScale scale,
        object? payload,
        params RenderFragmentReference[] inputs)
        => new(
            kind,
            s_bounds,
            scale,
            RenderValueCardinality.Single,
            contributesValuesToTarget: true,
            canBeUsedAsValueInput: true,
            hasTargetEffects: false,
            hasOpaqueExternalWork: false,
            inputs,
            payload,
            static _ => true);

    private static RecordedRenderGraph BuildGraph(
        RenderRequestId requestId,
        IReadOnlyList<RenderFragmentReference> references,
        IReadOnlyList<RenderFragmentReference> roots)
    {
        var builder = new RecordedRenderGraphBuilder(requestId);
        RenderProvenanceId provenance = builder.AddProvenance(
            typeof(ExecutionIslandAuthorityTests),
            "execution-island-authority-test");
        foreach (RenderFragmentReference reference in references)
        {
            RenderValueId[] inputs = reference.Inputs.SelectMany(static input => input.ValueIds).ToArray();
            reference.ValueIds = [builder.AddValue(inputs, provenance, reference)];
            reference.Id = builder.AddFragment(reference.ValueIds, provenance, reference);
        }

        foreach (RenderFragmentReference root in roots)
            builder.PublishRoot(root.Id!.Value);
        return builder.Build();
    }

    private sealed class TerminalOpacityNode(RenderTarget source) : RenderNode
    {
        private static readonly ShaderDescription s_shader = ShaderDescription.CurrentPixel(
            "half4 apply(half4 color) { return half4(color.bgr, color.a); }");

        public override void Process(RenderNodeContext context)
        {
            RenderResource<RenderTarget> resource = context.Borrow(
                source,
                (typeof(TerminalOpacityNode), "source"),
                version: 1);
            RenderFragmentHandle current = context.MaterializedInput(
                MaterializedInputDescription.FromRenderTarget(
                    resource,
                    s_bounds,
                    EffectiveScale.At(1),
                    RenderHitTestContract.OutputBounds));
            current = context.Shader(current, s_shader);
            current = context.Opacity(current, 0.625f);
            context.Publish(current);
        }
    }

    private sealed class OpacityOnlyNode(RenderTarget source) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            RenderResource<RenderTarget> resource = context.Borrow(
                source,
                (typeof(OpacityOnlyNode), "source"),
                version: 1);
            RenderFragmentHandle current = context.MaterializedInput(
                MaterializedInputDescription.FromRenderTarget(
                    resource,
                    s_bounds,
                    EffectiveScale.At(1),
                    RenderHitTestContract.OutputBounds));
            current = context.Opacity(current, 0.625f);
            context.Publish(current);
        }
    }

    private sealed class DeclaredInputReadbackNode(RenderTarget source, bool opaque) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            RenderResource<RenderTarget> resource = context.Borrow(
                source,
                (typeof(DeclaredInputReadbackNode), opaque, "source"),
                version: 1);
            RenderFragmentHandle current = context.MaterializedInput(
                MaterializedInputDescription.FromRenderTarget(
                    resource,
                    s_bounds,
                    EffectiveScale.At(1),
                    RenderHitTestContract.OutputBounds));
            current = opaque
                ? context.OpaqueMap(
                    current,
                    OpaqueRenderDescription.Create(
                        static session =>
                        {
                            RenderExecutionInput input = session.Inputs.Single();
                            input.UseSnapshot(static _ => { });
                            using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                            output.Canvas.Use(input.Draw);
                            session.Publish(output);
                        },
                        RenderOperationBoundsContract.Map(RenderBoundsContract.Identity),
                        RenderHitTestContract.AnyInput,
                        RenderValueCardinality.Single,
                        RenderScaleContract.PreserveInputSupply,
                        structuralKey: (typeof(DeclaredInputReadbackNode), "opaque"),
                        runtimeIdentity: new RenderRuntimeIdentity("opaque-readback"),
                        requiresReadback: true))
                : context.Geometry(
                    current,
                    GeometryDescription.Create(
                        static session =>
                        {
                            session.Input.UseSnapshot(static _ => { });
                            session.Canvas.Use(session.Input.Draw);
                        },
                        RenderBoundsContract.Identity,
                        RenderHitTestContract.AnyInput,
                        structuralKey: (typeof(DeclaredInputReadbackNode), "geometry"),
                        runtimeIdentity: new RenderRuntimeIdentity("geometry-readback"),
                        requiresReadback: true));
            context.Publish(current);
        }
    }
}
