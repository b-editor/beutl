using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Effects;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// Runs a <see cref="CompiledPlan"/> against the graphics context (feature 004, T023, D2/D5). A plan is a
/// schedule of passes threaded over the input operation set: a descriptor pass (<see cref="FusedShaderPass"/>,
/// <see cref="SkiaFilterPass"/>) transforms each current operation independently — a fused pass executes as one
/// draw built by shader composition (input image shader → <c>WithColorFilter</c> wraps → nested
/// <c>SKRuntimeEffect</c> child shaders, adjacent snippets merged into one program), a Skia-filter pass as one
/// filtered draw. The RGBA16F premultiplied linear-light representation is preserved between stages.
/// Descriptor-pass counters follow §C8: one <see cref="PipelineDiagnostics.GpuPasses"/> per executed draw, one
/// <see cref="PipelineDiagnostics.ProgramCreations"/> per <c>SKRuntimeEffect</c> created.
/// </summary>
internal static class PlanExecutor
{
    private static readonly ILogger s_logger = Log.CreateLogger("PlanExecutor");

    public static RenderNodeOperation[] Execute(
        CompiledPlan plan,
        FrameResources resources,
        RenderNodeOperation[] inputs,
        float outputScale,
        float workingScale,
        float maxWorkingScale,
        PipelineDiagnostics? diagnostics,
        RenderTargetPool? pool,
        int startPass = 0,
        PrefixCaptureSink? captureSink = null)
    {
        // FR-007 (C3.1) measurement scope: pooled leases live before this execution belong to the caller (an outer
        // plan, a held upstream op) and are subtracted from the peak measured for this plan.
        long leaseBaseline = 0;
        if (pool != null)
        {
            leaseBaseline = pool.LiveLeaseCount;
            pool.ResetPeakLiveLeases();
        }

        // Thread the whole operation set through the schedule pass by pass, so a split/composite can fan the set out
        // and back in and each descriptor pass maps every current operation.
        var current = new List<RenderNodeOperation>(inputs);
        try
        {
            for (int k = startPass; k < plan.Passes.Length; k++)
            {
                CompiledPass pass = plan.Passes[k];

                // A backend transition is the only place the executor syncs (C4.2); count one FlushSyncs per
                // transition so the counter equals the number of Skia<->Vulkan boundaries in the schedule. The
                // actual GPU barrier happens inside the materialize/dispatch path this pass drives.
                if (pass.SyncBefore && diagnostics != null)
                    diagnostics.FlushSyncs++;

                switch (pass)
                {
                    case SplitPass split:
                        ExecuteSplit(
                            split, current, outputScale, workingScale, maxWorkingScale, diagnostics, pool);
                        break;
                    case CompositePass composite:
                        ExecuteComposite(
                            composite, current, workingScale, maxWorkingScale, diagnostics, pool);
                        break;
                    case NestedGraphPass nestedGraph:
                        ExecuteNestedGraph(
                            nestedGraph, current, outputScale, workingScale, maxWorkingScale, diagnostics, pool);
                        break;
                    default:
                        MapDescriptorPass(
                            pass, resources.Passes[k], current, outputScale, workingScale, maxWorkingScale,
                            diagnostics, pool, captureSink != null && k == captureSink.CapturePassIndex ? captureSink : null);
                        break;
                }
            }

            // A capture frame deliberately retains the prefix pass's buffer past its plan-declared last use (the C10
            // cross-frame lease): exactly one buffer, so the frame's intra-frame peak is the plan's declared bound + 1.
            // Assert against that inflated bound rather than skipping, so a capture that over-retains is still caught.
            AssertPeakLiveWithinPlan(plan, inputs.Length, pool, leaseBaseline, captureSink?.Captured == true ? 1 : 0);

            return current.ToArray();
        }
        catch
        {
            RenderNodeOperation.DisposeAll(CollectionsMarshal.AsSpan(current));
            current.Clear();
            throw;
        }
    }

    // FR-007 runtime gate (C3.1): the measured peak of concurrently live pooled leases during one plan execution
    // must stay within the plan's declared peak-live bound. Only statically bounded single-input executions are
    // asserted: a dynamic-output pass (dynamic split, nested graph) allocates an execution-time-resolved set
    // exempt from the static bound (C3.5) — and a nested execution resets the pool's peak window — while a
    // multi-op input set has no per-op intermediate decls.
    [Conditional("DEBUG")]
    private static void AssertPeakLiveWithinPlan(
        CompiledPlan plan, int inputCount, RenderTargetPool? pool, long leaseBaseline, long captureAllowance)
    {
        if (pool == null || inputCount != 1)
            return;

        foreach (CompiledPass pass in plan.Passes)
        {
            if (pass.IsDynamicOutputs)
                return;
        }

        long measured = pool.PeakLiveLeaseCount - leaseBaseline;
        Debug.Assert(
            measured <= plan.Resources.PeakLiveCount + captureAllowance,
            $"FR-007 violated: measured peak of concurrently live pooled leases ({measured}) exceeds the plan's " +
            $"declared peak-live bound ({plan.Resources.PeakLiveCount}) plus the capture allowance ({captureAllowance}).");
    }

