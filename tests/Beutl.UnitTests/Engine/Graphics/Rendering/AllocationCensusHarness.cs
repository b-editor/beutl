using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

/// <summary>
/// Temporary measurement harness: splits one frame of the representative scene into the phases of
/// <see cref="RenderNodeRenderer.Rasterize"/> and reports the allocation of each.
/// </summary>
[TestFixture]
[Explicit("Measurement harness, not an assertion.")]
public sealed class AllocationCensusHarness
{
    private const int Frames = 200;
    private const int WarmupFrames = 20;
    private static readonly PixelSize s_frameSize = new(240, 160);

    [Test]
    [NonParallelizable]
    public void PhaseCensus_CacheDisabled()
    {
        string report = RenderThread.Dispatcher.Invoke(static () => RunCensus(warmCache: false));
        TestContext.Out.WriteLine(report);
    }

    [Test]
    [NonParallelizable]
    public void PhaseCensus_CacheActive()
    {
        string report = RenderThread.Dispatcher.Invoke(static () => RunCensus(warmCache: true));
        TestContext.Out.WriteLine(report);
    }

    /// <summary>Ground truth: the unmodified public entry point, for cross-checking the phase sum.</summary>
    [Test]
    [NonParallelizable]
    public void WholeFrameBaseline()
    {
        string report = RenderThread.Dispatcher.Invoke(static () =>
        {
            long disabled = MeasureWholeFrame(warmCache: false);
            long warm = MeasureWholeFrame(warmCache: true);
            return $"Rasterize() cache-disabled: {disabled} bytes/frame\n"
                   + $"Rasterize() cache-active : {warm} bytes/frame";
        });
        TestContext.Out.WriteLine(report);
    }

    /// <summary>Separates the per-node slope from the per-frame intercept by varying the node count.</summary>
    [Test]
    [NonParallelizable]
    public void NodeScaling()
    {
        string report = RenderThread.Dispatcher.Invoke(static () =>
        {
            var lines = new List<string>
            {
                "=== NODE SCALING (uniform RectShape scene, cache DISABLED) ===",
                $"{"rects",6} {"fragments",10} {"values",8} {"resources",10} {"islands",8} "
                + $"{"RECORD",10} {"METADATA",10} {"COMPILE",10} {"EXECUTE",10} {"TOTAL",10}",
            };
            int[] counts = [1, 2, 4, 8, 16, 32];
            foreach (int count in counts)
                lines.Add(MeasureUniformScene(count));

            return string.Join('\n', lines);
        });
        TestContext.Out.WriteLine(report);
    }

    private static string MeasureUniformScene(int rectCount)
    {
        var shapes = new List<Drawable.Resource>();
        CompositionContext compositionContext = CompositionContext.Default;
        for (int index = 0; index < rectCount; index++)
        {
            var rect = new RectShape
            {
                Width = { CurrentValue = 20 },
                Height = { CurrentValue = 20 },
                Fill = { CurrentValue = Brushes.CornflowerBlue },
                Transform = { CurrentValue = new TranslateTransform(index, index) },
            };
            shapes.Add(rect.ToResource(compositionContext));
        }

        Drawable.Resource[] resources = [.. shapes];
        var totals = new long[PhaseNames.Length];
        int fragments = 0, values = 0, resourceCount = 0, islands = 0;
        try
        {
            using var root = new DrawableRenderNode(resources[0]);
            RecordScene(root, resources);

            var registry = new RenderTargetLeaseRegistry(new CpuTargetFactory());
            using var structuralPlanCache = new StructuralPlanCache();
            using ProgramCache<CachedSkRuntimeEffect> programCache = SkRuntimeEffectProgramCache.Create();
            using ProgramCache<GLSLFilterPipeline> spirvProgramCache = SpirvShaderProgramCache.Create();
            try
            {
                for (int frame = 0; frame < WarmupFrames; frame++)
                {
                    RunOneFrame(root, registry, structuralPlanCache, programCache, spirvProgramCache,
                        RenderCacheOptions.Disabled, false, null);
                }

                for (int frame = 0; frame < Frames; frame++)
                {
                    RunOneFrame(root, registry, structuralPlanCache, programCache, spirvProgramCache,
                        RenderCacheOptions.Disabled, false, totals);
                }

                CountGraph(root, registry, structuralPlanCache, programCache, spirvProgramCache,
                    ref fragments, ref values, ref resourceCount, ref islands);
            }
            finally
            {
                registry.Dispose();
            }
        }
        finally
        {
            foreach (Drawable.Resource resource in resources)
                resource.Dispose();
        }

        long sum = 0;
        foreach (long value in totals)
            sum += value;

        return $"{rectCount,6} {fragments,10} {values,8} {resourceCount,10} {islands,8} "
               + $"{totals[3] / Frames,10:N0} {totals[4] / Frames,10:N0} {totals[5] / Frames,10:N0} "
               + $"{totals[7] / Frames,10:N0} {sum / Frames,10:N0}";
    }

