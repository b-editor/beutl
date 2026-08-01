using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;
using Beutl.UnitTests.Engine.Graphics.Rendering.Golden;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Fusion;

[TestFixture]
[NonParallelizable]
public sealed class CrossNodeShaderFusionTests
{
    private static readonly Rect s_bounds = new(3, 5, 12, 8);

    [Test]
    public void Enabled_CompilesDistinctShaderOpacityShaderNodesAsOneRun()
    {
        using CompiledRenderRequest compiled = CompilePrimaryChain(FusionMode.Enabled);

        CompiledShaderRun run = compiled.ExecutionPlan.ShaderRuns.Single();
        Assert.Multiple(() =>
        {
            Assert.That(compiled.ExecutionPlan.Islands, Has.Length.EqualTo(1));
            Assert.That(run.Stages.Select(static stage => stage.Kind), Is.EqualTo(new[]
            {
                RenderFragmentKind.Shader,
                RenderFragmentKind.Opacity,
                RenderFragmentKind.Shader,
            }));
            Assert.That(run.Stages.Select(static stage => stage.CoverageBehavior), Is.EqualTo(new[]
            {
                SkslCoverageBehavior.RequiresResolvedCoverage,
                SkslCoverageBehavior.PremultipliedCoverageHomogeneous,
                SkslCoverageBehavior.RequiresResolvedCoverage,
            }));
            Assert.That(run.Program.StageCount, Is.EqualTo(3));
            Assert.That(run.IsFused, Is.True);
            Assert.That(run.CoverageSource, Is.EqualTo(ShaderRunCoverageSource.MaterializedInput));
            Assert.That(run.Output.CanBeUsedAsValueInput, Is.True,
                "The typed opacity descriptor must preserve value-input eligibility.");
            Assert.That(compiled.ExecutionPlan.Boundaries, Has.Some.Matches<ExecutionIslandBoundary>(
                static boundary => boundary.Reason == ExecutionIslandBoundaryReason.MaterializedInput));
        });

        string source = run.Program.Source;
        int gamma = source.IndexOf("__beutl_s0_apply", StringComparison.Ordinal);
        int opacity = source.IndexOf("__beutl_s1_apply", StringComparison.Ordinal);
        int invert = source.IndexOf("__beutl_s2_apply", StringComparison.Ordinal);
        Assert.That(gamma, Is.GreaterThanOrEqualTo(0));
        Assert.That(opacity, Is.GreaterThan(gamma));
        Assert.That(invert, Is.GreaterThan(opacity));
    }

