using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.UnitTests.Engine.Graphics.Backend;
using Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Fusion;

[TestFixture]
public sealed class FusionBoundaryTests
{
    private static readonly Rect s_bounds = new(0, 0, 24, 16);

    public static IEnumerable<TestCaseData> RuntimeBarrierCases()
    {
        yield return RuntimeBarrierCase(
            FusionBoundaryRuntimeScenario.MaterializedInput,
            RenderPipelineBoundaryReason.CacheInput,
            expectedIntermediates: 0,
            expectedShaderRuns: 1);
        yield return RuntimeBarrierCase(
            FusionBoundaryRuntimeScenario.WholeSource,
            RenderPipelineBoundaryReason.UnsafeComposite,
            expectedIntermediates: 2,
            expectedShaderRuns: 2);
        yield return RuntimeBarrierCase(
            FusionBoundaryRuntimeScenario.Geometry,
            RenderPipelineBoundaryReason.Geometry,
            expectedIntermediates: 3,
            expectedShaderRuns: 1);
        yield return RuntimeBarrierCase(
            FusionBoundaryRuntimeScenario.OpaqueCallback,
            RenderPipelineBoundaryReason.Opaque,
            expectedIntermediates: 2,
            expectedShaderRuns: 2);
        yield return RuntimeBarrierCase(
            FusionBoundaryRuntimeScenario.TargetReadback,
            RenderPipelineBoundaryReason.Readback,
            expectedIntermediates: 1,
            expectedShaderRuns: 1);
        yield return RuntimeBarrierCase(
            FusionBoundaryRuntimeScenario.DestinationBlend,
            RenderPipelineBoundaryReason.UnsafeComposite,
            expectedIntermediates: 1,
            expectedShaderRuns: 1);
        yield return RuntimeBarrierCase(
            FusionBoundaryRuntimeScenario.DynamicExpansion,
            RenderPipelineBoundaryReason.DynamicTopology,
            expectedIntermediates: 3,
            expectedShaderRuns: 1);
        yield return RuntimeBarrierCase(
            FusionBoundaryRuntimeScenario.Graphics3D,
            RenderPipelineBoundaryReason.ThreeD,
            expectedIntermediates: 2,
            expectedShaderRuns: 2);
    }