    // Executes a nested-graph pass: per branch, describe the child graph at the branch's bounds and index, compile,
    // and recurse. Each branch's plan compiles fresh per frame (counted in PlanCompilations) — a nested description
    // is branch-index-dependent by contract, so the outer node's plan cache cannot hold it; program caching still
    // applies inside. An empty child graph is the identity (the branch passes through).
    private static void ExecuteNestedGraph(
        NestedGraphPass pass, List<RenderNodeOperation> current, float outputScale, float workingScale,
        float maxWorkingScale, PipelineDiagnostics? diagnostics, RenderTargetPool? pool)
    {
        var outputs = new List<RenderNodeOperation>(current.Count);
        try
        {
            for (int i = 0; i < current.Count; i++)
            {
                RenderNodeOperation op = current[i];
                var builder = new EffectGraphBuilder(op.Bounds, outputScale, workingScale, maxWorkingScale);
                pass.DescribeBranch(builder, i);
                using EffectGraph graph = builder.Build();
                CompiledPlan branchPlan = EffectGraphCompiler.Compile(graph, diagnostics);
                FrameResources branchResources = EffectGraphCompiler.ResolveResources(
                    branchPlan, builder.Bounds, workingScale);
                // Hand ownership of op to the recursion only once it is about to consume it: a DescribeBranch/
                // Build/Compile/ResolveResources throw above still leaves op in current for the catch to dispose.
                current[i] = null!;
                outputs.AddRange(Execute(
                    branchPlan, branchResources, [op], outputScale, workingScale, maxWorkingScale,
                    diagnostics, pool));
            }
        }
        catch
        {
            RenderNodeOperation.DisposeAll(CollectionsMarshal.AsSpan(outputs));
            RenderNodeOperation.DisposeAll(CollectionsMarshal.AsSpan(current));
            current.Clear();
            throw;
        }

        current.Clear();
        current.AddRange(outputs);
    }

    // Applies a descriptor pass to every current operation independently. A single upstream operation uses the
    // per-frame resolution (resolved size, working-scale carry, empty-ROI skip); a fanned-out set (an upstream
    // opaque split) sizes each branch from its own bounds — a coordinate-invariant fused pass is identity, so an
    // operation's output bounds equal its input bounds.
    private static void MapDescriptorPass(
        CompiledPass pass, PassResolution resolution, List<RenderNodeOperation> current,
        float outputScale, float workingScale, float maxWorkingScale, PipelineDiagnostics? diagnostics,
        RenderTargetPool? pool, PrefixCaptureSink? captureSink = null)
    {
        bool linear = current.Count == 1;
        // The prefix cache only ever captures a linear (single-op) pass output (C10 v1 scope), so a fanned-out set is
        // never a capture site; drop the sink in that case so no partial branch is retained.
        PrefixCaptureSink? sink = linear ? captureSink : null;
        var outputs = new List<RenderNodeOperation>(current.Count);
        try
        {
            for (int i = 0; i < current.Count; i++)
            {
                RenderNodeOperation op = current[i];
                current[i] = null!;
                // A null result drops this pass output and continues: either an empty resolved output (a shrinking
                // pass) or a preview allocation-failure (C7; delivery renders throw instead of returning null).
                RenderNodeOperation? mapped = MapOneOperation(
                    pass, resolution, linear, op, outputScale, workingScale, maxWorkingScale, diagnostics, pool, sink);
                if (mapped != null)
                    outputs.Add(mapped);
            }
        }
        catch
        {
            RenderNodeOperation.DisposeAll(CollectionsMarshal.AsSpan(outputs));
            throw;
        }

        current.Clear();
        current.AddRange(outputs);
    }