    private static void CountGraph(
        RenderNode root,
        RenderTargetLeaseRegistry registry,
        StructuralPlanCache structuralPlanCache,
        ProgramCache<CachedSkRuntimeEffect> programCache,
        ProgramCache<GLSLFilterPipeline> spirvProgramCache,
        ref int fragments,
        ref int values,
        ref int resources,
        ref int islands)
    {
        using RenderTargetLeaseSession targets = registry.BeginSession(RenderIntent.Preview);
        var request = new RenderRequest(new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Frame,
            new Rect(default, s_frameSize.ToSize(1)),
            null,
            1,
            float.PositiveInfinity,
            RenderCacheOptions.Disabled,
            FusionMode.Enabled));
        var recorder = new RenderRequestRecorder(request);
        RecordedRenderGraph graph = recorder.Record(root);
        fragments = graph.Fragments.Length;
        values = graph.Values.Length;
        resources = graph.Resources.Length;

        var cacheContext = new RenderCacheResolutionContext(
            RenderCacheFormatIdentity.LinearPremultipliedRgba16Float,
            targets.CacheDeviceContextIdentity,
            false,
            false);
        var compiler = new RenderRequestCompiler(structuralPlanCache, cacheContext, null);
        using CompiledRenderRequest compiled = compiler.Compile(
            request,
            graph,
            SkslBackendBudgetResolver.Portable);
        islands = compiled.ExecutionPlan.Islands.Length;
    }

    /// <summary>
    /// Long allocation loop for an external profiler. CENSUS_DELAY seconds elapse before the loop starts
    /// so a tracer can attach, and CENSUS_FRAMES frames run inside it.
    /// </summary>
    [Test]
    [NonParallelizable]
    public void TraceLoop()
    {
        int delay = int.Parse(Environment.GetEnvironmentVariable("CENSUS_DELAY") ?? "0");
        int frames = int.Parse(Environment.GetEnvironmentVariable("CENSUS_FRAMES") ?? "2000");
        string report = RenderThread.Dispatcher.Invoke(() =>
        {
            Drawable.Resource[] resources = CreateSceneResources();
            try
            {
                using var root = new DrawableRenderNode(resources[0]);
                RecordScene(root, resources);
                using var renderer = new RenderNodeRenderer(root, CreateOptions(false));
                for (int frame = 0; frame < WarmupFrames; frame++)
                    renderer.Rasterize().Dispose();

                Thread.Sleep(TimeSpan.FromSeconds(delay));
                string? sentinel = Environment.GetEnvironmentVariable("CENSUS_SENTINEL");
                if (sentinel is not null)
                    File.WriteAllText(sentinel, "go");

                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int frame = 0; frame < frames; frame++)
                    renderer.Rasterize().Dispose();

                long after = GC.GetAllocatedBytesForCurrentThread();
                return $"traced {frames} frames, {(after - before) / frames} bytes/frame";
            }
            finally
            {
                foreach (Drawable.Resource resource in resources)
                    resource.Dispose();
            }
        });
        TestContext.Out.WriteLine(report);
    }

    /// <summary>Reports the recorded-graph object counts for the representative scene.</summary>
    [Test]
    [NonParallelizable]
    public void RepresentativeSceneShape()
    {
        string report = RenderThread.Dispatcher.Invoke(static () =>
        {
            Drawable.Resource[] resources = CreateSceneResources();
            try
            {
                using var root = new DrawableRenderNode(resources[0]);
                RecordScene(root, resources);
                var registry = new RenderTargetLeaseRegistry(new CpuTargetFactory());
                using var structuralPlanCache = new StructuralPlanCache();
                using ProgramCache<CachedSkRuntimeEffect> programCache = SkRuntimeEffectProgramCache.Create();
                using ProgramCache<GLSLFilterPipeline> spirv = SpirvShaderProgramCache.Create();
                int fragments = 0, values = 0, resourceCount = 0, islands = 0;
                try
                {
                    CountGraph(root, registry, structuralPlanCache, programCache, spirv,
                        ref fragments, ref values, ref resourceCount, ref islands);
                }
                finally
                {
                    registry.Dispose();
                }

                return $"representative scene: fragments={fragments} values={values} "
                       + $"resources={resourceCount} islands={islands}";
            }
            finally
            {
                foreach (Drawable.Resource resource in resources)
                    resource.Dispose();
            }
        });
        TestContext.Out.WriteLine(report);
    }

    private static long MeasureWholeFrame(bool warmCache)
    {
        Drawable.Resource[] resources = CreateSceneResources();
        try
        {
            using var root = new DrawableRenderNode(resources[0]);
            RecordScene(root, resources);
            using var renderer = new RenderNodeRenderer(root, CreateOptions(warmCache));
            var revalidated = new HashSet<RenderNode>(ReferenceEqualityComparer.Instance);
            for (int frame = 0; frame < WarmupFrames; frame++)
            {
                if (warmCache)
                    IncrementRenderCounts(root, revalidated);
                renderer.Rasterize().Dispose();
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int frame = 0; frame < Frames; frame++)
            {
                if (warmCache)
                    IncrementRenderCounts(root, revalidated);
                renderer.Rasterize().Dispose();
            }

            long after = GC.GetAllocatedBytesForCurrentThread();
            return (after - before) / Frames;
        }
        finally
        {
            foreach (Drawable.Resource resource in resources)
                resource.Dispose();
        }
    }

    private static RenderNodeRendererOptions CreateOptions(bool warmCache)
        => new()
        {
            DefaultRequest = new RenderNodeRenderRequest
            {
                TargetDomain = new Rect(default, s_frameSize.ToSize(1)),
                CacheOptions = warmCache ? RenderCacheOptions.Enabled : RenderCacheOptions.Disabled,
                Purpose = RenderRequestPurpose.Frame,
            },
            TargetFactory = new CpuTargetFactory(),
        };

    private static string RunCensus(bool warmCache)
    {
        Drawable.Resource[] resources = CreateSceneResources();
        var totals = new long[PhaseNames.Length];
        try
        {
            using var root = new DrawableRenderNode(resources[0]);
            RecordScene(root, resources);

            var registry = new RenderTargetLeaseRegistry(new CpuTargetFactory());
            using var structuralPlanCache = new StructuralPlanCache();
            using ProgramCache<CachedSkRuntimeEffect> programCache = SkRuntimeEffectProgramCache.Create();
            using ProgramCache<GLSLFilterPipeline> spirvProgramCache = SpirvShaderProgramCache.Create();
            var revalidated = new HashSet<RenderNode>(ReferenceEqualityComparer.Instance);
            RenderCacheOptions cacheOptions =
                warmCache ? RenderCacheOptions.Enabled : RenderCacheOptions.Disabled;

            try
            {
                for (int frame = 0; frame < WarmupFrames; frame++)
                {
                    if (warmCache)
                        IncrementRenderCounts(root, revalidated);
                    RunOneFrame(root, registry, structuralPlanCache, programCache, spirvProgramCache,
                        cacheOptions, warmCache, null);
                }

                for (int frame = 0; frame < Frames; frame++)
                {
                    if (warmCache)
                        IncrementRenderCounts(root, revalidated);
                    RunOneFrame(root, registry, structuralPlanCache, programCache, spirvProgramCache,
                        cacheOptions, warmCache, totals);
                }
            }
            finally
            {
                registry.Dispose();
            }
        }
        finally
        {
            foreach (Drawable.Resource resource in resources)
                resource.Dispose();
        }

        return FormatReport(warmCache, totals);
    }

    private static readonly string[] PhaseNames =
    [
        "1 BeginSession (target lease session)",
        "2 BeginLifecycle (render-node cache)",
        "3 RenderRequest + RenderRequestOptions",
        "4 RECORD  (RenderRequestRecorder.Record)",
        "5 METADATA (compiler.ResolveMetadata)",
        "6 COMPILE  (compiler.CompileAfterMetadata)",
        "7 Target acquire + canvas + clear",
        "8 EXECUTE (RenderRequestExecutor.Execute)",
        "9 Teardown / dispose",
    ];

    private static void RunOneFrame(
        RenderNode root,
        RenderTargetLeaseRegistry registry,
        StructuralPlanCache structuralPlanCache,
        ProgramCache<CachedSkRuntimeEffect> programCache,
        ProgramCache<GLSLFilterPipeline> spirvProgramCache,
        RenderCacheOptions cacheOptions,
        bool warmCache,
        long[]? totals)
    {
        int phase = 0;
        long mark = GC.GetAllocatedBytesForCurrentThread();

        void Mark()
        {
            long now = GC.GetAllocatedBytesForCurrentThread();
            if (totals is not null)
                totals[phase] += now - mark;
            phase++;
            mark = now;
        }

        RenderTargetLeaseSession targets = registry.BeginSession(RenderIntent.Preview);
        Mark();

        RenderNodeCacheLifecycle lifecycle = RenderNodeCacheHelper.BeginLifecycle(
            root,
            cacheOptions.IsEnabled);
        Mark();

        var request = new RenderRequest(new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Frame,
            new Rect(default, s_frameSize.ToSize(1)),
            null,
            1,
            float.PositiveInfinity,
            cacheOptions,
            FusionMode.Enabled));
        Mark();

        var recorder = new RenderRequestRecorder(request);
        RecordedRenderGraph graph = recorder.Record(root);
        Mark();

        var cacheContext = new RenderCacheResolutionContext(
            RenderCacheFormatIdentity.LinearPremultipliedRgba16Float,
            targets.CacheDeviceContextIdentity,
            warmCache,
            warmCache);
        var compiler = new RenderRequestCompiler(
            structuralPlanCache,
            cacheContext,
            warmCache ? RenderNodeCacheLookup.Instance : null);
        RenderNodeMeasurement measurement = compiler.ResolveMetadata(request, graph);
        Mark();

        SkslBackendBudget shaderBudget = SkslBackendBudgetResolver.Resolve(
            targets.ExternalTarget?.RawValue.Context?.Backend);
        CompiledRenderRequest compiled =
            compiler.CompileAfterMetadata(request, graph, measurement, shaderBudget);
        Mark();

        Rect selectedBounds = compiled.SelectedOutputBounds;
        Bitmap? bitmap = null;
        PixelRect deviceBounds = PixelRect.FromRect(compiled.ExecutionTargetBounds, 1);
        PixelRect selectedDeviceBounds = PixelRect.FromRect(selectedBounds, 1);
        Rect rasterBounds = deviceBounds.ToRect(1);
        RenderTargetLease rootLease = targets.Acquire(deviceBounds.Size);
        ImmediateCanvas canvas = ImmediateCanvas.CreateExecutorManaged(
            rootLease.Target,
            1,
            float.PositiveInfinity,
            rasterBounds.Size,
            RenderIntent.Preview,
            deviceBounds.Position);
        canvas.Clear();
        IDisposable? transform = canvas.PushTransform(
            Matrix.CreateTranslation(-rasterBounds.X, -rasterBounds.Y));
        Mark();

        var executor = new RenderRequestExecutor(targets, programCache, spirvProgramCache: spirvProgramCache);
        executor.Execute(
            compiled,
            canvas,
            () =>
            {
                transform?.Dispose();
                transform = null;
                var selectedSubset = new PixelRect(
                    selectedDeviceBounds.X - deviceBounds.X,
                    selectedDeviceBounds.Y - deviceBounds.Y,
                    selectedDeviceBounds.Width,
                    selectedDeviceBounds.Height);
                Bitmap complete = rootLease.Target.Snapshot();
                bitmap = RenderNodeRenderer.TakeRasterizationBitmap(complete, selectedSubset);
            },
            compiled.ExecutionTargetBounds);
        Mark();

        transform?.Dispose();
        canvas.Dispose();
        rootLease.Dispose();
        compiled.Dispose();
        targets.Dispose();
        lifecycle.CompleteSuccessfully(true);
        bitmap?.Dispose();
        Mark();
    }

    private static string FormatReport(bool warmCache, long[] totals)
    {
        long sum = 0;
        foreach (long value in totals)
            sum += value;

        var lines = new List<string>
        {
            $"=== PHASE CENSUS ({(warmCache ? "cache ACTIVE" : "cache DISABLED")}), {Frames} frames ===",
        };
        for (int index = 0; index < totals.Length; index++)
        {
            long perFrame = totals[index] / Frames;
            double share = sum == 0 ? 0 : 100.0 * totals[index] / sum;
            lines.Add($"  {PhaseNames[index],-42} {perFrame,10:N0} B/frame  {share,5:F1}%");
        }

        lines.Add($"  {"TOTAL (decomposed)",-42} {sum / Frames,10:N0} B/frame");
        return string.Join('\n', lines);
    }

    private static void RecordScene(DrawableRenderNode root, Drawable.Resource[] resources)
    {
        using var context = new GraphicsContext2D(root, s_frameSize.ToSize(1));
        context.Clear();
        foreach (Drawable.Resource resource in resources)
            context.DrawDrawable(resource);
    }

    private static void IncrementRenderCounts(RenderNode root, HashSet<RenderNode> revalidated)
    {
        revalidated.Clear();
        Visit(root);
        return;

        void Visit(RenderNode current)
        {
            if (current.IsDisposed || !revalidated.Add(current))
                return;

            ReadOnlySpan<RenderNode> children = current.ChildNodes;
            for (int index = 0; index < children.Length; index++)
                Visit(children[index]);

            current.HasChanges = false;
        }
    }

    private static Drawable.Resource[] CreateSceneResources()
    {
        var background = new RectShape
        {
            Width = { CurrentValue = s_frameSize.Width },
            Height = { CurrentValue = s_frameSize.Height },
            Fill = { CurrentValue = Brushes.CornflowerBlue },
        };

        var accent = new EllipseShape
        {
            Width = { CurrentValue = 76 },
            Height = { CurrentValue = 76 },
            Fill = { CurrentValue = Brushes.OrangeRed },
            FilterEffect = { CurrentValue = new Brightness { Amount = { CurrentValue = 78 } } },
            Transform = { CurrentValue = new TranslateTransform(44, -18) },
        };

        var label = new TextBlock
        {
            FontFamily = { CurrentValue = FontFamily.Default },
            Size = { CurrentValue = 28 },
            Fill = { CurrentValue = Brushes.White },
            Text = { CurrentValue = "CACHE" },
            Transform = { CurrentValue = new TranslateTransform(-28, 30) },
        };

        CompositionContext context = CompositionContext.Default;
        return
        [
            background.ToResource(context),
            accent.ToResource(context),
            label.ToResource(context),
        ];
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
