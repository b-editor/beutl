using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Graphics.Shaders;
using Beutl.UnitTests.Engine.Graphics.Backend;
using Beutl.UnitTests.Engine.Graphics.Rendering.Golden;
using static Beutl.UnitTests.Engine.Graphics.Rendering.Fusion.FusionBoundaryExecutionTestSupport;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Fusion;

[TestFixture]
[NonParallelizable]
public sealed class FusionBoundaryTests
{
    private static readonly Rect s_bounds = new(0, 0, 24, 16);

    [Test]
    public void AntialiasedThinStroke_NonlinearShaderPreservesCoverageAtTheExactBoundary()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var node = new AntialiasedCoverageBoundaryNode(s_bounds);
            using FusionBoundaryExecutionResult disabled = FusionBoundaryExecutionTestSupport.Execute(
                node,
                s_bounds,
                FusionMode.Disabled);
            using FusionBoundaryExecutionResult enabled = FusionBoundaryExecutionTestSupport.Execute(
                node,
                s_bounds,
                FusionMode.Enabled);

            RgbaMaximumError maximum = ImageMetrics.EdgeBandMaximumAbsoluteErrorPerChannel(
                disabled.Bitmap,
                enabled.Bitmap);
            Assert.Multiple(() =>
            {
                Assert.That(FusionBoundaryExecutionTestSupport.CountFractionalAlphaPixels(enabled.Bitmap),
                    Is.GreaterThan(0), "The control must contain antialiased fractional-coverage edge pixels.");
                Assert.That(FusionBoundaryExecutionTestSupport.SumAbsoluteChannels(enabled.Bitmap), Is.GreaterThan(1));
                Assert.That(maximum.Maximum, Is.LessThanOrEqualTo(0.02));
                Assert.That(enabled.Statistics.ShaderRunExecutions, Is.EqualTo(1));
                Assert.That(enabled.Statistics.FusedShaderRunExecutions, Is.Zero);
                Assert.That(enabled.Statistics.IntermediateTargetAcquisitions, Is.EqualTo(1));
            });
        });
    }


    [Test]
    public void StandaloneBackendOverflow_ExecutesCompatibilityPathWithParityAndExactReason()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using RenderTarget source = FusionBoundaryExecutionTestSupport.CreatePatternSource(s_bounds);
            using var node = new BackendOverflowBoundaryNode(source, s_bounds);
            SkslBackendBudget budget = Budget(
                capabilityClass: (typeof(FusionBoundaryTests), "runtime-standalone-overflow"),
                maxUniformVectors: 0);
            using FusionBoundaryExecutionResult disabled = FusionBoundaryExecutionTestSupport.ExecuteWithBudget(
                node,
                s_bounds,
                FusionMode.Disabled,
                budget);
            using FusionBoundaryExecutionResult enabled = FusionBoundaryExecutionTestSupport.ExecuteWithBudget(
                node,
                s_bounds,
                FusionMode.Enabled,
                budget);

            RgbaMaximumError maximum = ImageMetrics.MaximumAbsoluteErrorPerChannel(
                disabled.Bitmap,
                enabled.Bitmap);
            Assert.Multiple(() =>
            {
                Assert.That(FusionBoundaryExecutionTestSupport.SumAbsoluteChannels(enabled.Bitmap), Is.GreaterThan(1));
                Assert.That(maximum.Maximum, Is.LessThanOrEqualTo(0.02));
                Assert.That(enabled.Statistics.ShaderRunExecutions, Is.Zero);
                Assert.That(disabled.Statistics.ShaderRunExecutions, Is.Zero);
            });
        });
    }

    [Test]
    public void NonlinearCurrentPixel_AfterCoverageProducerRequiresMaterializationBoundary()
    {
        using CompiledRenderRequest compiled = Compile((requestId, cache) =>
        {
            RenderFragmentReference source = Fragment(
                RenderFragmentKind.OpaqueSource,
                OpaquePayload(
                    OpaqueRenderTopology.Source,
                    RenderValueCardinality.Single));
            ShaderDescription nonlinear = ShaderDescription.CurrentPixel(
                "half4 apply(half4 color) { return color * color.a; }");
            RenderFragmentReference shader = Fragment(
                RenderFragmentKind.Shader,
                new ShaderRenderFragmentPayload(nonlinear),
                source);
            return BuildGraph(requestId, [source, shader], [shader], cache);
        });

        CompiledShaderRun run = compiled.ExecutionPlan.ShaderRuns.Single();
        Assert.Multiple(() =>
        {
            Assert.That(SemanticIslandFragments(compiled), Is.EqualTo(new[] { 0 }));
            Assert.That(run.GetInput(compiled.Graph), Is.SameAs(compiled.Graph.Fragments[0]));
        });
    }

    [Test]
    public void ScopeMetadataMismatch_RemainsAnExplicitCompatibilityBoundary()
    {
        using CompiledRenderRequest compiled = Compile((requestId, cache) =>
        {
            RenderFragmentReference source = Fragment(RenderFragmentKind.MaterializedInput, payload: null);
            RenderFragmentReference first = CurrentPixel(source, "return color * 0.75;");
            ShaderDescription description = ShaderDescription.CurrentPixel(
                "half4 apply(half4 color) { return color.bgra; }");
            var mismatched = new RenderFragmentReference(
                RenderFragmentKind.Shader,
                s_bounds,
                EffectiveScale.Unbounded,
                RenderValueCardinality.Single,
                contributesValuesToTarget: true,
                canBeUsedAsValueInput: true,
                hasTargetEffects: true,
                hasOpaqueExternalWork: false,
                [first],
                new ShaderRenderFragmentPayload(description),
                RenderFragmentHitTest.Bounds);
            return BuildGraph(requestId, [source, first, mismatched], [mismatched], cache);
        });

        Assert.Multiple(() =>
        {
            Assert.That(compiled.ExecutionPlan.ShaderRuns, Has.Exactly(1).Items);
            Assert.That(compiled.ExecutionPlan.ShaderRuns.Single().GetOutput(compiled.Graph),
                Is.SameAs(compiled.Graph.Fragments[1]));
            Assert.That(SemanticIslandFragments(compiled), Is.EqualTo(new[] { 2 }));
        });
    }

    [Test]
    public void TargetCaptureRemainsAnExplicitBarrier()
    {
        AssertBarrier(RenderFragmentKind.TargetCapture, TargetCapturePayload());
    }

    [Test]
    public void WholeSourceStagesStartRunsAndNeverBecomeSuccessors()
    {
        using CompiledRenderRequest compiled = Compile((requestId, cache) =>
        {
            RenderFragmentReference source = Fragment(RenderFragmentKind.MaterializedInput, payload: null);
            RenderFragmentReference first = Fragment(RenderFragmentKind.Shader, WholeSourcePayload(), source);
            RenderFragmentReference second = Fragment(RenderFragmentKind.Shader, WholeSourcePayload(), first);
            RenderFragmentReference currentPixel = CurrentPixel(second, "return color * 0.5;");
            return BuildGraph(requestId, [source, first, second, currentPixel], [currentPixel], cache);
        });

        CompiledShaderRun[] runs = compiled.ExecutionPlan.ShaderRuns.ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(runs.Select(static run => run.StageFragmentIndices.Length), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(runs.All(run => run.GetWholeSourceHead(compiled.Graph) is not null), Is.True);
            Assert.That(runs.All(run =>
                run.GetDescription(compiled.Graph, 0).Kind == ShaderDescriptionKind.WholeSource
                && Enumerable.Range(1, run.StageFragmentIndices.Length - 1)
                    .All(index => run.GetDescription(compiled.Graph, index).Kind
                        == ShaderDescriptionKind.CurrentPixel)), Is.True);
            Assert.That(runs[1].GetInput(compiled.Graph), Is.SameAs(runs[0].GetOutput(compiled.Graph)));
        });
    }

    [Test]
    public void FusionDisabled_KeepsWholeSourceInACompatibilityIsland()
    {
        using CompiledRenderRequest compiled = Compile((requestId, cache) =>
        {
            RenderFragmentReference source = Fragment(RenderFragmentKind.MaterializedInput, payload: null);
            RenderFragmentReference wholeSource = Fragment(
                RenderFragmentKind.Shader,
                WholeSourcePayload(),
                source);
            RenderFragmentReference currentPixel = CurrentPixel(wholeSource, "return color * 0.5;");
            return BuildGraph(requestId, [source, wholeSource, currentPixel], [currentPixel], cache);
        }, fusionMode: FusionMode.Disabled);

        Assert.Multiple(() =>
        {
            Assert.That(compiled.ExecutionPlan.Islands.Select(static island => island.ShaderRun is not null),
                Is.EqualTo(new[] { false, true }));
            Assert.That(compiled.ExecutionPlan.ShaderRuns.Single().StageFragmentIndices, Has.Length.EqualTo(1));
            Assert.That(compiled.ExecutionPlan.ShaderRuns.Single().GetWholeSourceHead(compiled.Graph), Is.Null);
            Assert.That(SemanticIslandFragments(compiled), Is.EqualTo(new[] { 1 }));
        });
    }


    [Test]
    public void BypassedCacheCandidate_DoesNotSplitOtherwiseCompatibleRun()
    {
        using CompiledRenderRequest compiled = Compile((requestId, cache) =>
        {
            RenderFragmentReference source = Fragment(RenderFragmentKind.MaterializedInput, payload: null);
            RenderFragmentReference first = CurrentPixel(source, "return color * 0.75;");
            RenderFragmentReference second = CurrentPixel(first, "return half4(color.bgr, color.a);");
            cache.Add(first);
            return BuildGraph(requestId, [source, first, second], [second], cache);
        });

        Assert.That(compiled.ExecutionPlan.ShaderRuns.Select(static run => run.StageFragmentIndices.Length),
            Is.EqualTo(new[] { 2 }));
    }

    [Test]
    public void BackendStageBudget_SplitsBeforeOverflowAndPreservesOrderDeterministically()
    {
        SkslBackendBudget budget = Budget(capabilityClass: (typeof(FusionBoundaryTests), 2), maxStages: 2);
        using CompiledRenderRequest first = Compile(FiveStageGraph, budget: budget);
        using CompiledRenderRequest second = Compile(FiveStageGraph, budget: budget);

        CompiledShaderRun[] firstRuns = first.ExecutionPlan.ShaderRuns.ToArray();
        CompiledShaderRun[] secondRuns = second.ExecutionPlan.ShaderRuns.ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(firstRuns.Select(static run => run.StageFragmentIndices.Length),
                Is.EqualTo(new[] { 2, 2, 1 }));
            Assert.That(firstRuns.SelectMany(run => Enumerable.Range(0, run.StageFragmentIndices.Length)
                    .Select(index => run.GetDescription(first.Graph, index).Source.Text)),
                Is.EqualTo(secondRuns.SelectMany(run => Enumerable.Range(0, run.StageFragmentIndices.Length)
                    .Select(index => run.GetDescription(second.Graph, index).Source.Text))));
        });
    }

    [Test]
    public void DefaultCompiler_UsesFinitePortableBudgetAndSplitsBeforeOverflow()
    {
        SkslBackendBudget budget = SkslBackendBudgetResolver.Portable;
        using CompiledRenderRequest compiled = Compile(
            (requestId, cache) => StageGraph(requestId, cache, budget.MaxStages + 1),
            budget: budget);

        CompiledShaderRun[] runs = compiled.ExecutionPlan.ShaderRuns.ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(runs.Select(static run => run.StageFragmentIndices.Length),
                Is.EqualTo(new[] { budget.MaxStages, 1 }));
            Assert.That(runs, Has.All.Matches<CompiledShaderRun>(
                run => run.Program.Budget.Equals(budget)));
        });
    }

    [Test]
    public void StandaloneBackendOverflow_RunsTheShaderInItsOwnSemanticIsland()
    {
        SkslBackendBudget budget = Budget(
            capabilityClass: (typeof(FusionBoundaryTests), "standalone-uniform-overflow"),
            maxUniformVectors: 0);
        using CompiledRenderRequest compiled = Compile((requestId, cache) =>
        {
            RenderFragmentReference source = Fragment(RenderFragmentKind.MaterializedInput, payload: null);
            ShaderDescription description = ShaderDescription.CurrentPixel(
                "uniform float gain; half4 apply(half4 color) { return color * gain; }",
                static bindings => bindings.Uniform("gain", 0.5f));
            RenderFragmentReference shader = Fragment(
                RenderFragmentKind.Shader,
                new ShaderRenderFragmentPayload(description),
                source);
            return BuildGraph(requestId, [source, shader], [shader], cache);
        }, budget: budget);

        Assert.Multiple(() =>
        {
            Assert.That(compiled.ExecutionPlan.ShaderRuns, Is.Empty);
            Assert.That(compiled.ExecutionPlan.Islands, Has.Exactly(1).Items);
            Assert.That(SemanticIslandFragments(compiled), Is.EqualTo(new[] { 1 }));
        });
    }

    [Test]
    public void PortableResourceOverflow_UsesSingleCompatibilityFallbackAtCurrentBudget()
    {
        using var registry = new RenderRequestResourceRegistry();
        SkslBackendBudget budget = SkslBackendBudgetResolver.Portable;
        ShaderDescription description = ResourceHeavyShader(budget.MaxSamplers, registry);
        using CompiledRenderRequest compiled = Compile((requestId, cache) =>
        {
            RenderFragmentReference source = Fragment(RenderFragmentKind.MaterializedInput, payload: null);
            RenderFragmentReference shader = Fragment(
                RenderFragmentKind.Shader,
                new ShaderRenderFragmentPayload(description),
                source);
            return BuildGraph(requestId, [source, shader], [shader], cache);
        }, budget: budget);

        Assert.Multiple(() =>
        {
            Assert.That(compiled.ExecutionPlan.ShaderRuns, Is.Empty);
            Assert.That(compiled.ExecutionPlan.Islands, Has.Exactly(1).Items);
            Assert.That(compiled.ExecutionPlan.Islands[0].ShaderRun, Is.Null);
        });
    }

    [Test]
    public void DynamicCardinalityAndGroupOpacityDoNotClaimShaderEligibility()
    {
        using CompiledRenderRequest dynamic = Compile((requestId, cache) =>
        {
            RenderFragmentReference source = Fragment(
                RenderFragmentKind.OpaqueExpand,
                OpaquePayload(
                    OpaqueRenderTopology.Expand,
                    RenderValueCardinality.Dynamic),
                cardinality: RenderValueCardinality.Dynamic);
            ShaderDescription description = ShaderDescription.CurrentPixel(
                "half4 apply(half4 color) { return color; }");
            RenderFragmentReference shader = Fragment(
                RenderFragmentKind.Shader,
                new ShaderRenderFragmentPayload(description),
                RenderValueCardinality.Dynamic,
                source);
            return BuildGraph(requestId, [source, shader], [shader], cache);
        });
        using CompiledRenderRequest groupOpacity = Compile((requestId, cache) =>
        {
            RenderFragmentReference source = Fragment(
                RenderFragmentKind.OpaqueExpand,
                OpaquePayload(
                    OpaqueRenderTopology.Expand,
                    RenderValueCardinality.Exactly(2)),
                cardinality: RenderValueCardinality.Exactly(2));
            RenderFragmentReference opacity = Fragment(
                RenderFragmentKind.Opacity,
                new OpacityRenderFragmentPayload(0.5f, OpacityRenderNode.CreateFusionDescription(0.5f)),
                RenderValueCardinality.Exactly(2),
                source);
            return BuildGraph(requestId, [source, opacity], [opacity], cache);
        });

        Assert.Multiple(() =>
        {
            Assert.That(dynamic.ExecutionPlan.ShaderRuns, Is.Empty);
            Assert.That(SemanticIslandFragments(dynamic), Is.EqualTo(new[] { 0, 1 }));
            Assert.That(groupOpacity.ExecutionPlan.ShaderRuns, Is.Empty,
                "Group opacity over multiple values is not equivalent to per-value color multiplication.");
            Assert.That(SemanticIslandFragments(groupOpacity), Is.EqualTo(new[] { 0, 1 }));
        });
    }

    [Test]
    public void ZeroOrOneInput_CanStartAShaderRunWithoutABackendBoundary()
    {
        using CompiledRenderRequest compiled = Compile((requestId, cache) =>
        {
            RenderFragmentReference source = Fragment(
                RenderFragmentKind.OpaqueSource,
                OpaquePayload(
                    OpaqueRenderTopology.Source,
                    RenderValueCardinality.ZeroOrOne),
                cardinality: RenderValueCardinality.ZeroOrOne);
            RenderFragmentReference shader = Fragment(
                RenderFragmentKind.Shader,
                new ShaderRenderFragmentPayload(ShaderDescription.CurrentPixel(
                    "half4 apply(half4 color) { return color; }")),
                RenderValueCardinality.ZeroOrOne,
                source);
            return BuildGraph(requestId, [source, shader], [shader], cache);
        });

        Assert.That(compiled.ExecutionPlan.ShaderRuns, Has.Exactly(1).Items);
    }

    private static void AssertBarrier(RenderFragmentKind barrierKind, object? payload)
    {
        using CompiledRenderRequest compiled = Compile((requestId, cache) =>
        {
            RenderFragmentReference source = Fragment(RenderFragmentKind.MaterializedInput, payload: null);
            RenderFragmentReference barrier = Fragment(barrierKind, payload, source);
            RenderFragmentReference shader = CurrentPixel(barrier, "return color * color.a;");
            return BuildGraph(requestId, [source, barrier, shader], [shader], cache);
        });

        Assert.Multiple(() =>
        {
            Assert.That(compiled.ExecutionPlan.ShaderRuns, Has.Exactly(1).Items);
            Assert.That(SemanticIslandFragments(compiled), Is.EqualTo(new[] { 1 }));
            Assert.That(compiled.ExecutionPlan.ShaderRuns.Single().GetInput(compiled.Graph),
                Is.SameAs(compiled.Graph.Fragments[1]));
        });
    }

    // The fragment of each island that runs without a compiled shader run, in plan order.
    private static int[] SemanticIslandFragments(CompiledRenderRequest compiled)
        => [.. compiled.ExecutionPlan.Islands
            .Where(static island => island.ShaderRun is null)
            .Select(static island => island.FragmentIndices.Single())];

    private static RecordedRenderGraph FiveStageGraph(
        RenderRequestId requestId,
        HashSet<RenderFragmentReference> cache)
        => StageGraph(requestId, cache, 5);

    private static RecordedRenderGraph StageGraph(
        RenderRequestId requestId,
        HashSet<RenderFragmentReference> cache,
        int stageCount)
    {
        RenderFragmentReference source = Fragment(RenderFragmentKind.MaterializedInput, payload: null);
        var references = new List<RenderFragmentReference> { source };
        RenderFragmentReference current = source;
        for (int index = 0; index < stageCount; index++)
        {
            current = CurrentPixel(current, $"return color * {index + 1}.0;");
            references.Add(current);
        }
        return BuildGraph(requestId, references, [current], cache);
    }

    private static object WholeSourcePayload()
    {
        ShaderDescription description = ShaderDescription.WholeSource(
            "uniform shader src; half4 main(float2 coord) { return src.eval(coord); }",
            RenderBoundsContract.Identity);
        return new ShaderRenderFragmentPayload(description);
    }

    private static object TargetCapturePayload()
    {
        TargetCaptureDescription description = TargetCaptureDescription.Create(
            TargetRegion.Full,
            s_bounds,
            RenderHitTestContract.None,
            TargetCaptureScaleContract.MaterializeAtWorkingScale);
        return new TargetCaptureRenderFragmentPayload(description);
    }

    private static RenderFragmentReference CurrentPixel(
        RenderFragmentReference input,
        string body)
    {
        ShaderDescription description = ShaderDescription.CurrentPixel(
            $"half4 apply(half4 color) {{ {body} }}");
        return Fragment(
            RenderFragmentKind.Shader,
            new ShaderRenderFragmentPayload(description),
            input);
    }

    private static OpaqueRenderFragmentPayload OpaquePayload(
        OpaqueRenderTopology topology,
        RenderValueCardinality cardinality)
    {
        OpaqueRenderBoundsContract bounds = topology switch
        {
            OpaqueRenderTopology.Source => OpaqueRenderBoundsContract.Source(s_bounds),
            OpaqueRenderTopology.Map => OpaqueRenderBoundsContract.Map(RenderBoundsContract.Identity),
            OpaqueRenderTopology.Combine or OpaqueRenderTopology.Expand
                => OpaqueRenderBoundsContract.FullInputs(static _ => s_bounds),
            _ => throw new ArgumentOutOfRangeException(nameof(topology)),
        };
        OpaqueRenderDescription description = OpaqueRenderDescription.CreateRequestLocal(
            static _ => { },
            bounds,
            RenderHitTestContract.OutputBounds,
            cardinality,
            RenderScaleContract.MaterializeAtWorkingScale);
        IReadOnlyList<RenderInputReadback> inputReadbacks = topology == OpaqueRenderTopology.Map
            ? [RenderInputReadback.None]
            : Array.Empty<RenderInputReadback>();
        return new OpaqueRenderFragmentPayload(topology, description, inputReadbacks);
    }

    private static RenderFragmentReference Fragment(
        RenderFragmentKind kind,
        object? payload,
        params RenderFragmentReference[] inputs)
        => Fragment(kind, payload, RenderValueCardinality.Single, inputs);

    private static RenderFragmentReference Fragment(
        RenderFragmentKind kind,
        object? payload,
        RenderValueCardinality cardinality,
        params RenderFragmentReference[] inputs)
    {
        return new RenderFragmentReference(
            kind,
            s_bounds,
            kind == RenderFragmentKind.MaterializedInput ? EffectiveScale.At(1) : EffectiveScale.Unbounded,
            cardinality,
            contributesValuesToTarget: true,
            canBeUsedAsValueInput: true,
            hasTargetEffects: kind == RenderFragmentKind.TargetCapture
                || inputs.Any(static input => input.HasTargetEffects),
            hasOpaqueExternalWork: kind is RenderFragmentKind.OpaqueSource
                    or RenderFragmentKind.OpaqueMap
                    or RenderFragmentKind.OpaqueCombine
                    or RenderFragmentKind.OpaqueExpand
                || inputs.Any(static input => input.HasOpaqueExternalWork),
            [.. inputs],
            payload,
            RenderFragmentHitTest.Bounds);
    }

    private static CompiledRenderRequest Compile(
        Func<RenderRequestId, HashSet<RenderFragmentReference>, RecordedRenderGraph> createGraph,
        FusionMode fusionMode = FusionMode.Enabled,
        SkslBackendBudget? budget = null)
        => CompileGraph(
            new RenderRequestOptions(
                RenderIntent.Preview,
                RenderRequestPurpose.Frame,
                targetDomain: s_bounds,
                fusionMode: fusionMode),
            requestId => createGraph(
                requestId,
                new HashSet<RenderFragmentReference>(ReferenceEqualityComparer.Instance)),
            budget ?? SkslBackendBudget.Unlimited);
}