    private static RenderNodeOperation? MapOneOperation(
        CompiledPass pass, PassResolution resolution, bool linear, RenderNodeOperation op,
        float outputScale, float workingScale, float maxWorkingScale, PipelineDiagnostics? diagnostics,
        RenderTargetPool? pool, PrefixCaptureSink? captureSink = null)
    {
        // A compute pass on a context without Vulkan takes its declared fallback before any allocation (C6/A7).
        if (pass is ComputePass compute && !SupportsCompute())
        {
            switch (compute.Fallback)
            {
                case ComputeFallback.Identity:
                    return op;
                case ComputeFallback.Skip:
                    op.Dispose();
                    return null;
            }
        }

        bool invariantFused = pass is FusedShaderPass { CoordinateInvariant: true };
        Rect outBounds;
        int width, height;
        float w;
        bool skip;
        if (invariantFused)
        {
            // Identity: output bounds are the operation's own — surviving an upstream opaque node that did not
            // advance the builder's logical bounds, and sizing each fan-out branch. On the linear path the density
            // is the carried resolution.WorkingScale (the FR-012/C3.2 clamp carry, review M2); a fan-out branch has
            // no per-op resolution and re-clamps locally.
            outBounds = op.Bounds;
            w = linear
                ? resolution.WorkingScale
                : RenderNodeContext.ClampWorkingScaleToBufferBudget(outBounds, CarriedWorkingScale(op, workingScale));
            (width, height) = RenderNodeContext.DeviceBufferSize(outBounds, w);
            skip = width <= 0 || height <= 0;
        }
        else if (!linear)
        {
            // Fan-out of a non-invariant pass (a split branch through a Blur/DropShadow/whole-source shader): size
            // each branch from ITS OWN bounds advanced by the pass's composed forward map. The graph-level
            // OutputBounds was computed before the split and is wrong for a branch (review B1).
            Rect forward = pass.ForwardBounds(op.Bounds);
            outBounds = forward.IsInvalid ? op.Bounds : forward;
            w = RenderNodeContext.ClampWorkingScaleToBufferBudget(outBounds, CarriedWorkingScale(op, workingScale));
            (width, height) = RenderNodeContext.DeviceBufferSize(outBounds, w);
            skip = width <= 0 || height <= 0;
        }
        else
        {
            // Linear single-op non-invariant pass: bake window and placement are the exact rect ResolveResources
            // sized the buffer from. A non-invariant whole-source fused pass whose output rect differs from its
            // input (a channel-shift shader baked into an expanded rect) is sized/placed by its resolved ROI here.
            outBounds = resolution.OutputRoi.IsInvalid
                ? (pass.OutputBounds.IsInvalid ? op.Bounds : pass.OutputBounds)
                : resolution.OutputRoi;
            width = resolution.Width;
            height = resolution.Height;
            w = resolution.WorkingScale;
            skip = resolution.SkipEmpty;
        }

        if (skip)
        {
            // Two skip causes need opposite handling. When the INPUT op is itself empty, an identity/invariant pass
            // over nothing is nothing: pass the already-empty op through. When the input is non-empty but the pass's
            // resolved OUTPUT is empty (a shrinking pass, e.g. a fully-closed Clipping), the pass legitimately
            // produces nothing — drop the input rather than leaking it downstream (legacy Apply removed the target).
            float inW = RenderNodeContext.ClampWorkingScaleToBufferBudget(op.Bounds, CarriedWorkingScale(op, workingScale));
            (int inBw, int inBh) = RenderNodeContext.DeviceBufferSize(op.Bounds, inW);
            if (inBw <= 0 || inBh <= 0)
                return op;

            op.Dispose();
            return null;
        }

        // Geometry and compute passes sample their input as a texture, so they materialize it and manage their own
        // input/output targets (with uniform C7 drop/throw on any failed acquire). Fused/Skia bake the source op
        // straight into the output buffer.
        switch (pass)
        {
            case GeometryPass geometry:
                return ExecuteGeometry(
                    geometry, width, height, w, outBounds, op, outputScale, workingScale, maxWorkingScale,
                    diagnostics, pool);
            case ComputePass computePass:
                return ExecuteCompute(
                    computePass, width, height, w, outBounds, op, outputScale, workingScale, maxWorkingScale,
                    diagnostics, pool);
        }

        RenderTarget? target = RenderTargetPool.Acquire(pool, width, height, diagnostics);
        if (target == null)
        {
            return DropOrThrow(op, maxWorkingScale,
                $"Effect pass buffer allocation failed ({width}x{height} px, w {w}, bounds {outBounds}).");
        }

        try
        {
            switch (pass)
            {
                case FusedShaderPass fused:
                    ExecuteFused(fused, target, w, outBounds, op, maxWorkingScale, diagnostics);
                    break;
                case SkiaFilterPass skia:
                    ExecuteSkia(skia, target, w, outBounds, op, maxWorkingScale);
                    break;
                default:
                    throw new NotSupportedException(
                        $"Pass '{pass.GetType().Name}' is not executable by the descriptor path.");
            }
        }
        catch
        {
            target.Dispose();
            op.Dispose();
            throw;
        }

        if (diagnostics != null)
            diagnostics.GpuPasses++;

        // Retain a shallow copy for the pass-prefix output cache (C10) before the op is threaded downstream: the
        // ref keeps the pooled buffer alive across frames so the next frame can resume from this pass's output.
        captureSink?.Capture(target, outBounds, EffectiveScale.At(w));

        op.Dispose();
        return RenderNodeOperation.CreateFromRenderTarget(
            outBounds, outBounds.Position, target, EffectiveScale.At(w));
    }

    // The C7 allocation-failure normalization for a per-operation pass: delivery (MaxWorkingScale == +Inf) throws
    // with the same message shape as the legacy ThrowIfDeliveryAllocationFailure; preview drops the pass output
    // (returns null) and logs. Either way the consumed input is released.
    private static RenderNodeOperation? DropOrThrow(RenderNodeOperation op, float maxWorkingScale, string message)
    {
        op.Dispose();
        if (float.IsPositiveInfinity(maxWorkingScale))
            throw new InvalidOperationException(message);

        s_logger.LogWarning("{Message} Preview drops this pass output.", message);
        return null;
    }

    // The density ceiling an operation carries into a downstream re-clamp/materialization (FR-012/C3.2): its pixels
    // only exist at its own EffectiveScale, so a clamped-down upstream op must never be re-materialized above that —
    // the boundary working scale alone would re-raise the density an upstream pass already reduced. A vector
    // (Unbounded) op re-rasterizes at any density, so it takes the full boundary working scale.
    private static float CarriedWorkingScale(RenderNodeOperation op, float workingScale)
        => op.EffectiveScale.IsUnbounded ? workingScale : MathF.Min(workingScale, op.EffectiveScale.Value);

    private static void ExecuteFused(
        FusedShaderPass pass, RenderTarget target, float w, Rect outBounds,
        RenderNodeOperation source, float maxWorkingScale, PipelineDiagnostics? diagnostics)
    {
        BakeSource(target, w, outBounds, source, maxWorkingScale, paint: null);

        // A whole-source stage samples src at arbitrary coordinates, so its declared tile mode governs out-of-bounds
        // reads (matching the legacy custom effect); a fused snippet run only samples the current pixel, so Decal.
        SKShaderTileMode srcTile = pass.Stages is [RuntimeShaderStage { Source.Kind: SkslSourceKind.WholeSource } ws]
            ? ws.SrcTileMode
            : SKShaderTileMode.Decal;
        using SKImage srcImage = target.Value.Snapshot();
        using SKShader srcShader = srcImage.ToShader(srcTile, srcTile);

        var uniformContext = new PassUniformContext(w, target.Width, target.Height, diagnostics);
        var disposables = new List<IDisposable>();
        try
        {
            SKShader composed = ComposeStages(pass.Stages, srcShader, uniformContext, diagnostics, disposables);
            using var paint = new SKPaint { Shader = composed };
            using var canvas = new ImmediateCanvas(target, w, maxWorkingScale, logicalSize: outBounds.Size);
            canvas.Clear();
            using (canvas.PushDeviceSpace())
            {
                canvas.Canvas.DrawRect(new SKRect(0, 0, target.Width, target.Height), paint);
            }
        }
        finally
        {
            for (int i = disposables.Count - 1; i >= 0; i--)
                disposables[i].Dispose();
        }
    }