    [TestCaseSource(nameof(RuntimeBarrierCases))]
    [Category("GpuPassFusionGpu")]
    public void RuntimeBarrier_PreservesDisabledEnabledParityAndExactMaterialization(
        int scenarioValue,
        int expectedReasonValue,
        int expectedIntermediates,
        int expectedShaderRuns)
    {
        var scenario = (FusionBoundaryRuntimeScenario)scenarioValue;
        var expectedReason = (RenderPipelineBoundaryReason)expectedReasonValue;
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using RenderTarget source = FusionBoundaryExecutionTestSupport.CreatePatternSource(s_bounds);
            using var node = new FusionBoundaryRuntimeNode(source, s_bounds, scenario);
            using FusionBoundaryExecutionResult disabled = FusionBoundaryExecutionTestSupport.Execute(
                node,
                s_bounds,
                FusionMode.Disabled);
            using FusionBoundaryExecutionResult enabled = FusionBoundaryExecutionTestSupport.Execute(
                node,
                s_bounds,
                FusionMode.Enabled);

            RgbaMaximumError maximum = ImageMetrics.MaximumAbsoluteErrorPerChannel(
                disabled.Bitmap,
                enabled.Bitmap);
            Assert.Multiple(() =>
            {
                Assert.That(FusionBoundaryExecutionTestSupport.SumAbsoluteChannels(enabled.Bitmap),
                    Is.GreaterThan(1), "The barrier parity oracle must be non-vacuous.");
                Assert.That(maximum.Maximum, Is.LessThanOrEqualTo(0.02));
                Assert.That(enabled.Statistics.FusedShaderRunExecutions, Is.Zero,
                    "A hard boundary must not be crossed by a merged Shader run.");
                Assert.That(enabled.Statistics.ShaderRunExecutions, Is.EqualTo(expectedShaderRuns));
                Assert.That(disabled.Statistics.ShaderRunExecutions, Is.EqualTo(expectedShaderRuns));
                Assert.That(enabled.Statistics.IntermediateTargetAcquisitions,
                    Is.EqualTo(expectedIntermediates));
                Assert.That(disabled.Statistics.IntermediateTargetAcquisitions,
                    Is.EqualTo(expectedIntermediates));
                Assert.That(enabled.Diagnostics[RenderPipelineCounter.FullFrameMaterializations],
                    Is.EqualTo(expectedIntermediates));
                Assert.That(disabled.Diagnostics[RenderPipelineCounter.FullFrameMaterializations],
                    Is.EqualTo(expectedIntermediates));
                Assert.That(enabled.Diagnostics[RenderPipelineCounter.IntermediateAcquires],
                    Is.EqualTo(expectedIntermediates));
                Assert.That(disabled.Diagnostics[RenderPipelineCounter.IntermediateAcquires],
                    Is.EqualTo(expectedIntermediates));
                Assert.That(enabled.Diagnostics[RenderPipelineCounter.ExternalRootResources], Is.EqualTo(1));
                Assert.That(disabled.Diagnostics[RenderPipelineCounter.ExternalRootResources], Is.EqualTo(1));
                Assert.That(enabled.Diagnostics.Events,
                    Has.Some.Matches<RenderPipelineDiagnosticEvent>(item =>
                        item.Kind == RenderPipelineDiagnosticEventKind.BoundaryPlanned
                        && item.BoundaryReason == expectedReason));
            });

            if (scenario == FusionBoundaryRuntimeScenario.TargetReadback)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(enabled.Diagnostics[RenderPipelineCounter.Synchronizations], Is.EqualTo(1));
                    Assert.That(disabled.Diagnostics[RenderPipelineCounter.Synchronizations], Is.EqualTo(1));
                });
            }
            else if (scenario == FusionBoundaryRuntimeScenario.Graphics3D)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(enabled.Diagnostics[RenderPipelineCounter.ExecutedBackendTransitions],
                        Is.EqualTo(1));
                    Assert.That(enabled.Statistics.Synchronizations, Is.EqualTo(1));
                });
            }
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
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
                Assert.That(enabled.Diagnostics[RenderPipelineCounter.FullFrameMaterializations], Is.EqualTo(1));
                Assert.That(enabled.Diagnostics[RenderPipelineCounter.IntermediateAcquires], Is.EqualTo(1));
                Assert.That(enabled.Diagnostics[RenderPipelineCounter.ExternalRootResources], Is.EqualTo(1));
                Assert.That(enabled.Diagnostics.Events,
                    Has.Some.Matches<RenderPipelineDiagnosticEvent>(static item =>
                        item.Kind == RenderPipelineDiagnosticEventKind.BoundaryPlanned
                        && item.BoundaryReason == RenderPipelineBoundaryReason.UnsafeComposite));
            });
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    public void SelectedRenderCacheBoundary_PreservesParityAndPreventsCrossBoundaryFusion()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using RenderTarget source = FusionBoundaryExecutionTestSupport.CreatePatternSource(s_bounds);
            using var disabledNode = new CachedBoundaryRoot(source, s_bounds);
            using var enabledNode = new CachedBoundaryRoot(source, s_bounds);
            disabledNode.Cached.Cache.ReportRenderCount(RenderNodeCache.Count);
            enabledNode.Cached.Cache.ReportRenderCount(RenderNodeCache.Count);
            var disabledDiagnostics = new RenderPipelineDiagnosticsState();
            var enabledDiagnostics = new RenderPipelineDiagnosticsState();
            using var disabledRenderer = CreateBoundaryRenderer(
                disabledNode,
                FusionMode.Disabled,
                disabledDiagnostics,
                useRenderCache: true);
            using var enabledRenderer = CreateBoundaryRenderer(
                enabledNode,
                FusionMode.Enabled,
                enabledDiagnostics,
                useRenderCache: true);

            using RenderNodeRasterization disabledWarm = disabledRenderer.Rasterize();
            using RenderNodeRasterization enabledWarm = enabledRenderer.Rasterize();
            using RenderNodeRasterization disabled = disabledRenderer.Rasterize();
            using RenderNodeRasterization enabled = enabledRenderer.Rasterize();
            Assert.That(disabled.Bitmap, Is.Not.Null);
            Assert.That(enabled.Bitmap, Is.Not.Null);

            RgbaMaximumError maximum = ImageMetrics.MaximumAbsoluteErrorPerChannel(
                disabled.Bitmap!,
                enabled.Bitmap!);
            Assert.Multiple(() =>
            {
                Assert.That(FusionBoundaryExecutionTestSupport.SumAbsoluteChannels(enabled.Bitmap!), Is.GreaterThan(1));
                Assert.That(maximum.Maximum, Is.LessThanOrEqualTo(0.02));
                Assert.That(enabledRenderer.LastExecutionStatistics.FusedShaderRunExecutions, Is.Zero);
                Assert.That(enabledRenderer.LastExecutionStatistics.ShaderRunExecutions, Is.EqualTo(1));
                Assert.That(disabledRenderer.LastExecutionStatistics.ShaderRunExecutions, Is.EqualTo(1));
                Assert.That(enabledDiagnostics.Latest[RenderPipelineCounter.RenderCacheHits], Is.EqualTo(1));
                Assert.That(disabledDiagnostics.Latest[RenderPipelineCounter.RenderCacheHits], Is.EqualTo(1));
                Assert.That(enabledDiagnostics.Latest.Events,
                    Has.Some.Matches<RenderPipelineDiagnosticEvent>(static item =>
                        item.Kind == RenderPipelineDiagnosticEventKind.BoundaryPlanned
                        && item.BoundaryReason == RenderPipelineBoundaryReason.CacheInput));
                Assert.That(enabledDiagnostics.Latest.Events,
                    Has.None.Matches<RenderPipelineDiagnosticEvent>(static item =>
                        item.Kind == RenderPipelineDiagnosticEventKind.BoundaryPlanned
                        && item.BoundaryReason == RenderPipelineBoundaryReason.CacheCapture));
            });
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    public void StandaloneBackendOverflow_ExecutesCompatibilityPathWithParityAndExactReason()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using RenderTarget source = FusionBoundaryExecutionTestSupport.CreatePatternSource(s_bounds);
            using var node = new BackendOverflowBoundaryNode(source, s_bounds);
            SkslBackendBudget budget = new(
                capabilityClass: (typeof(FusionBoundaryTests), "runtime-standalone-overflow"),
                maxStages: int.MaxValue,
                maxUniformVectors: 0,
                maxSamplers: int.MaxValue,
                maxChildren: int.MaxValue,
                maxSourceBytes: int.MaxValue,
                maxProgramTokens: int.MaxValue);
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
            RenderPipelineDiagnosticEvent[] boundaries = enabled.Diagnostics.Events
                .Where(static item => item.Kind == RenderPipelineDiagnosticEventKind.BoundaryPlanned)
                .ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(FusionBoundaryExecutionTestSupport.SumAbsoluteChannels(enabled.Bitmap), Is.GreaterThan(1));
                Assert.That(maximum.Maximum, Is.LessThanOrEqualTo(0.02));
                Assert.That(enabled.Statistics.ShaderRunExecutions, Is.Zero);
                Assert.That(disabled.Statistics.ShaderRunExecutions, Is.Zero);
                Assert.That(boundaries.Count(static item =>
                        item.BoundaryReason == RenderPipelineBoundaryReason.BackendLimit),
                    Is.EqualTo(1));
                Assert.That(boundaries, Has.None.Matches<RenderPipelineDiagnosticEvent>(static item =>
                    item.BoundaryReason == RenderPipelineBoundaryReason.UnsafeComposite));
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
                new ShaderRenderFragmentPayload(nonlinear, nonlinear.CreateRuntimeIdentity()),
                source);
            return BuildGraph(requestId, [source, shader], [shader], cache);
        });

        CompiledShaderRun run = compiled.ExecutionPlan.ShaderRuns.Single();
        Assert.Multiple(() =>
        {
            Assert.That(run.CoverageSource, Is.EqualTo(ShaderRunCoverageSource.CompatibilityMaterialization));
            Assert.That(run.Stages.Single().CoverageBehavior,
                Is.EqualTo(SkslCoverageBehavior.RequiresResolvedCoverage));
            Assert.That(compiled.ExecutionPlan.Boundaries, Has.Some.Matches<ExecutionIslandBoundary>(
                static boundary => boundary.Reason == ExecutionIslandBoundaryReason.Opaque));
            Assert.That(compiled.ExecutionPlan.Boundaries, Has.Some.Matches<ExecutionIslandBoundary>(
                static boundary => boundary.Reason == ExecutionIslandBoundaryReason.CoverageResolution));
        });
    }

    [Test]
    public void WholeSourceGeometryAndTargetCaptureRemainExplicitBarriers()
    {
        AssertBarrier(
            RenderFragmentKind.Shader,
            WholeSourcePayload(),
            ExecutionIslandBoundaryReason.WholeSourceShader);
        AssertBarrier(
            RenderFragmentKind.Geometry,
            GeometryPayload(),
            expected: ExecutionIslandBoundaryReason.Geometry);
        AssertBarrier(
            RenderFragmentKind.TargetCapture,
            TargetCapturePayload(),
            expected: ExecutionIslandBoundaryReason.TargetCapture);
    }

    [Test]
    public void Graphics3DMetadata_ProducesExplicitThreeDAndBackendTransitionBoundaries()
    {
        OpaqueRenderDescription description = OpaqueRenderDescription.CreateBackendBoundary(
            RenderBackendBoundary.Graphics3D,
            static _ => { },
            RenderOperationBoundsContract.Source(s_bounds),
            RenderHitTestContract.OutputBounds,
            RenderValueCardinality.ZeroOrOne,
            RenderScaleContract.MaterializeAtWorkingScale,
            typeof(FusionBoundaryTests),
            new RenderRuntimeIdentity("3d-test"));
        using CompiledRenderRequest compiled = Compile((requestId, cache) =>
        {
            RenderFragmentReference threeD = Fragment(
                RenderFragmentKind.OpaqueSource,
                new OpaqueRenderFragmentPayload(OpaqueRenderTopology.Source, description));
            RenderFragmentReference shader = CurrentPixel(threeD, "return color.bgra;");
            return BuildGraph(requestId, [threeD, shader], [shader], cache);
        });

        Assert.Multiple(() =>
        {
            Assert.That(compiled.ExecutionPlan.Boundaries, Has.Some.Matches<ExecutionIslandBoundary>(
                static boundary => boundary.Reason == ExecutionIslandBoundaryReason.ThreeD));
            Assert.That(compiled.ExecutionPlan.Boundaries, Has.Some.Matches<ExecutionIslandBoundary>(
                static boundary => boundary.Reason == ExecutionIslandBoundaryReason.BackendTransition));
            Assert.That(compiled.ExecutionPlan.ShaderRuns, Has.Exactly(1).Items,
                "Eligible 2D work may resume after the explicit materialized 3D boundary.");
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

        Assert.Multiple(() =>
        {
            Assert.That(compiled.ExecutionPlan.ShaderRuns.Select(static run => run.Stages.Length),
                Is.EqualTo(new[] { 2 }));
            Assert.That(compiled.ExecutionPlan.Boundaries, Has.None.Matches<ExecutionIslandBoundary>(
                static boundary => boundary.Reason is ExecutionIslandBoundaryReason.CacheInput
                    or ExecutionIslandBoundaryReason.CacheCapture));
        });
    }

    [Test]
    public void BackendStageBudget_SplitsBeforeOverflowAndPreservesOrderDeterministically()
    {
        SkslBackendBudget budget = Budget(maxStages: 2);
        using CompiledRenderRequest first = Compile(FiveStageGraph, budget: budget);
        using CompiledRenderRequest second = Compile(FiveStageGraph, budget: budget);

        CompiledShaderRun[] firstRuns = first.ExecutionPlan.ShaderRuns.ToArray();
        CompiledShaderRun[] secondRuns = second.ExecutionPlan.ShaderRuns.ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(firstRuns.Select(static run => run.Stages.Length), Is.EqualTo(new[] { 2, 2, 1 }));
            Assert.That(firstRuns.SelectMany(static run => run.Stages)
                .Select(static stage => stage.Description.Source.Text),
                Is.EqualTo(secondRuns.SelectMany(static run => run.Stages)
                    .Select(static stage => stage.Description.Source.Text)));
            Assert.That(first.ExecutionPlan.Boundaries.Count(static boundary =>
                    boundary.Reason == ExecutionIslandBoundaryReason.BackendLimit),
                Is.EqualTo(2));
            Assert.That(first.ExecutionPlan.Boundaries
                    .Where(static boundary => boundary.Reason == ExecutionIslandBoundaryReason.BackendLimit),
                Has.All.Matches<ExecutionIslandBoundary>(static boundary =>
                    boundary.BackendLimits.Contains(SkslBackendLimit.StageCount)));
        });
    }

    [Test]
    public void StandaloneBackendOverflow_ReportsOnlyTheExactBackendLimitBoundary()
    {
        SkslBackendBudget budget = new(
            capabilityClass: (typeof(FusionBoundaryTests), "standalone-uniform-overflow"),
            maxStages: int.MaxValue,
            maxUniformVectors: 0,
            maxSamplers: int.MaxValue,
            maxChildren: int.MaxValue,
            maxSourceBytes: int.MaxValue,
            maxProgramTokens: int.MaxValue);
        using CompiledRenderRequest compiled = Compile((requestId, cache) =>
        {
            RenderFragmentReference source = Fragment(RenderFragmentKind.MaterializedInput, payload: null);
            ShaderDescription description = ShaderDescription.CurrentPixel(
                "uniform float gain; half4 apply(half4 color) { return color * gain; }",
                static bindings => bindings.Uniform("gain", 0.5f));
            RenderFragmentReference shader = Fragment(
                RenderFragmentKind.Shader,
                new ShaderRenderFragmentPayload(description, description.CreateRuntimeIdentity()),
                source);
            return BuildGraph(requestId, [source, shader], [shader], cache);
        }, budget: budget);

        ExecutionIslandBoundary[] backendBoundaries = compiled.ExecutionPlan.Boundaries
            .Where(static boundary => boundary.Reason == ExecutionIslandBoundaryReason.BackendLimit)
            .ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(compiled.ExecutionPlan.ShaderRuns, Is.Empty);
            Assert.That(compiled.ExecutionPlan.Islands, Has.Exactly(1).Items);
            Assert.That(backendBoundaries, Has.Exactly(1).Items);
            Assert.That(backendBoundaries[0].BackendLimits,
                Is.EqualTo(new[] { SkslBackendLimit.UniformVectors }));
            Assert.That(compiled.ExecutionPlan.Boundaries, Has.None.Matches<ExecutionIslandBoundary>(
                static boundary => boundary.Reason == ExecutionIslandBoundaryReason.WholeSourceShader));
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
                new ShaderRenderFragmentPayload(description, description.CreateRuntimeIdentity()),
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
            Assert.That(dynamic.ExecutionPlan.Boundaries, Has.Some.Matches<ExecutionIslandBoundary>(
                static boundary => boundary.Reason == ExecutionIslandBoundaryReason.DynamicTopology));
            Assert.That(groupOpacity.ExecutionPlan.ShaderRuns, Is.Empty,
                "Group opacity over multiple values is not equivalent to per-value color multiplication.");
            Assert.That(groupOpacity.ExecutionPlan.Boundaries, Has.Some.Matches<ExecutionIslandBoundary>(
                static boundary => boundary.Reason == ExecutionIslandBoundaryReason.DynamicTopology));
        });
    }

    private static void AssertBarrier(
        RenderFragmentKind barrierKind,
        object? payload,
        ExecutionIslandBoundaryReason expected)
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
            Assert.That(compiled.ExecutionPlan.Boundaries, Has.Some.Matches<ExecutionIslandBoundary>(
                boundary => boundary.Reason == expected));
            Assert.That(compiled.ExecutionPlan.Boundaries, Has.Some.Matches<ExecutionIslandBoundary>(
                static boundary => boundary.Reason == ExecutionIslandBoundaryReason.CoverageResolution));
        });
    }

    private static RecordedRenderGraph FiveStageGraph(
        RenderRequestId requestId,
        HashSet<RenderFragmentReference> cache)
    {
        RenderFragmentReference source = Fragment(RenderFragmentKind.MaterializedInput, payload: null);
        var references = new List<RenderFragmentReference> { source };
        RenderFragmentReference current = source;
        for (int index = 0; index < 5; index++)
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
        return new ShaderRenderFragmentPayload(description, description.CreateRuntimeIdentity());
    }

    private static object TargetCapturePayload()
    {
        TargetCaptureDescription description = TargetCaptureDescription.Create(
            TargetRegion.Full,
            s_bounds,
            RenderHitTestContract.None,
            RenderScaleContract.MaterializeAtWorkingScale);
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
            new ShaderRenderFragmentPayload(description, description.CreateRuntimeIdentity()),
            input);
    }

    private static GeometryRenderFragmentPayload GeometryPayload()
    {
        GeometryDescription description = GeometryDescription.Create(
            static _ => { },
            RenderBoundsContract.Identity,
            RenderHitTestContract.OutputBounds);
        return new GeometryRenderFragmentPayload(description, new object());
    }

    private static OpaqueRenderFragmentPayload OpaquePayload(
        OpaqueRenderTopology topology,
        RenderValueCardinality cardinality)
    {
        RenderOperationBoundsContract bounds = topology switch
        {
            OpaqueRenderTopology.Source => RenderOperationBoundsContract.Source(s_bounds),
            OpaqueRenderTopology.Map => RenderOperationBoundsContract.Map(RenderBoundsContract.Identity),
            OpaqueRenderTopology.Combine or OpaqueRenderTopology.Expand
                => RenderOperationBoundsContract.FullInputs(static _ => s_bounds),
            _ => throw new ArgumentOutOfRangeException(nameof(topology)),
        };
        OpaqueRenderDescription description = OpaqueRenderDescription.Create(
            static _ => { },
            bounds,
            RenderHitTestContract.OutputBounds,
            cardinality,
            RenderScaleContract.MaterializeAtWorkingScale);
        return new OpaqueRenderFragmentPayload(topology, description);
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
            inputs,
            payload,
            static _ => true);
    }

    private static CompiledRenderRequest Compile(
        Func<RenderRequestId, HashSet<RenderFragmentReference>, RecordedRenderGraph> createGraph,
        FusionMode fusionMode = FusionMode.Enabled,
        SkslBackendBudget? budget = null)
    {
        var options = new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Frame,
            targetDomain: s_bounds,
            fusionMode: fusionMode);
        var request = new RenderRequest(options);
        var cache = new HashSet<RenderFragmentReference>(ReferenceEqualityComparer.Instance);
        RecordedRenderGraph graph = createGraph(request.Id, cache);
        request.TransitionTo(RenderRequestState.Recording);
        request.TransitionTo(RenderRequestState.Recorded);
        return new RenderRequestCompiler().Compile(
            request,
            graph,
            budget ?? SkslBackendBudget.Unlimited);
    }

    private static RecordedRenderGraph BuildGraph(
        RenderRequestId requestId,
        IReadOnlyList<RenderFragmentReference> references,
        IReadOnlyList<RenderFragmentReference> roots,
        IReadOnlySet<RenderFragmentReference> cache)
    {
        var builder = new RecordedRenderGraphBuilder(requestId);
        RenderProvenanceId provenance = builder.AddProvenance(typeof(FusionBoundaryTests), "test");
        foreach (RenderFragmentReference reference in references)
        {
            RenderValueId[] inputs = reference.Inputs.SelectMany(static input => input.ValueIds).ToArray();
            reference.ValueIds = reference.ValueCardinality.Maximum == 0
                ? []
                : [builder.AddValue(inputs, provenance, reference)];
            reference.Id = builder.AddFragment(reference.ValueIds, provenance, reference);
            if (cache.Contains(reference))
                builder.AddCacheCandidate(reference.Id.Value, (typeof(FusionBoundaryTests), reference.Id.Value.Value));
        }
        foreach (RenderFragmentReference root in roots)
            builder.PublishRoot(root.Id!.Value);
        return builder.Build();
    }

    private static SkslBackendBudget Budget(int maxStages)
        => new(
            capabilityClass: (typeof(FusionBoundaryTests), maxStages),
            maxStages,
            maxUniformVectors: int.MaxValue,
            maxSamplers: int.MaxValue,
            maxChildren: int.MaxValue,
            maxSourceBytes: int.MaxValue,
            maxProgramTokens: int.MaxValue);

    private static TestCaseData RuntimeBarrierCase(
        FusionBoundaryRuntimeScenario scenario,
        RenderPipelineBoundaryReason reason,
        int expectedIntermediates,
        int expectedShaderRuns)
        => new TestCaseData((int)scenario, (int)reason, expectedIntermediates, expectedShaderRuns)
            .SetName($"RuntimeBarrier_{scenario}_PreservesParityAndMaterialization");

    private static RenderNodeRenderer CreateBoundaryRenderer(
        RenderNode node,
        FusionMode fusionMode,
        IRenderPipelineDiagnosticsState diagnostics,
        bool useRenderCache)
        => new(
            node,
            new RenderNodeRendererOptions
            {
                TargetDomain = s_bounds,
                OutputScale = 1,
                MaxWorkingScale = 1,
                UseRenderCache = useRenderCache,
                FusionMode = fusionMode,
                RenderPurpose = RenderRequestPurpose.Frame,
                Diagnostics = diagnostics,
            });
}