    [Test]
    public void Disabled_KeepsIdenticalSemanticStagesButPreventsComposition()
    {
        using CompiledRenderRequest enabled = CompilePrimaryChain(FusionMode.Enabled);
        using CompiledRenderRequest disabled = CompilePrimaryChain(FusionMode.Disabled);

        CompiledShaderStage[] enabledStages = enabled.ExecutionPlan.ShaderRuns
            .SelectMany(static run => run.Stages)
            .ToArray();
        CompiledShaderStage[] disabledStages = disabled.ExecutionPlan.ShaderRuns
            .SelectMany(static run => run.Stages)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(enabled.Request.Options.PlanIdentity,
                Is.Not.EqualTo(disabled.Request.Options.PlanIdentity));
            Assert.That(enabled.ExecutionPlan.ShaderRuns.Count(), Is.EqualTo(1));
            Assert.That(disabled.ExecutionPlan.ShaderRuns.Count(), Is.EqualTo(3));
            Assert.That(disabled.ExecutionPlan.ShaderRuns.Select(static run => run.Stages.Length),
                Is.EqualTo(new[] { 1, 1, 1 }));
            Assert.That(disabledStages.Select(static stage => stage.Kind),
                Is.EqualTo(enabledStages.Select(static stage => stage.Kind)));
            Assert.That(disabledStages.Select(static stage => stage.Description.Source.Text),
                Is.EqualTo(enabledStages.Select(static stage => stage.Description.Source.Text)));
            Assert.That(disabled.ExecutionPlan.Boundaries.Count(static boundary =>
                    boundary.Reason == ExecutionIslandBoundaryReason.FusionDisabled),
                Is.EqualTo(2));
        });
    }

    [Test]
    public void CpuDestination_UsesPortableBudgetAndSplitsLongShaderChain()
    {
        int stageCount = SkslBackendBudgetResolver.Portable.MaxStages + 1;
        var targetFactory = new CpuTargetFactory();
        using var node = new LongShaderChainNode(stageCount);
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    Intent = RenderIntent.Preview,
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                    FusionMode = FusionMode.Enabled,
                    Purpose = RenderRequestPurpose.Frame,
                },
                TargetFactory = targetFactory,
            });
        using RenderTarget destination = targetFactory.CreateCpuTarget(new PixelSize(24, 16));
        using var canvas = new ImmediateCanvas(destination, logicalSize: new Size(24, 16));

        Assert.That(destination.Value.Context, Is.Null);
        renderer.Render(canvas);

        Assert.Multiple(() =>
        {
            Assert.That(renderer.LastExecutionStatistics.ShaderRunExecutions, Is.EqualTo(2));
            Assert.That(renderer.LastExecutionStatistics.ShaderStageExecutions, Is.EqualTo(stageCount));
            Assert.That(renderer.LastExecutionStatistics.FusedShaderRunExecutions, Is.EqualTo(1));
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    public void ChangingFromCpuToGpuDestination_SelectsTheActualSurfaceCapabilityClass()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            int stageCount = SkslBackendBudgetResolver.Portable.MaxStages + 1;
            var cpuFactory = new CpuTargetFactory();
            using var node = new LongShaderChainNode(stageCount);
            using var renderer = new RenderNodeRenderer(
                node,
                new RenderNodeRendererOptions
                {
                    DefaultRequest = new RenderNodeRenderRequest
                    {
                        Intent = RenderIntent.Preview,
                        CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                        FusionMode = FusionMode.Enabled,
                        Purpose = RenderRequestPurpose.Frame,
                    },
                });
            using RenderTarget cpuDestination = cpuFactory.CreateCpuTarget(new PixelSize(24, 16));
            using var cpuCanvas = new ImmediateCanvas(cpuDestination, logicalSize: new Size(24, 16));
            using RenderTarget gpuDestination = RenderTarget.Create(24, 16)
                ?? throw new InvalidOperationException("Could not create the GPU fusion-test surface.");
            using var gpuCanvas = new ImmediateCanvas(gpuDestination, logicalSize: new Size(24, 16));

            Assert.Multiple(() =>
            {
                Assert.That(cpuDestination.Value.Context, Is.Null);
                Assert.That(
                    gpuDestination.Value.Context?.Backend,
                    Is.AnyOf(GRBackend.Vulkan, GRBackend.Metal));
            });

            renderer.Render(cpuCanvas);
            StructuralPlanCacheStatistics cpuStatistics = renderer.StructuralPlanCacheStatistics;
            renderer.Render(gpuCanvas);
            StructuralPlanCacheStatistics firstGpuStatistics = renderer.StructuralPlanCacheStatistics;
            renderer.Render(gpuCanvas);
            StructuralPlanCacheStatistics warmedGpuStatistics = renderer.StructuralPlanCacheStatistics;

            Assert.Multiple(() =>
            {
                Assert.That(cpuStatistics.Compilations, Is.EqualTo(1));
                Assert.That(firstGpuStatistics.Compilations, Is.EqualTo(2));
                Assert.That(firstGpuStatistics.Replacements, Is.EqualTo(1));
                Assert.That(warmedGpuStatistics.Compilations, Is.EqualTo(2));
                Assert.That(warmedGpuStatistics.Hits, Is.EqualTo(1));
                Assert.That(renderer.LastExecutionStatistics.ShaderRunExecutions, Is.EqualTo(2));
                Assert.That(renderer.LastExecutionStatistics.ShaderStageExecutions, Is.EqualTo(stageCount));
            });
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    public void ProgramCache_ContextChangeMissesAndThenWarmsTheNewContext()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using IGraphicsContext firstContext = GraphicsContextFactory.CreateContext();
            using IGraphicsContext secondContext = GraphicsContextFactory.CreateContext();
            using RenderTarget firstDestination = CreateContextTarget(firstContext, 24, 16);
            using RenderTarget secondDestination = CreateContextTarget(secondContext, 24, 16);
            using var firstCanvas = new ImmediateCanvas(
                firstDestination,
                logicalSize: new Size(24, 16));
            using var secondCanvas = new ImmediateCanvas(
                secondDestination,
                logicalSize: new Size(24, 16));
            using RenderTarget source = new CpuTargetFactory().CreateCpuTarget(
                new PixelSize((int)s_bounds.Width, (int)s_bounds.Height));
            source.Value.Canvas.Clear(new SKColor(48, 112, 216, 176));
            using var node = new PrimaryChainNode(source, s_bounds);
            using var renderer = CreateRenderer(node, FusionMode.Enabled);

            renderer.Render(firstCanvas);
            ProgramCacheStatistics firstCold = renderer.ProgramCacheStatistics;
            renderer.Render(firstCanvas);
            ProgramCacheStatistics firstWarm = renderer.ProgramCacheStatistics;
            renderer.Render(secondCanvas);
            ProgramCacheStatistics secondCold = renderer.ProgramCacheStatistics;
            renderer.Render(secondCanvas);
            ProgramCacheStatistics secondWarm = renderer.ProgramCacheStatistics;

            Assert.Multiple(() =>
            {
                Assert.That(firstDestination.Value.Context?.Backend,
                    Is.EqualTo(secondDestination.Value.Context?.Backend));
                Assert.That(firstDestination.Value.Context?.Handle,
                    Is.Not.EqualTo(secondDestination.Value.Context?.Handle));
                Assert.That(firstCold.Creations, Is.EqualTo(1));
                Assert.That(firstCold.Hits, Is.Zero);
                Assert.That(firstCold.RetainedPrograms, Is.EqualTo(1));
                Assert.That(firstWarm.Creations, Is.EqualTo(1));
                Assert.That(firstWarm.Hits, Is.EqualTo(1));
                Assert.That(firstWarm.RetainedPrograms, Is.EqualTo(1));
                Assert.That(secondCold.Creations, Is.EqualTo(2));
                Assert.That(secondCold.Hits, Is.EqualTo(1));
                Assert.That(secondCold.Evictions, Is.EqualTo(1));
                Assert.That(secondCold.RetainedPrograms, Is.EqualTo(1),
                    "switching contexts must eagerly discharge the old context's program");
                Assert.That(secondWarm.Creations, Is.EqualTo(2));
                Assert.That(secondWarm.Hits, Is.EqualTo(2));
                Assert.That(secondWarm.Evictions, Is.EqualTo(1));
                Assert.That(secondWarm.RetainedPrograms, Is.EqualTo(1));
            });
        });
    }

    [Test]
    public void PublishedIntermediateFanOut_IsAnExplicitDeterministicBoundary()
    {
        using CompiledRenderRequest compiled = CompilePrimaryChain(
            FusionMode.Enabled,
            publishFirstShader: true);

        CompiledShaderRun[] runs = compiled.ExecutionPlan.ShaderRuns.ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(runs, Has.Length.EqualTo(2));
            Assert.That(runs[0].Stages.Select(static stage => stage.Kind),
                Is.EqualTo(new[] { RenderFragmentKind.Shader }));
            Assert.That(runs[1].Stages.Select(static stage => stage.Kind),
                Is.EqualTo(new[] { RenderFragmentKind.Opacity, RenderFragmentKind.Shader }));
            Assert.That(compiled.ExecutionPlan.Boundaries, Has.Some.Matches<ExecutionIslandBoundary>(
                static boundary => boundary.Reason == ExecutionIslandBoundaryReason.Branching));
        });
    }

    [Test]
    public void Planner_OmitsCommittedFragmentsThatAreNotReachableFromAPublication()
    {
        var requestId = new RenderRequestId(1);
        RenderFragmentReference source = Fragment(
            RenderFragmentKind.MaterializedInput,
            EffectiveScale.At(1),
            payload: null);
        ShaderDescription description = ShaderDescription.CurrentPixel(
            "half4 apply(half4 color) { return color; }");
        RenderFragmentReference unpublished = Fragment(
            RenderFragmentKind.Shader,
            EffectiveScale.At(1),
            new ShaderRenderFragmentPayload(description, description.CreateRuntimeIdentity()),
            source);
        RecordedRenderGraph graph = BuildGraph(requestId, [source, unpublished], [source]);

        ExecutionIslandPlan plan = new ExecutionIslandPlanner().Plan(
            graph,
            RenderRequestCompiler.ResolveRoots(graph),
            FusionMode.Enabled,
            SkslBackendBudget.Unlimited);

        Assert.Multiple(() =>
        {
            Assert.That(graph.Fragments, Has.Length.EqualTo(2));
            Assert.That(plan.Islands, Is.Empty);
            Assert.That(plan.Boundaries, Is.Empty);
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    public void Enabled_ExecutesDistinctNodePrimaryChainOnce_WithParityAndAWarmedProgram()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using RenderTarget source = CreateSourceTarget();
            using var node = new PrimaryChainNode(source, s_bounds);
            var diagnostics = new RenderPipelineDiagnosticsState();
            using var enabled = CreateRenderer(node, FusionMode.Enabled, diagnostics: diagnostics);
            using var disabled = CreateRenderer(node, FusionMode.Disabled);

            using RenderNodeRasterization disabledRaster = disabled.Rasterize();
            using RenderNodeRasterization enabledRaster = enabled.Rasterize();
            using RenderNodeRasterization warmedRaster = enabled.Rasterize();

            Assert.That(disabledRaster.Bitmap, Is.Not.Null);
            Assert.That(enabledRaster.Bitmap, Is.Not.Null);
            Assert.That(warmedRaster.Bitmap, Is.Not.Null);
            RgbaMaximumError parity = ImageMetrics.MaximumAbsoluteErrorPerChannel(
                disabledRaster.Bitmap!,
                enabledRaster.Bitmap!);
            RgbaMaximumError warmedParity = ImageMetrics.MaximumAbsoluteErrorPerChannel(
                enabledRaster.Bitmap!,
                warmedRaster.Bitmap!);
            double ssim = ImageMetrics.Ssim(disabledRaster.Bitmap!, enabledRaster.Bitmap!);
            double energy = SumAbsoluteChannels(enabledRaster.Bitmap!);

            Assert.Multiple(() =>
            {
                Assert.That(energy, Is.GreaterThan(1), "the execution oracle must not be transparent or vacuous");
                Assert.That(ssim, Is.GreaterThanOrEqualTo(0.99));
                Assert.That(parity.Maximum, Is.LessThanOrEqualTo(0.0025));
                Assert.That(warmedParity.Maximum, Is.Zero);
                Assert.That(enabled.LastExecutionStatistics.ShaderRunExecutions, Is.EqualTo(1));
                Assert.That(enabled.LastExecutionStatistics.ShaderStageExecutions, Is.EqualTo(3));
                Assert.That(enabled.LastExecutionStatistics.FusedShaderRunExecutions, Is.EqualTo(1));
                Assert.That(enabled.LastExecutionStatistics.IntermediateTargetAcquisitions, Is.Zero);
                Assert.That(enabled.LastExecutionStatistics.Synchronizations, Is.Zero);
                Assert.That(diagnostics.Latest[RenderPipelineCounter.PlannedGpuPasses], Is.EqualTo(1));
                Assert.That(diagnostics.Latest[RenderPipelineCounter.ExecutedGpuPasses], Is.EqualTo(1));
                Assert.That(enabled.LastExecutionStatistics.ProgramCacheHits, Is.EqualTo(1));
                Assert.That(enabled.ProgramCacheStatistics.Creations, Is.EqualTo(1));
                Assert.That(enabled.ProgramCacheStatistics.Hits, Is.EqualTo(1));
                Assert.That(enabled.TargetPoolStatistics.Creates, Is.EqualTo(1));
                Assert.That(enabled.TargetPoolStatistics.Misses, Is.EqualTo(1));
                Assert.That(enabled.TargetPoolStatistics.Reuses, Is.EqualTo(1));
                Assert.That(enabled.TargetPoolStatistics.LeasedTargets, Is.Zero);
                Assert.That(disabled.LastExecutionStatistics.ShaderRunExecutions, Is.EqualTo(3));
                Assert.That(disabled.LastExecutionStatistics.FusedShaderRunExecutions, Is.Zero);
                Assert.That(enabled.Options.DefaultRequest.FusionMode, Is.EqualTo(FusionMode.Enabled));
                Assert.That(disabled.Options.DefaultRequest.FusionMode, Is.EqualTo(FusionMode.Disabled));
                Assert.That(enabled.StructuralPlanCacheStatistics.Compilations, Is.EqualTo(1));
                Assert.That(enabled.StructuralPlanCacheStatistics.Misses, Is.EqualTo(1));
                Assert.That(enabled.StructuralPlanCacheStatistics.Hits, Is.EqualTo(1));
                Assert.That(disabled.StructuralPlanCacheStatistics.Compilations, Is.EqualTo(1));
                Assert.That(disabled.StructuralPlanCacheStatistics.Misses, Is.EqualTo(1));
                Assert.That(disabled.StructuralPlanCacheStatistics.Hits, Is.Zero);
                Assert.That(node.ProcessCounts, Is.EqualTo(new[] { 3, 3, 3 }),
                    "Each render must traverse the countable source, Gamma, and Invert node transactions.");
            });
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    public void UnboundedVectorShaderRoot_DrawsTerminalRunDirectlyAndCacheForcesMaterialization()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var directDiagnostics = new RenderPipelineDiagnosticsState();
            using var directNode = new VectorTerminalShaderNode(publishTwice: false);
            using var direct = CreateRenderer(
                directNode,
                FusionMode.Enabled,
                diagnostics: directDiagnostics);
            using RenderNodeRasterization directRaster = direct.Rasterize();

            var cacheDiagnostics = new RenderPipelineDiagnosticsState();
            using var cachedNode = new VectorTerminalShaderNode(publishTwice: false);
            cachedNode.Cache.ReportRenderCount(RenderNodeCache.Count);
            using var cached = CreateRenderer(
                cachedNode,
                FusionMode.Enabled,
                useRenderCache: true,
                diagnostics: cacheDiagnostics);
            using RenderNodeRasterization missRaster = cached.Rasterize();
            RenderExecutionStatistics missStatistics = cached.LastExecutionStatistics;
            using RenderNodeRasterization hitRaster = cached.Rasterize();

            Assert.That(directRaster.Bitmap, Is.Not.Null);
            Assert.That(missRaster.Bitmap, Is.Not.Null);
            Assert.That(hitRaster.Bitmap, Is.Not.Null);
            RgbaMaximumError missParity = ImageMetrics.MaximumAbsoluteErrorPerChannel(
                directRaster.Bitmap!,
                missRaster.Bitmap!);
            RgbaMaximumError hitParity = ImageMetrics.MaximumAbsoluteErrorPerChannel(
                directRaster.Bitmap!,
                hitRaster.Bitmap!);

            Assert.Multiple(() =>
            {
                Assert.That(directNode.OutputScale.IsUnbounded, Is.True);
                Assert.That(direct.LastExecutionStatistics.ShaderRunExecutions, Is.EqualTo(1));
                Assert.That(direct.LastExecutionStatistics.IntermediateTargetAcquisitions, Is.EqualTo(1),
                    "Only the vector coverage input should materialize; the terminal Shader writes the root target.");
                Assert.That(directDiagnostics.Latest[RenderPipelineCounter.ExecutedGpuPasses], Is.EqualTo(2));
                Assert.That(cachedNode.Cache.IsCached, Is.True);
                Assert.That(missStatistics.IntermediateTargetAcquisitions, Is.GreaterThan(1),
                    "A selected cache capture must force a materialized terminal Shader value.");
                Assert.That(cacheDiagnostics.Latest[RenderPipelineCounter.RenderCacheHits], Is.EqualTo(1));
                Assert.That(missParity.Maximum, Is.LessThanOrEqualTo(0.02));
                Assert.That(hitParity.Maximum, Is.LessThanOrEqualTo(0.02));
            });
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    public void PublishedShaderFanOut_DisablesTerminalDirectDraw()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var node = new VectorTerminalShaderNode(publishTwice: true);
            using var renderer = CreateRenderer(node, FusionMode.Enabled);

            using RenderNodeRasterization result = renderer.Rasterize();

            Assert.Multiple(() =>
            {
                Assert.That(result.Bitmap, Is.Not.Null);
                Assert.That(node.OutputScale.IsUnbounded, Is.True);
                Assert.That(renderer.LastExecutionStatistics.ShaderRunExecutions, Is.EqualTo(1));
                Assert.That(renderer.LastExecutionStatistics.IntermediateTargetAcquisitions, Is.EqualTo(2),
                    "Fan-out must materialize both the vector input and terminal Shader output exactly once.");
            });
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    public void TerminalDirectDraw_PreservesActiveDestinationState_WithCacheMissAsReference()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using RenderTarget source = CreateSourceTarget();
            using var directNode = new PrimaryChainNode(source, s_bounds);
            using var cachedNode = new PrimaryChainNode(source, s_bounds);
            cachedNode.Cache.ReportRenderCount(RenderNodeCache.Count);
            using var direct = CreateRenderer(directNode, FusionMode.Enabled);
            using var cached = CreateRenderer(cachedNode, FusionMode.Enabled, useRenderCache: true);

            using Bitmap directBitmap = RenderWithActiveDestinationState(direct);
            using Bitmap cachedBitmap = RenderWithActiveDestinationState(cached);
            using Bitmap background = CreateActiveDestinationBackground();
            RgbaMaximumError parity = ImageMetrics.MaximumAbsoluteErrorPerChannel(
                directBitmap,
                cachedBitmap);
            RgbaMaximumError directChange = ImageMetrics.MaximumAbsoluteErrorPerChannel(
                directBitmap,
                background);
            RgbaMaximumError cachedChange = ImageMetrics.MaximumAbsoluteErrorPerChannel(
                cachedBitmap,
                background);

            Assert.Multiple(() =>
            {
                Assert.That(direct.LastExecutionStatistics.IntermediateTargetAcquisitions, Is.Zero);
                Assert.That(cached.LastExecutionStatistics.IntermediateTargetAcquisitions, Is.GreaterThan(0));
                Assert.That(cachedNode.Cache.IsCached, Is.True);
                Assert.That(directChange.Maximum, Is.GreaterThan(0.02),
                    "The terminal direct draw must modify the cleared destination.");
                Assert.That(cachedChange.Maximum, Is.GreaterThan(0.02),
                    "The cache-materialized terminal draw must modify the cleared destination.");
                Assert.That(parity.Maximum, Is.LessThanOrEqualTo(0.02));
            });
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    public void TerminalDirectDraw_FractionalDestinationFallsBackToMaterializedComposite()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using RenderTarget source = CreateSourceTarget();
            using var directNode = new PrimaryChainNode(source, s_bounds);
            using var cachedNode = new PrimaryChainNode(source, s_bounds);
            cachedNode.Cache.ReportRenderCount(RenderNodeCache.Count);
            using var direct = CreateRenderer(directNode, FusionMode.Enabled);
            using var cached = CreateRenderer(cachedNode, FusionMode.Enabled, useRenderCache: true);

            using Bitmap directBitmap = RenderWithDestinationTranslation(direct, 2.25f, 1.5f);
            using Bitmap cachedBitmap = RenderWithDestinationTranslation(cached, 2.25f, 1.5f);
            RgbaMaximumError parity = ImageMetrics.MaximumAbsoluteErrorPerChannel(
                directBitmap,
                cachedBitmap);

            Assert.Multiple(() =>
            {
                Assert.That(direct.LastExecutionStatistics.IntermediateTargetAcquisitions, Is.GreaterThan(0),
                    "A fractional device origin must disable the terminal direct draw.");
                Assert.That(cached.LastExecutionStatistics.IntermediateTargetAcquisitions, Is.GreaterThan(0));
                Assert.That(parity.Maximum, Is.LessThanOrEqualTo(0.02));
            });
        });
    }

    private static RenderNodeRenderer CreateRenderer(
        RenderNode node,
        FusionMode fusionMode,
        bool useRenderCache = false,
        IRenderPipelineDiagnosticsState? diagnostics = null)
        => new(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    Intent = RenderIntent.Preview,
                    TargetDomain = s_bounds,
                    OutputScale = 1,
                    MaxWorkingScale = 1,
                    CacheOptions = new Beutl.Graphics.Rendering.Cache.RenderCacheOptions(useRenderCache, Beutl.Graphics.Rendering.Cache.RenderCacheRules.Default),
                    FusionMode = fusionMode,
                    Purpose = RenderRequestPurpose.Frame,
                    Diagnostics = diagnostics,
                },
            });

    private static Bitmap RenderWithActiveDestinationState(RenderNodeRenderer renderer)
        => RenderWithDestinationTranslation(renderer, 2, 1);

    private static Bitmap CreateActiveDestinationBackground()
    {
        using RenderTarget target = RenderTarget.Create(32, 24)
            ?? throw new InvalidOperationException("Could not allocate the active-state background control.");
        target.Value.Canvas.Clear(new SKColor(26, 48, 72, 255));
        return target.Snapshot();
    }

    private static Bitmap RenderWithDestinationTranslation(
        RenderNodeRenderer renderer,
        float x,
        float y)
    {
        using RenderTarget target = RenderTarget.Create(32, 24)
            ?? throw new InvalidOperationException("Could not allocate the active-state destination.");
        using var canvas = new ImmediateCanvas(target, logicalSize: new Size(32, 24));
        canvas.Clear(new Color(255, 26, 48, 72));
        using (canvas.PushTransform(Matrix.CreateTranslation(x, y)))
        using (canvas.PushClip(new Rect(4, 5, 10, 7)))
        using (canvas.PushOpacity(0.625f))
        using (canvas.PushBlendMode(BlendMode.Screen))
        {
            renderer.Render(canvas);
        }

        return target.Snapshot();
    }

    private static RenderTarget CreateSourceTarget()
    {
        RenderTarget target = RenderTarget.Create((int)s_bounds.Width, (int)s_bounds.Height)
            ?? throw new InvalidOperationException("Could not allocate the deterministic fusion source.");
        using var paint = new SKPaint
        {
            Color = new SKColor(48, 112, 216, 176),
            IsAntialias = false,
        };
        target.Value.Canvas.Clear(new SKColor(12, 24, 40, 96));
        target.Value.Canvas.DrawRect(SKRect.Create(2, 1, 8, 6), paint);
        return target;
    }

    private static RenderTarget CreateContextTarget(
        IGraphicsContext context,
        int width,
        int height)
    {
        ITexture2D texture = context.CreateTexture2D(
            width,
            height,
            TextureFormat.RGBA16Float);
        try
        {
            return new TextureRenderTarget(texture);
        }
        catch
        {
            texture.Dispose();
            throw;
        }
    }

    private static double SumAbsoluteChannels(Bitmap bitmap)
    {
        double result = 0;
        foreach (ushort bits in bitmap.GetPixelSpan<ushort>())
            result += Math.Abs((float)BitConverter.UInt16BitsToHalf(bits));
        return result;
    }

    private sealed class PrimaryChainNode : RenderNode
    {
        private static readonly ShaderDescription s_gamma = ShaderDescription.CurrentPixel(
            "half4 apply(half4 color) { return half4(sqrt(max(color.rgb, half3(0))), color.a); }");

        private static readonly ShaderDescription s_invert = ShaderDescription.CurrentPixel(
            "half4 apply(half4 color) { return half4(color.a - color.rgb, color.a); }");

        private readonly MaterializedSourceNode _source;
        private readonly ShaderStageNode _gamma = new(s_gamma);
        private readonly OpacityRenderNode _opacity = new(0.625f);
        private readonly ShaderStageNode _invert = new(s_invert);

        public PrimaryChainNode(RenderTarget source, Rect bounds)
        {
            _source = new MaterializedSourceNode(source, bounds);
        }

        public int[] ProcessCounts =>
        [
            _source.ProcessCalls,
            _gamma.ProcessCalls,
            _invert.ProcessCalls,
        ];

        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle current = context.RecordNode(_source, []).Single();
            current = context.RecordNode(_gamma, [current]).Single();
            current = context.RecordNode(_opacity, [current]).Single();
            current = context.RecordNode(_invert, [current]).Single();
            context.Publish(current);
        }

        protected override void OnDispose(bool disposing)
        {
            _invert.Dispose();
            _opacity.Dispose();
            _gamma.Dispose();
            _source.Dispose();
            base.OnDispose(disposing);
        }
    }

    private sealed class MaterializedSourceNode(RenderTarget source, Rect bounds) : RenderNode
    {
        public int ProcessCalls { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            ProcessCalls++;
            RenderResource<RenderTarget> target = context.Borrow(source, "primary-fusion-source", 1);
            context.Publish(context.MaterializedInput(
                MaterializedInputDescription.FromRenderTarget(
                    target,
                    bounds,
                    EffectiveScale.At(1),
                    PixelRect.FromRect(bounds, 1),
                    default,
                    RenderHitTestContract.OutputBounds)));
        }
    }

    private sealed class ShaderStageNode(ShaderDescription description) : RenderNode
    {
        public int ProcessCalls { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            ProcessCalls++;
            Assert.That(context.Inputs, Has.Exactly(1).Items);
            context.Publish(context.Shader(context.Inputs[0], description));
        }
    }

    private sealed class LongShaderChainNode : RenderNode
    {
        private static readonly ShaderDescription s_shader = ShaderDescription.CurrentPixel(
            "half4 apply(half4 color) { return color; }");

        private readonly RectangleRenderNode _source = new(
            new Rect(3, 5, 12, 8),
            Brushes.Resource.White,
            pen: null);
        private readonly ShaderStageNode[] _stages;

        public LongShaderChainNode(int stageCount)
        {
            _stages = Enumerable.Range(0, stageCount)
                .Select(static _ => new ShaderStageNode(s_shader))
                .ToArray();
        }

        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle current = context.RecordNode(_source, []).Single();
            foreach (ShaderStageNode stage in _stages)
                current = context.RecordNode(stage, [current]).Single();
            context.Publish(current);
        }

        protected override void OnDispose(bool disposing)
        {
            foreach (ShaderStageNode stage in _stages)
                stage.Dispose();
            _source.Dispose();
            base.OnDispose(disposing);
        }
    }

    private sealed class CpuTargetFactory : IRenderTargetFactory
    {
        public RenderTarget CreateCpuTarget(PixelSize deviceSize)
            => CreateCore(deviceSize);

        public RenderTarget Create(RenderTargetAllocationDescriptor allocation)
            => CreateCore(allocation.DeviceSize);

        private static RenderTarget CreateCore(PixelSize deviceSize)
        {
            SKSurface surface = SKSurface.Create(new SKImageInfo(
                    deviceSize.Width,
                    deviceSize.Height,
                    SKColorType.RgbaF16,
                    SKAlphaType.Premul,
                    SKColorSpace.CreateSrgbLinear()))
                ?? throw new InvalidOperationException("Could not create the CPU fusion-test surface.");
            return new CpuRenderTarget(surface, deviceSize);
        }
    }

    private sealed class CpuRenderTarget(SKSurface surface, PixelSize size)
        : RenderTarget(surface, size.Width, size.Height)
    {
    }

    private sealed class TextureRenderTarget : RenderTarget
    {
        private ITexture2D? _texture;

        public TextureRenderTarget(ITexture2D texture)
            : base(texture.CreateSkiaSurface(), texture.Width, texture.Height)
        {
            _texture = texture;
        }

        protected override void Dispose(bool disposing)
        {
            try
            {
                base.Dispose(disposing);
            }
            finally
            {
                if (disposing)
                    Interlocked.Exchange(ref _texture, null)?.Dispose();
            }
        }
    }

    private sealed class VectorTerminalShaderNode(bool publishTwice) : RenderNode
    {
        private static readonly ShaderDescription s_shader = ShaderDescription.CurrentPixel(
            "half4 apply(half4 color) { return half4(color.bgr, color.a); }");

        private readonly EllipseRenderNode _source = new(
            new Rect(4.25f, 6.5f, 9.5f, 5.75f),
            Brushes.Resource.White,
            pen: null);
        private readonly ShaderStageNode _shader = new(s_shader);

        public EffectiveScale OutputScale { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle current = context.RecordNode(_source, []).Single();
            current = context.RecordNode(_shader, [current]).Single();
            if (!current.TryGetMetadata(out RenderFragmentMetadata metadata))
                throw new InvalidOperationException("The finite shader output must expose concrete metadata.");
            OutputScale = metadata.EffectiveScale;
            context.Publish(current);
            if (publishTwice)
                context.Publish(current);
        }

        protected override void OnDispose(bool disposing)
        {
            _shader.Dispose();
            _source.Dispose();
            base.OnDispose(disposing);
        }
    }

    private static CompiledRenderRequest CompilePrimaryChain(
        FusionMode fusionMode,
        bool publishFirstShader = false)
    {
        var options = new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Frame,
            fusionMode: fusionMode);
        var request = new RenderRequest(options);

        RenderFragmentReference source = Fragment(
            RenderFragmentKind.MaterializedInput,
            EffectiveScale.At(1),
            payload: null);
        ShaderDescription gamma = ShaderDescription.CurrentPixel(
            "half4 apply(half4 color) { return half4(sqrt(color.rgb), color.a); }");
        RenderFragmentReference firstShader = Fragment(
            RenderFragmentKind.Shader,
            EffectiveScale.At(1),
            new ShaderRenderFragmentPayload(gamma, gamma.CreateRuntimeIdentity()),
            source);
        RenderFragmentReference opacity = Fragment(
            RenderFragmentKind.Opacity,
            EffectiveScale.At(1),
            new OpacityRenderFragmentPayload(
                0.625f,
                OpacityRenderNode.CreateFusionDescription(0.625f)),
            firstShader);
        ShaderDescription invert = ShaderDescription.CurrentPixel(
            "half4 apply(half4 color) { return half4(color.a - color.rgb, color.a); }");
        RenderFragmentReference secondShader = Fragment(
            RenderFragmentKind.Shader,
            EffectiveScale.At(1),
            new ShaderRenderFragmentPayload(invert, invert.CreateRuntimeIdentity()),
            opacity);

        RecordedRenderGraph graph = BuildGraph(
            request.Id,
            [source, firstShader, opacity, secondShader],
            publishFirstShader ? [firstShader, secondShader] : [secondShader]);
        request.TransitionTo(RenderRequestState.Recording);
        request.TransitionTo(RenderRequestState.Recorded);
        return new RenderRequestCompiler().Compile(request, graph);
    }

    private static RenderFragmentReference Fragment(
        RenderFragmentKind kind,
        EffectiveScale scale,
        object? payload,
        params RenderFragmentReference[] inputs)
    {
        return new RenderFragmentReference(
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
    }

    private static RecordedRenderGraph BuildGraph(
        RenderRequestId requestId,
        IReadOnlyList<RenderFragmentReference> references,
        IReadOnlyList<RenderFragmentReference> roots)
    {
        var builder = new RecordedRenderGraphBuilder(requestId);
        RenderProvenanceId provenance = builder.AddProvenance(typeof(CrossNodeShaderFusionTests), "test");
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
}