    private static void ExecuteSkia(
        SkiaFilterPass pass, RenderTarget target, float w, Rect outBounds,
        RenderNodeOperation source, float maxWorkingScale)
    {
        SKImageFilter? filter = null;
        foreach (Func<SKImageFilter?, SKImageFilter?> factory in pass.Filters)
        {
            SKImageFilter? outer = factory(filter);
            // An identity factory can hand back its own argument; disposing the predecessor then would free the
            // filter still in use. Only advance when the factory produced a genuinely new instance.
            if (outer != null && !ReferenceEquals(outer, filter))
            {
                filter?.Dispose();
                filter = outer;
            }
        }

        using SKPaint? paint = filter != null ? new SKPaint { ImageFilter = filter } : null;
        BakeSource(target, w, outBounds, source, maxWorkingScale, paint);
        filter?.Dispose();
    }

    private static void BakeSource(
        RenderTarget target, float w, Rect outBounds, RenderNodeOperation source, float maxWorkingScale, SKPaint? paint)
    {
        using var canvas = new ImmediateCanvas(target, w, maxWorkingScale, logicalSize: outBounds.Size);
        canvas.Clear();
        using (canvas.PushTransform(Matrix.CreateTranslation(-outBounds.X, -outBounds.Y)))
        using (paint != null ? canvas.PushPaint(paint) : default)
        {
            source.Render(canvas);
        }
    }

    private static SKShader ComposeStages(
        ImmutableArray<FusedStage> stages, SKShader srcShader, in PassUniformContext uniformContext,
        PipelineDiagnostics? diagnostics, List<IDisposable> disposables)
    {
        SKShader current = srcShader;
        int i = 0;
        while (i < stages.Length)
        {
            if (stages[i] is ColorFilterStage colorFilter)
            {
                SKColorFilter? filter = colorFilter.Factory();
                if (filter != null)
                {
                    disposables.Add(filter);
                    current = Track(current.WithColorFilter(filter), disposables);
                }

                i++;
            }
            else
            {
                int j = i;
                var run = new List<RuntimeShaderStage>();
                while (j < stages.Length && stages[j] is RuntimeShaderStage runtime)
                {
                    run.Add(runtime);
                    j++;
                }

                current = BuildRuntimeRun(run, current, uniformContext, diagnostics, disposables);
                i = j;
            }
        }

        return current;
    }

    private static SKShader BuildRuntimeRun(
        List<RuntimeShaderStage> run, SKShader srcChild, in PassUniformContext uniformContext,
        PipelineDiagnostics? diagnostics, List<IDisposable> disposables)
    {
        bool wholeSource = run.Count == 1 && run[0].Source.Kind == SkslSourceKind.WholeSource;
        string childName = wholeSource ? "src" : SkslSnippetMerger.SourceChildName;

        // The program (merged/whole SKSL parse) is structural, so it is cached process-wide by a source-identity
        // signature: a warm run neither re-merges nor re-parses, keeping ProgramCreations at zero (SC-002). The
        // cached builder (which owns its SKRuntimeEffect) is reused, its per-frame uniforms/children overwritten and
        // Build() re-run below; it is NOT disposed here — disposing it would free the shared effect (the cache
        // disposes it on eviction). Its built shader is independent and IS disposed per frame.
        string signature = ProgramSignature(run, wholeSource);
        SKRuntimeShaderBuilder builder = ProgramCache.GetOrCreate(
            signature,
            () => wholeSource ? run[0].Source.Source : SkslSnippetMerger.Merge(run.Select(s => s.Source).ToList()),
            diagnostics);
        builder.Children[childName] = srcChild;

        for (int k = 0; k < run.Count; k++)
        {
            string prefix = wholeSource ? string.Empty : $"fe{k}_";
            foreach (UniformBinding uniform in run[k].Uniforms)
                uniform.Apply(builder, prefix + uniform.Name, in uniformContext);
            // An eager child/sampler (a LUT, curve textures) is graph-/caller-owned and left alone; a deferred
            // child's shader is produced here from this pass's real density (executorOwned == true) and tracked for
            // disposal after the draw. Either way the graph releases eager bindings after execution even when this
            // pass is skipped for an empty ROI (contract A2).
            foreach (ChildBinding child in run[k].Children)
            {
                SKShader childShader = child.Resolve(in uniformContext, out bool executorOwned);
                if (executorOwned)
                    disposables.Add(childShader);
                builder.Children[prefix + child.Name] = childShader;
            }
        }

        return Track(builder.Build(), disposables);
    }

    // The program-cache key: the ordered source identities of a runtime run. A whole-source stage keys on its one
    // source; a merged snippet run keys on the sequence of snippet hashes, which fully determines the merged text.
    private static string ProgramSignature(List<RuntimeShaderStage> run, bool wholeSource)
    {
        if (wholeSource)
            return "w:" + run[0].Source.IdentityHash;

        var sb = new System.Text.StringBuilder("m:");
        for (int i = 0; i < run.Count; i++)
        {
            if (i > 0)
                sb.Append(',');
            sb.Append(run[i].Source.IdentityHash);
        }

        return sb.ToString();
    }

