using System.Collections.Immutable;
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
                Intent = RenderIntent.Preview,
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

            current.ClearChanges(current.ChangeVersion);
        }
    }

    // ==== What the render-node recording cache actually serves ====

    private enum RecordingOutcome
    {
        /// <summary>Replayed: the node's Process was not called.</summary>
        Skipped,

        /// <summary>Re-recorded although its own recording repeated, because the cache refuses to store it.</summary>
        RefusedRepeat,

        /// <summary>Re-recorded, refused, and its recording did not repeat.</summary>
        RefusedFresh,

        /// <summary>Re-recorded although the retained recording was replayable: the gate rejected reuse.</summary>
        RejectedReplayable,

        /// <summary>Not reached this frame.</summary>
        NotVisited,
    }

    private static readonly string[] OutcomeNames =
        ["SKIPPED", "REFUSED(repeat)", "REFUSED(fresh)", "REJECTED(replayable)", "not visited"];

    [Test]
    [NonParallelizable]
    public void RecordingCacheEffect()
    {
        string report = RenderThread.Dispatcher.Invoke(static () =>
            MeasureRecordingCacheEffect(warmCache: false)
            + "\n\n"
            + MeasureRecordingCacheEffect(warmCache: true));
        TestContext.Out.WriteLine(report);
    }

    private static string MeasureRecordingCacheEffect(bool warmCache)
    {
        Drawable.Resource[] resources = CreateSceneResources();
        var lines = new List<string>
        {
            $"=== RECORDING-CACHE EFFECT ({(warmCache ? "render cache ACTIVE" : "render cache DISABLED")}), "
            + $"{Frames} frames ===",
        };
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

            void Frame(long[]? totals)
            {
                if (warmCache)
                    IncrementRenderCounts(root, revalidated);
                RunOneFrame(root, registry, structuralPlanCache, programCache, spirvProgramCache,
                    cacheOptions, warmCache, totals);
            }

            try
            {
                for (int frame = 0; frame < WarmupFrames; frame++)
                    Frame(null);

                var nodes = new List<RenderNode>();
                CollectNodes(root, nodes, new HashSet<RenderNode>(ReferenceEqualityComparer.Instance));

                // Visit detection: a node that has no retained recording before the frame and one after it
                // was reached. This also re-records everything, so the next frames restart from cold.
                foreach (RenderNode node in nodes)
                    node.RecordingSnapshot = null;
                Frame(null);
                var visited = new HashSet<RenderNode>(ReferenceEqualityComparer.Instance);
                foreach (RenderNode node in nodes)
                {
                    if (node.RecordingSnapshot is not null)
                        visited.Add(node);
                }

                for (int frame = 0; frame < WarmupFrames; frame++)
                    Frame(null);

                // Steady-state classification.
                var tally = new Dictionary<string, int[]>(StringComparer.Ordinal);
                var before = new RenderNodeRecordingSnapshot?[nodes.Count];
                for (int frame = 0; frame < Frames; frame++)
                {
                    for (int index = 0; index < nodes.Count; index++)
                        before[index] = nodes[index].RecordingSnapshot;

                    Frame(null);

                    for (int index = 0; index < nodes.Count; index++)
                    {
                        RenderNodeRecordingSnapshot? after = nodes[index].RecordingSnapshot;
                        RecordingOutcome outcome = Classify(before[index], after, visited.Contains(nodes[index]));
                        string name = nodes[index].GetType().Name;
                        if (!tally.TryGetValue(name, out int[]? counts))
                            tally[name] = counts = new int[OutcomeNames.Length];
                        counts[(int)outcome]++;
                    }
                }

                lines.Add($"  nodes in tree: {nodes.Count}, reached per frame: {visited.Count}");
                lines.Add(string.Empty);
                lines.Add($"  {"node type",-34} {"SKIP",6} {"REF-rep",8} {"REF-new",8} {"REJECT",7} {"n/v",5}");
                var totalsByOutcome = new int[OutcomeNames.Length];
                foreach ((string name, int[] counts) in tally.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    for (int index = 0; index < counts.Length; index++)
                        totalsByOutcome[index] += counts[index];
                    lines.Add($"  {name,-34} {counts[0] / (double)Frames,6:F2} {counts[1] / (double)Frames,8:F2} "
                              + $"{counts[2] / (double)Frames,8:F2} {counts[3] / (double)Frames,7:F2} "
                              + $"{counts[4] / (double)Frames,5:F2}");
                }

                lines.Add($"  {"TOTAL visits/frame",-34} {totalsByOutcome[0] / (double)Frames,6:F2} "
                          + $"{totalsByOutcome[1] / (double)Frames,8:F2} {totalsByOutcome[2] / (double)Frames,8:F2} "
                          + $"{totalsByOutcome[3] / (double)Frames,7:F2} {totalsByOutcome[4] / (double)Frames,5:F2}");

                int recorded = totalsByOutcome[1] + totalsByOutcome[2] + totalsByOutcome[3];
                double hitRate = recorded + totalsByOutcome[0] == 0
                    ? 0
                    : 100.0 * totalsByOutcome[0] / (recorded + totalsByOutcome[0]);
                lines.Add($"  hit rate: {hitRate:F1}% of reached visits skip Process");

                // What the served nodes replay, and what that replay costs.
                var served = new List<RenderNodeRecordingSnapshot>();
                int replayedFragments = 0;
                foreach (RenderNode node in nodes)
                {
                    RenderNodeRecordingSnapshot? snapshot = node.RecordingSnapshot;
                    if (snapshot is { IsReplayable: true, Fragments: { } fragments })
                    {
                        served.Add(snapshot);
                        replayedFragments += fragments.Length;
                    }
                }

                long replayCost = MeasureReplayCost(served, 2000);
                lines.Add(string.Empty);
                lines.Add($"  replayable snapshots: {served.Count}, fragments replayed/frame: {replayedFragments}");
                lines.Add($"  replay clone cost (mirrors ReplayRecording's allocating body): "
                          + $"{replayCost:N0} B/frame");

                // RECORD-phase ablations.
                long recordCached = MeasureRecordPhase(Frame, nodes, AblationMode.None);
                long recordNoReuse = MeasureRecordPhase(Frame, nodes, AblationMode.ClearAll);
                long recordNoServe = MeasureRecordPhase(Frame, nodes, AblationMode.ClearReplayable);
                lines.Add(string.Empty);
                lines.Add($"  RECORD, cache serving              : {recordCached,10:N0} B/frame");
                lines.Add($"  RECORD, every snapshot cleared     : {recordNoReuse,10:N0} B/frame");
                lines.Add($"  RECORD, replayable snapshots clear : {recordNoServe,10:N0} B/frame");
                lines.Add($"  delivered saving (cleared - served): {recordNoReuse - recordCached,10:N0} B/frame");
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

        return string.Join('\n', lines);
    }

    private enum AblationMode
    {
        None,
        ClearAll,
        ClearReplayable,
    }

    private static long MeasureRecordPhase(Action<long[]?> frame, List<RenderNode> nodes, AblationMode mode)
    {
        void Clear()
        {
            if (mode == AblationMode.None)
                return;
            foreach (RenderNode node in nodes)
            {
                if (mode == AblationMode.ClearAll || node.RecordingSnapshot is { IsReplayable: true })
                    node.RecordingSnapshot = null;
            }
        }

        for (int index = 0; index < WarmupFrames; index++)
        {
            Clear();
            frame(null);
        }

        var totals = new long[PhaseNames.Length];
        for (int index = 0; index < Frames; index++)
        {
            Clear();
            frame(totals);
        }

        return totals[3] / Frames;
    }

    /// <summary>
    /// Mirrors the allocating body of <c>NodeRecordingTransaction.ReplayRecording</c>: the clone array, one
    /// input array per fragment, and one fragment clone. The set adds and list adds it also performs are left
    /// out because the recording path performs them too.
    /// </summary>
    private static long MeasureReplayCost(List<RenderNodeRecordingSnapshot> served, int iterations)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < iterations; iteration++)
        {
            foreach (RenderNodeRecordingSnapshot snapshot in served)
            {
                ReplayedRenderFragment[] fragments = snapshot.Fragments!;
                RenderFragmentReference[] replayed = fragments.Length == 0
                    ? []
                    : new RenderFragmentReference[fragments.Length];
                for (int index = 0; index < fragments.Length; index++)
                {
                    ReplayedRenderFragment fragment = fragments[index];
                    int[] slots = fragment.InputSlots;
                    ImmutableArray<RenderFragmentReference> inputs;
                    if (slots.Length == 0)
                    {
                        inputs = [];
                    }
                    else
                    {
                        var builder = ImmutableArray.CreateBuilder<RenderFragmentReference>(slots.Length);
                        for (int slot = 0; slot < slots.Length; slot++)
                            builder.Add(fragment.Template);
                        inputs = builder.MoveToImmutable();
                    }

                    replayed[index] = fragment.Template.CloneForReplay(inputs);
                }
            }
        }

        long after = GC.GetAllocatedBytesForCurrentThread();
        return (after - before) / iterations;
    }

    private static RecordingOutcome Classify(
        RenderNodeRecordingSnapshot? before,
        RenderNodeRecordingSnapshot? after,
        bool reached)
    {
        if (!reached || after is null)
            return RecordingOutcome.NotVisited;

        bool retained = ReferenceEquals(before, after);
        if (after.IsReplayable)
            return retained ? RecordingOutcome.Skipped : RecordingOutcome.RejectedReplayable;

        return retained ? RecordingOutcome.RefusedRepeat : RecordingOutcome.RefusedFresh;
    }

    private static void CollectNodes(RenderNode node, List<RenderNode> into, HashSet<RenderNode> seen)
    {
        if (node.IsDisposed || !seen.Add(node))
            return;

        into.Add(node);
        ReadOnlySpan<RenderNode> children = node.ChildNodes;
        for (int index = 0; index < children.Length; index++)
            CollectNodes(children[index], into, seen);
    }

    [Test]
    [NonParallelizable]
    public void SkippedVisitMarginalCost()
    {
        string report = RenderThread.Dispatcher.Invoke(static () =>
        {
            var lines = new List<string>
            {
                "=== MARGINAL COST OF ONE WRAPPER VISIT (representative scene + N wrappers, "
                + "render cache DISABLED) ===",
                $"  {"wrapper",-22} {"N",4} {"RECORD served",14} {"RECORD forced",14} "
                + $"{"saved by skip",14} {"skipped",8}",
            };
            foreach (bool useTransform in new[] { false, true })
            {
                foreach (int depth in new[] { 0, 8, 16, 32 })
                    lines.Add(MeasureWrapperDepth(useTransform, depth));
            }

            return string.Join('\n', lines);
        });
        TestContext.Out.WriteLine(report);
    }

    private static string MeasureWrapperDepth(bool useTransform, int depth)
    {
        Drawable.Resource[] resources = CreateSceneResources();
        try
        {
            using var scene = new DrawableRenderNode(resources[0]);
            RecordScene(scene, resources);

            RenderNode top = scene;
            for (int index = 0; index < depth; index++)
            {
                ContainerRenderNode wrapper = useTransform
                    ? new TransformRenderNode(Matrix.Identity, TransformOperator.Prepend)
                    : new ContainerRenderNode();
                wrapper.AddChild(top);
                top = wrapper;
            }

            var registry = new RenderTargetLeaseRegistry(new CpuTargetFactory());
            using var structuralPlanCache = new StructuralPlanCache();
            using ProgramCache<CachedSkRuntimeEffect> programCache = SkRuntimeEffectProgramCache.Create();
            using ProgramCache<GLSLFilterPipeline> spirvProgramCache = SpirvShaderProgramCache.Create();
            try
            {
                void Frame(long[]? totals) => RunOneFrame(top, registry, structuralPlanCache, programCache,
                    spirvProgramCache, RenderCacheOptions.Disabled, false, totals);

                var nodes = new List<RenderNode>();
                CollectNodes(top, nodes, new HashSet<RenderNode>(ReferenceEqualityComparer.Instance));

                long served = MeasureRecordPhase(Frame, nodes, AblationMode.None);

                int skipped = 0;
                foreach (RenderNode node in nodes)
                {
                    if (node.RecordingSnapshot is { IsReplayable: true })
                        skipped++;
                }

                long forced = MeasureRecordPhase(Frame, nodes, AblationMode.ClearAll);
                string name = useTransform ? "TransformRenderNode" : "ContainerRenderNode";
                return $"  {name,-22} {depth,4} {served,14:N0} {forced,14:N0} {forced - served,14:N0} "
                       + $"{skipped,8}";
            }
            finally
            {
                registry.Dispose();
                if (!ReferenceEquals(top, scene))
                    top.Dispose();
            }
        }
        finally
        {
            foreach (Drawable.Resource resource in resources)
                resource.Dispose();
        }
    }

    [Test]
    [NonParallelizable]
    public void GateOverRejection()
    {
        string report = RenderThread.Dispatcher.Invoke(static () =>
        {
            var lines = new List<string> { "=== GATE OVER-REJECTION (render cache DISABLED) ===" };
            lines.Add(MeasureDirtyLeaf(dirtyLeaf: false));
            lines.Add(MeasureDirtyLeaf(dirtyLeaf: true));
            return string.Join('\n', lines);
        });
        TestContext.Out.WriteLine(report);
    }

    private static string MeasureDirtyLeaf(bool dirtyLeaf)
    {
        Drawable.Resource[] resources = CreateSceneResources();
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
                var nodes = new List<RenderNode>();
                CollectNodes(root, nodes, new HashSet<RenderNode>(ReferenceEqualityComparer.Instance));
                RenderNode? leaf = nodes.FirstOrDefault(node => node is GeometryRenderNode);
                if (leaf is null)
                    return "  no GeometryRenderNode found";

                void Frame(long[]? totals)
                {
                    if (dirtyLeaf)
                        leaf.MarkChanged();
                    RunOneFrame(root, registry, structuralPlanCache, programCache, spirvProgramCache,
                        RenderCacheOptions.Disabled, false, totals);
                }

                for (int frame = 0; frame < WarmupFrames; frame++)
                    Frame(null);

                int skipped = 0, rejected = 0, refused = 0, sameDigests = 0;
                var before = new RenderNodeRecordingSnapshot?[nodes.Count];
                var rejectedTypes = new Dictionary<string, int>(StringComparer.Ordinal);
                for (int frame = 0; frame < Frames; frame++)
                {
                    for (int index = 0; index < nodes.Count; index++)
                        before[index] = nodes[index].RecordingSnapshot;

                    Frame(null);

                    for (int index = 0; index < nodes.Count; index++)
                    {
                        RenderNodeRecordingSnapshot? after = nodes[index].RecordingSnapshot;
                        switch (Classify(before[index], after, true))
                        {
                            case RecordingOutcome.Skipped:
                                skipped++;
                                break;
                            case RecordingOutcome.RejectedReplayable:
                                rejected++;
                                string name = nodes[index].GetType().Name;
                                if (before[index] is { } previous
                                    && after is not null
                                    && previous.InputFingerprints.AsSpan()
                                        .SequenceEqual(after.InputFingerprints))
                                {
                                    sameDigests++;
                                }

                                rejectedTypes[name] = rejectedTypes.GetValueOrDefault(name) + 1;
                                break;
                            default:
                                refused++;
                                break;
                        }
                    }
                }

                var totals = new long[PhaseNames.Length];
                for (int frame = 0; frame < Frames; frame++)
                    Frame(totals);

                string detail = rejectedTypes.Count == 0
                    ? "none"
                    : string.Join(", ", rejectedTypes
                        .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                        .Select(pair => $"{pair.Key}x{pair.Value / Frames}"));
                return $"  one leaf dirty={dirtyLeaf,-5} skip/frame={skipped / (double)Frames,6:F2} "
                       + $"sameDigests/frame={sameDigests / (double)Frames,5:F2} "
                       + $"reject/frame={rejected / (double)Frames,6:F2} "
                       + $"refused/frame={refused / (double)Frames,5:F2} "
                       + $"RECORD={totals[3] / Frames,8:N0} B/frame\n"
                       + $"      rejected-but-replayable: {detail}";
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
    }

    [Test]
    [NonParallelizable]
    public void RefusedLeafShare()
    {
        string report = RenderThread.Dispatcher.Invoke(static () =>
        {
            var lines = new List<string>
            {
                "=== RECORD BY SCENE VARIANT (render cache DISABLED) ===",
                $"  {"variant",-28} {"nodes",6} {"skip",5} {"refused",8} {"RECORD",10}",
            };
            lines.Add(MeasureVariant("all three drawables", 0b111));
            lines.Add(MeasureVariant("no text", 0b011));
            lines.Add(MeasureVariant("no ellipse+effect", 0b101));
            lines.Add(MeasureVariant("background rect only", 0b001));
            lines.Add(MeasureVariant("empty (clear only)", 0b000));
            return string.Join('\n', lines);
        });
        TestContext.Out.WriteLine(report);
    }

    private static string MeasureVariant(string label, int mask)
    {
        Drawable.Resource[] all = CreateSceneResources();
        var kept = new List<Drawable.Resource>();
        for (int index = 0; index < all.Length; index++)
        {
            if ((mask & (1 << index)) != 0)
                kept.Add(all[index]);
        }

        try
        {
            using var root = new DrawableRenderNode(all[0]);
            using (var context = new GraphicsContext2D(root, s_frameSize.ToSize(1)))
            {
                context.Clear();
                foreach (Drawable.Resource resource in kept)
                    context.DrawDrawable(resource);
            }

            var registry = new RenderTargetLeaseRegistry(new CpuTargetFactory());
            using var structuralPlanCache = new StructuralPlanCache();
            using ProgramCache<CachedSkRuntimeEffect> programCache = SkRuntimeEffectProgramCache.Create();
            using ProgramCache<GLSLFilterPipeline> spirvProgramCache = SpirvShaderProgramCache.Create();
            try
            {
                void Frame(long[]? totals) => RunOneFrame(root, registry, structuralPlanCache, programCache,
                    spirvProgramCache, RenderCacheOptions.Disabled, false, totals);

                var nodes = new List<RenderNode>();
                CollectNodes(root, nodes, new HashSet<RenderNode>(ReferenceEqualityComparer.Instance));
                long record = MeasureRecordPhase(Frame, nodes, AblationMode.None);

                int skipped = 0, refused = 0;
                foreach (RenderNode node in nodes)
                {
                    if (node.RecordingSnapshot is { } snapshot)
                    {
                        if (snapshot.IsReplayable)
                            skipped++;
                        else
                            refused++;
                    }
                }

                return $"  {label,-28} {nodes.Count,6} {skipped,5} {refused,8} {record,10:N0}";
            }
            finally
            {
                registry.Dispose();
            }
        }
        finally
        {
            foreach (Drawable.Resource resource in all)
                resource.Dispose();
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