    private static SKShader Track(SKShader shader, List<IDisposable> disposables)
    {
        disposables.Add(shader);
        return shader;
    }

    private static bool SupportsCompute()
    {
        IGraphicsContext? gfx = GraphicsContextFactory.SharedContext;
        return gfx is { Supports3DRendering: true };
    }

    // Bakes an operation into a freshly acquired pooled buffer sized to its bounds at density w, so a geometry /
    // compute / split pass can sample it as a texture. Counts one FullFrameMaterializations (C8). Returns null when
    // the pool cannot allocate (the caller applies the C7 drop/throw); an empty-size input is handled by the caller.
    private static RenderTarget? MaterializeInput(
        RenderNodeOperation op, float w, float maxWorkingScale, PipelineDiagnostics? diagnostics, RenderTargetPool? pool)
    {
        (int bw, int bh) = RenderNodeContext.DeviceBufferSize(op.Bounds, w);
        RenderTarget? target = RenderTargetPool.Acquire(pool, bw, bh, diagnostics);
        if (target == null)
            return null;

        try
        {
            BakeSource(target, w, op.Bounds, op, maxWorkingScale, paint: null);
        }
        catch
        {
            target.Dispose();
            throw;
        }

        if (diagnostics != null)
            diagnostics.FullFrameMaterializations++;
        return target;
    }

    private static RenderNodeOperation? ExecuteGeometry(
        GeometryPass pass, int width, int height, float w, Rect outBounds, RenderNodeOperation op,
        float outputScale, float workingScale, float maxWorkingScale, PipelineDiagnostics? diagnostics,
        RenderTargetPool? pool)
    {
        float inW = RenderNodeContext.ClampWorkingScaleToBufferBudget(op.Bounds, CarriedWorkingScale(op, workingScale));
        (int inBw, int inBh) = RenderNodeContext.DeviceBufferSize(op.Bounds, inW);
        if (inBw <= 0 || inBh <= 0)
            return op;

        RenderTarget? inputTarget = MaterializeInput(op, inW, maxWorkingScale, diagnostics, pool);
        if (inputTarget == null)
            return DropOrThrow(op, maxWorkingScale, $"Geometry input materialization failed ({inBw}x{inBh} px).");

        RenderTarget? outputTarget = RenderTargetPool.Acquire(pool, width, height, diagnostics);
        if (outputTarget == null)
        {
            inputTarget.Dispose();
            return DropOrThrow(op, maxWorkingScale,
                $"Geometry output allocation failed ({width}x{height} px, w {w}, bounds {outBounds}).");
        }

        bool discarded;
        try
        {
            var input = new EffectInput(inputTarget, op.Bounds, EffectiveScale.At(inW));
            using var canvas = new ImmediateCanvas(outputTarget, w, maxWorkingScale, logicalSize: outBounds.Size);
            canvas.Clear();
            var session = new GeometrySession(canvas, [input], outBounds, outputScale, w, maxWorkingScale, diagnostics);
            pass.Render(session);
            discarded = session.IsOutputDiscarded;
        }
        catch
        {
            outputTarget.Dispose();
            inputTarget.Dispose();
            op.Dispose();
            throw;
        }

        inputTarget.Dispose();
        if (discarded)
        {
            outputTarget.Dispose();
            op.Dispose();
            return null;
        }

        op.Dispose();
        if (diagnostics != null)
            diagnostics.GpuPasses++;
        return RenderNodeOperation.CreateFromRenderTarget(
            outBounds, outBounds.Position, outputTarget, EffectiveScale.At(w));
    }

    private static RenderNodeOperation? ExecuteCompute(
        ComputePass pass, int width, int height, float w, Rect outBounds, RenderNodeOperation op,
        float outputScale, float workingScale, float maxWorkingScale, PipelineDiagnostics? diagnostics,
        RenderTargetPool? pool)
    {
        IGraphicsContext? gfx = GraphicsContextFactory.SharedContext;
        if (gfx is not { Supports3DRendering: true })
        {
            // Identity/Skip already returned in MapOneOperation; only CpuCallback reaches here without Vulkan.
            return ExecuteComputeCpuFallback(
                pass, width, height, w, outBounds, op, outputScale, workingScale, maxWorkingScale, diagnostics, pool);
        }

        float inW = RenderNodeContext.ClampWorkingScaleToBufferBudget(op.Bounds, CarriedWorkingScale(op, workingScale));
        (int inBw, int inBh) = RenderNodeContext.DeviceBufferSize(op.Bounds, inW);
        if (inBw <= 0 || inBh <= 0)
            return op;

        RenderTarget? inputTarget = MaterializeInput(op, inW, maxWorkingScale, diagnostics, pool);
        if (inputTarget == null)
            return DropOrThrow(op, maxWorkingScale, $"Compute input materialization failed ({inBw}x{inBh} px).");

        inputTarget.PrepareForSampling();
        ITexture2D? sourceTexture = inputTarget.Texture;
        if (sourceTexture == null)
        {
            // Genuinely surface-less context (a raster surface behind a compute-capable context): identity so the
            // content survives. Decided BEFORE the output acquire so a later allocation failure is never mistaken
            // for it (review M1) — the input passes through unchanged.
            inputTarget.Dispose();
            return op;
        }

        RenderTarget? outputTarget = RenderTargetPool.Acquire(pool, width, height, diagnostics);
        if (outputTarget == null)
        {
            inputTarget.Dispose();
            return DropOrThrow(op, maxWorkingScale,
                $"Compute output allocation failed ({width}x{height} px, w {w}, bounds {outBounds}).");
        }

        ITexture2D? destTexture = outputTarget.Texture;
        if (destTexture == null)
        {
            outputTarget.Dispose();
            inputTarget.Dispose();
            return DropOrThrow(op, maxWorkingScale, $"Pooled compute output has no Vulkan texture ({width}x{height} px).");
        }

        var scratch = new List<RenderTarget>();
        var depthScratch = new List<IDisposable>();
        try
        {
            var ctx = new ComputeContext(
                gfx, sourceTexture, destTexture, width, height, w, scratch, depthScratch, diagnostics, pool);
            pass.Dispatch(ctx);
        }
        catch (ComputeScratchAllocationException ex)
        {
            // A ping-pong / depth scratch acquire failed mid-dispatch: normalize like every other pass kind (C7,
            // review M1) — preview drops and continues, delivery throws — instead of aborting preview by rethrowing.
            ReleaseComputeScratch(scratch, depthScratch);
            outputTarget.Dispose();
            inputTarget.Dispose();
            return DropOrThrow(op, maxWorkingScale, ex.Message);
        }
        catch
        {
            ReleaseComputeScratch(scratch, depthScratch);
            outputTarget.Dispose();
            inputTarget.Dispose();
            op.Dispose();
            throw;
        }

        ReleaseComputeScratch(scratch, depthScratch);
        inputTarget.Dispose();
        op.Dispose();
        return RenderNodeOperation.CreateFromRenderTarget(
            outBounds, outBounds.Position, outputTarget, EffectiveScale.At(w));
    }

    private static RenderNodeOperation? ExecuteComputeCpuFallback(
        ComputePass pass, int width, int height, float w, Rect outBounds, RenderNodeOperation op,
        float outputScale, float workingScale, float maxWorkingScale, PipelineDiagnostics? diagnostics,
        RenderTargetPool? pool)
    {
        if (pass.CpuCallback is not { } cpu)
            return op;

        float inW = RenderNodeContext.ClampWorkingScaleToBufferBudget(op.Bounds, CarriedWorkingScale(op, workingScale));
        (int inBw, int inBh) = RenderNodeContext.DeviceBufferSize(op.Bounds, inW);
        if (inBw <= 0 || inBh <= 0)
            return op;

        RenderTarget? inputTarget = MaterializeInput(op, inW, maxWorkingScale, diagnostics, pool);
        if (inputTarget == null)
            return DropOrThrow(op, maxWorkingScale, "Compute CPU-fallback input materialization failed.");

        RenderTarget? outputTarget = RenderTargetPool.Acquire(pool, width, height, diagnostics);
        if (outputTarget == null)
        {
            inputTarget.Dispose();
            return DropOrThrow(op, maxWorkingScale, "Compute CPU-fallback output allocation failed.");
        }

        bool discarded;
        try
        {
            var input = new EffectInput(inputTarget, op.Bounds, EffectiveScale.At(inW));
            using var canvas = new ImmediateCanvas(outputTarget, w, maxWorkingScale, logicalSize: outBounds.Size);
            canvas.Clear();
            var session = new GeometrySession(canvas, [input], outBounds, outputScale, w, maxWorkingScale, diagnostics);
            cpu(session);
            discarded = session.IsOutputDiscarded;
        }
        catch
        {
            outputTarget.Dispose();
            inputTarget.Dispose();
            op.Dispose();
            throw;
        }

        inputTarget.Dispose();
        if (discarded)
        {
            outputTarget.Dispose();
            op.Dispose();
            return null;
        }

        op.Dispose();
        if (diagnostics != null)
            diagnostics.GpuPasses++;
        return RenderNodeOperation.CreateFromRenderTarget(
            outBounds, outBounds.Position, outputTarget, EffectiveScale.At(w));
    }

    private static void ReleaseComputeScratch(List<RenderTarget> scratch, List<IDisposable> depthScratch)
    {
        foreach (RenderTarget t in scratch)
            t.Dispose();
        foreach (IDisposable d in depthScratch)
            d.Dispose();
    }

    // Fan-out: each current op is materialized once and split into the branches its callback emits (a static count
    // or, for dynamic outputs, an execution-time-resolved count the executor allocates, counts and releases).
    private static void ExecuteSplit(
        SplitPass pass, List<RenderNodeOperation> current, float outputScale, float workingScale,
        float maxWorkingScale, PipelineDiagnostics? diagnostics, RenderTargetPool? pool)
    {
        var outputs = new List<RenderNodeOperation>(current.Count);
        try
        {
            for (int i = 0; i < current.Count; i++)
            {
                RenderNodeOperation op = current[i];
                current[i] = null!;

                float inW = RenderNodeContext.ClampWorkingScaleToBufferBudget(op.Bounds, CarriedWorkingScale(op, workingScale));
                (int bw, int bh) = RenderNodeContext.DeviceBufferSize(op.Bounds, inW);
                if (bw <= 0 || bh <= 0)
                {
                    outputs.Add(op);
                    continue;
                }

                RenderTarget? inputTarget = MaterializeInput(op, inW, maxWorkingScale, diagnostics, pool);
                if (inputTarget == null)
                {
                    // Delivery throws inside DropOrThrow; preview drops this input's branches and continues.
                    DropOrThrow(op, maxWorkingScale, $"Split input materialization failed ({bw}x{bh} px).");
                    continue;
                }

                try
                {
                    var input = new EffectInput(inputTarget, op.Bounds, EffectiveScale.At(inW));
                    var emitter = new SplitEmitter(
                        input, inW, outputScale, maxWorkingScale, diagnostics, pool, outputs);
                    pass.Render(emitter);
                }
                finally
                {
                    inputTarget.Dispose();
                    op.Dispose();
                }
            }
        }
        catch
        {
            RenderNodeOperation.DisposeAll(CollectionsMarshal.AsSpan(outputs));
            throw;
        }

        current.Clear();
        current.AddRange(outputs);
    }

    // Fan-in: composite the whole current branch set into one output under the blend mode, applying each branch's
    // per-input offset. Draws each branch once onto a single pooled target.
    private static void ExecuteComposite(
        CompositePass pass, List<RenderNodeOperation> current, float workingScale, float maxWorkingScale,
        PipelineDiagnostics? diagnostics, RenderTargetPool? pool)
    {
        if (current.Count == 0)
            return;

        // The composite target density is the boundary working scale clamped to the union, NOT CarriedWorkingScale's
        // min over the inputs: each branch is redrawn into a fresh boundary-density buffer, so folding to the lowest
        // input density would permanently downsample a higher-density fan-in layer (a 003 FR-019 mixed-density scene).
        // The min-carry belongs only where an op's own pixels are re-materialized (single-op passes, split branches).
        Rect union = default;
        for (int i = 0; i < current.Count; i++)
        {
            Point offset = i < pass.InputOffsets.Length ? pass.InputOffsets[i] : default;
            union = union.Union(current[i].Bounds.Translate(offset));
        }

        float w = RenderNodeContext.ClampWorkingScaleToBufferBudget(union, workingScale);
        (int bw, int bh) = RenderNodeContext.DeviceBufferSize(union, w);
        if (bw <= 0 || bh <= 0)
        {
            RenderNodeOperation.DisposeAll(CollectionsMarshal.AsSpan(current));
            current.Clear();
            return;
        }

        RenderTarget? target = RenderTargetPool.Acquire(pool, bw, bh, diagnostics);
        if (target == null)
        {
            RenderNodeOperation.DisposeAll(CollectionsMarshal.AsSpan(current));
            current.Clear();
            if (float.IsPositiveInfinity(maxWorkingScale))
                throw new InvalidOperationException($"Composite output allocation failed ({bw}x{bh} px, w {w}).");

            s_logger.LogWarning("Composite output allocation failed ({Width}x{Height} px). Preview drops it.", bw, bh);
            return;
        }

        // A folded color-filter run (C9) applies as one composed SKColorFilter on each branch draw; identical to
        // baking each branch through the run and then compositing, at zero extra layers (it rides the blend-mode
        // SaveLayer the composite already opens per branch).
        SKColorFilter? branchFilter = ComposeCompositeColorFilter(pass.InputColorFilters);
        try
        {
            using var canvas = new ImmediateCanvas(target, w, maxWorkingScale, logicalSize: union.Size);
            canvas.Clear();
            using (canvas.PushTransform(Matrix.CreateTranslation(-union.X, -union.Y)))
            {
                // Src-over preserves the destination wherever the branch is transparent, so the full-canvas
                // SaveLayer PushBlendMode takes is pure overhead there: one whole-target round trip per branch
                // (~9x the branch area on a 3x3 split). A src-over branch draws directly; a C9 fold filter rides
                // a branch-bounded layer (Skia widens it itself if the filter affects transparent black). Any
                // other blend mode keeps the full-canvas layer — e.g. Multiply zeroes the destination where the
                // source is transparent, so the layer's extent is semantically load-bearing (§C8/§C9).
                for (int i = 0; i < current.Count; i++)
                {
                    Point offset = i < pass.InputOffsets.Length ? pass.InputOffsets[i] : default;
                    using (canvas.PushTransform(Matrix.CreateTranslation(offset.X, offset.Y)))
                    {
                        if (pass.BlendMode == BlendMode.SrcOver)
                        {
                            if (branchFilter == null)
                            {
                                current[i].Render(canvas);
                            }
                            else
                            {
                                using (canvas.PushBlendMode(pass.BlendMode, branchFilter, current[i].Bounds))
                                    current[i].Render(canvas);
                            }
                        }
                        else
                        {
                            if (diagnostics != null)
                                diagnostics.CompositeLayerSaves++;
                            using (canvas.PushBlendMode(pass.BlendMode, branchFilter))
                                current[i].Render(canvas);
                        }
                    }
                }
            }
        }
        catch
        {
            branchFilter?.Dispose();
            target.Dispose();
            RenderNodeOperation.DisposeAll(CollectionsMarshal.AsSpan(current));
            current.Clear();
            throw;
        }

        branchFilter?.Dispose();

        RenderNodeOperation.DisposeAll(CollectionsMarshal.AsSpan(current));
        current.Clear();
        if (diagnostics != null)
            diagnostics.GpuPasses++;
        current.Add(RenderNodeOperation.CreateFromRenderTarget(union, union.Position, target, EffectiveScale.At(w)));
    }

    // Composes the folded color-filter factories into one SKColorFilter in node order (C9): stage 0 is innermost, so
    // each subsequent filter wraps as the outer of a compose. A null factory result is a no-op stage and is skipped.
    // Returns null when nothing folded (or every stage is a no-op), leaving the composite draw unfiltered. The caller
    // owns the returned filter and disposes it once the whole branch set is drawn.
    private static SKColorFilter? ComposeCompositeColorFilter(ImmutableArray<Func<SKColorFilter?>> factories)
    {
        if (factories.IsDefaultOrEmpty)
            return null;

        SKColorFilter? composed = null;
        foreach (Func<SKColorFilter?> factory in factories)
        {
            SKColorFilter? filter = factory();
            if (filter == null)
                continue;

            if (composed == null)
            {
                composed = filter;
            }
            else
            {
                SKColorFilter next = SKColorFilter.CreateCompose(filter, composed);
                composed.Dispose();
                filter.Dispose();
                composed = next;
            }
        }

        return composed;
    }

    // A compute pass's ping-pong/depth scratch acquire failed. Distinct from a genuine dispatch bug so ExecuteCompute
    // can route it through the uniform C7 drop/throw (preview drops, delivery throws) rather than aborting preview.
    private sealed class ComputeScratchAllocationException(string message) : Exception(message);

    // The executor-owned resources handed to a compute node's dispatch callback: the materialized source and the
    // pass output texture, plus pooled color and depth scratch released when the pass ends.
    private sealed class ComputeContext(
        IGraphicsContext gfx, ITexture2D source, ITexture2D destination, int width, int height, float workingScale,
        List<RenderTarget> colorScratch, List<IDisposable> depthScratch, PipelineDiagnostics? diagnostics,
        RenderTargetPool? pool) : IComputeContext
    {
        public ITexture2D Source => source;

        public ITexture2D Destination => destination;

        public int Width => width;

        public int Height => height;

        public float WorkingScale => workingScale;

        public ITexture2D AcquireColorScratch()
        {
            RenderTarget target = RenderTargetPool.Acquire(pool, width, height, diagnostics)
                ?? throw new ComputeScratchAllocationException(
                    $"Compute ping-pong scratch allocation failed ({width}x{height} px).");
            colorScratch.Add(target);
            return target.Texture
                ?? throw new ComputeScratchAllocationException("Pooled compute scratch has no Vulkan texture.");
        }

        public void CopySourceToDestination()
        {
            gfx.CopyTexture(source, destination);
        }

        public ITexture2D AcquireDepthScratch()
        {
            if (pool != null)
            {
                PooledTextureLease lease = pool.AcquireTexture(width, height, TextureFormat.Depth32Float, diagnostics)
                    ?? throw new ComputeScratchAllocationException(
                        $"Compute depth scratch allocation failed ({width}x{height} px).");
                depthScratch.Add(lease);
                return lease.Texture;
            }

            ITexture2D depth = gfx.CreateTexture2D(width, height, TextureFormat.Depth32Float);
            // C8: a fresh non-pooled GPU target creation still counts TargetAllocations.
            if (diagnostics != null)
                diagnostics.TargetAllocations++;
            depthScratch.Add(depth);
            return depth;
        }

        public void Run<T>(GLSLShader shader, ITexture2D src, ITexture2D dst, ITexture2D depth, T pushConstants)
            where T : unmanaged
        {
            shader.ExecuteSingleTarget(src, dst, depth, pushConstants);
            if (diagnostics != null)
                diagnostics.GpuPasses++;
        }

        public void Run<T>(
            GLSLShader shader, ITexture2D src, ITexture2D mask, ITexture2D dst, ITexture2D depth, T pushConstants)
            where T : unmanaged
        {
            shader.ExecuteSingleTargetWithMask(src, mask, dst, depth, pushConstants);
            if (diagnostics != null)
                diagnostics.GpuPasses++;
        }
    }

    // The fan-out sink a split callback drives. Each Emit allocates one pooled branch target, opens a bracketed
    // session over it, runs the branch draw, and appends the branch op — counting one GpuPasses per branch and
    // applying the C7 drop/throw on a failed allocation.
    private sealed class SplitEmitter(
        EffectInput input, float workingScale, float outputScale, float maxWorkingScale,
        PipelineDiagnostics? diagnostics, RenderTargetPool? pool, List<RenderNodeOperation> outputs) : ISplitEmitter
    {
        public EffectInput Input => input;

        public float WorkingScale => workingScale;

        public void Emit(Rect logicalBounds, Action<GeometrySession> render)
        {
            ArgumentNullException.ThrowIfNull(render);

            float w = RenderNodeContext.ClampWorkingScaleToBufferBudget(logicalBounds, workingScale);
            (int bw, int bh) = RenderNodeContext.DeviceBufferSize(logicalBounds, w);
            if (bw <= 0 || bh <= 0)
                return;

            RenderTarget? target = RenderTargetPool.Acquire(pool, bw, bh, diagnostics);
            if (target == null)
            {
                if (float.IsPositiveInfinity(maxWorkingScale))
                    throw new InvalidOperationException($"Split branch allocation failed ({bw}x{bh} px, w {w}).");

                s_logger.LogWarning("Split branch allocation failed ({Width}x{Height} px). Preview drops it.", bw, bh);
                return;
            }

            bool discarded;
            try
            {
                using var canvas = new ImmediateCanvas(target, w, maxWorkingScale, logicalSize: logicalBounds.Size);
                canvas.Clear();
                var session = new GeometrySession(
                    canvas, [input], logicalBounds, outputScale, w, maxWorkingScale, diagnostics);
                render(session);
                discarded = session.IsOutputDiscarded;
            }
            catch
            {
                target.Dispose();
                throw;
            }

            if (discarded)
            {
                target.Dispose();
                return;
            }

            if (diagnostics != null)
                diagnostics.GpuPasses++;
            outputs.Add(RenderNodeOperation.CreateFromRenderTarget(
                logicalBounds, logicalBounds.Position, target, EffectiveScale.At(w)));
        }
    }
}
