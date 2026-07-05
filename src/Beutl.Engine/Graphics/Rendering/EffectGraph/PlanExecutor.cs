using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Effects;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// Runs a <see cref="CompiledPlan"/> against the graphics context (feature 004, T023, D2/D5). A plan is a
/// schedule of passes threaded over the input operation set. Two pass families interleave:
/// <list type="bullet">
/// <item><description>A descriptor pass (<see cref="FusedShaderPass"/>, <see cref="SkiaFilterPass"/>) transforms
/// each current operation independently — a fused pass executes as one draw built by shader composition (input
/// image shader → <c>WithColorFilter</c> wraps → nested <c>SKRuntimeEffect</c> child shaders, adjacent snippets
/// merged into one program), a Skia-filter pass as one filtered draw. The RGBA16F premultiplied linear-light
/// representation is preserved between stages.</description></item>
/// <item><description>An <see cref="OpaqueLegacyPass"/> segment runs the retained (internal-only) activator over
/// the <em>whole</em> current set via <see cref="LegacyBridgeExecutor"/> (T019 bridge). Its input is the upstream
/// pass's output and its output feeds the downstream passes, so an unmigrated effect fuses correctly between
/// migrated ones. All of a segment's counter attribution stays inside the legacy machinery (never additionally
/// counted here), keeping bridged content's counters byte-identical to today's.</description></item>
/// </list>
/// A plan that is a single opaque pass over all inputs keeps the whole-plan fast path (byte-identity + counter
/// parity for a fully-unmigrated chain). Descriptor-pass counters follow §C8: one
/// <see cref="PipelineDiagnostics.GpuPasses"/> per executed draw, one
/// <see cref="PipelineDiagnostics.ProgramCreations"/> per <c>SKRuntimeEffect</c> created.
/// </summary>
internal static class PlanExecutor
{
    private static readonly ILogger s_logger = Log.CreateLogger("PlanExecutor");

    public static RenderNodeOperation[] Execute(
        CompiledPlan plan,
        FrameResources resources,
        RenderNodeOperation[] inputs,
        Rect bounds,
        float outputScale,
        float workingScale,
        float maxWorkingScale,
        PipelineDiagnostics? diagnostics,
        RenderTargetPool? pool)
    {
        // Whole-plan fast path: a fully-unmigrated chain is one opaque pass over every input at once. Delegating
        // to the bridge keeps the render byte-identical and its counters unchanged from the pre-redesign pipeline.
        if (plan.Passes is [OpaqueLegacyPass onlyOpaque])
        {
            return LegacyBridgeExecutor.Execute(
                onlyOpaque.Context, inputs, bounds, outputScale, workingScale, maxWorkingScale, diagnostics, pool);
        }

        // Mixed plan: thread the whole operation set through the schedule pass by pass, so an opaque segment can
        // consume the upstream set and hand its output to the downstream passes.
        var current = new List<RenderNodeOperation>(inputs);
        try
        {
            for (int k = 0; k < plan.Passes.Length; k++)
            {
                CompiledPass pass = plan.Passes[k];

                // A backend transition is the only place the executor syncs (C4.2); count one FlushSyncs per
                // transition so the counter equals the number of Skia<->Vulkan boundaries in the schedule. The
                // actual GPU barrier happens inside the materialize/dispatch path this pass drives.
                if (pass.SyncBefore && diagnostics != null)
                    diagnostics.FlushSyncs++;

                switch (pass)
                {
                    case OpaqueLegacyPass opaque:
                        ExecuteOpaqueSegment(
                            opaque, current, bounds, outputScale, workingScale, maxWorkingScale, diagnostics, pool);
                        break;
                    case SplitPass split:
                        ExecuteSplit(
                            split, current, outputScale, workingScale, maxWorkingScale, diagnostics, pool);
                        break;
                    case CompositePass composite:
                        ExecuteComposite(
                            composite, current, workingScale, maxWorkingScale, diagnostics, pool);
                        break;
                    default:
                        MapDescriptorPass(
                            pass, resources.Passes[k], current, outputScale, workingScale, maxWorkingScale,
                            diagnostics, pool);
                        break;
                }
            }

            return current.ToArray();
        }
        catch
        {
            RenderNodeOperation.DisposeAll(CollectionsMarshal.AsSpan(current));
            current.Clear();
            throw;
        }
    }

    // Runs a bridged legacy segment over the whole current set. The bridge takes ownership of the handed-in
    // operations (same contract as the whole-plan fast path); on success its output replaces the set.
    private static void ExecuteOpaqueSegment(
        OpaqueLegacyPass opaque, List<RenderNodeOperation> current, Rect bounds, float outputScale,
        float workingScale, float maxWorkingScale, PipelineDiagnostics? diagnostics, RenderTargetPool? pool)
    {
        RenderNodeOperation[] segmentInputs = current.ToArray();
        current.Clear();
        RenderNodeOperation[] produced = LegacyBridgeExecutor.Execute(
            opaque.Context, segmentInputs, bounds, outputScale, workingScale, maxWorkingScale, diagnostics, pool);
        current.AddRange(produced);
    }

    // Applies a descriptor pass to every current operation independently. A single upstream operation uses the
    // per-frame resolution (resolved size, working-scale carry, empty-ROI skip); a fanned-out set (an upstream
    // opaque split) sizes each branch from its own bounds — a coordinate-invariant fused pass is identity, so an
    // operation's output bounds equal its input bounds.
    private static void MapDescriptorPass(
        CompiledPass pass, PassResolution resolution, List<RenderNodeOperation> current,
        float outputScale, float workingScale, float maxWorkingScale, PipelineDiagnostics? diagnostics,
        RenderTargetPool? pool)
    {
        bool linear = current.Count == 1;
        var outputs = new List<RenderNodeOperation>(current.Count);
        try
        {
            for (int i = 0; i < current.Count; i++)
            {
                RenderNodeOperation op = current[i];
                current[i] = null!;
                // A null result is a preview allocation-failure drop (C7): the pass output is discarded and the
                // frame continues; delivery renders throw instead of returning null.
                RenderNodeOperation? mapped = MapOneOperation(
                    pass, resolution, linear, op, outputScale, workingScale, maxWorkingScale, diagnostics, pool);
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
        RenderTargetPool? pool)
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

        // A coordinate-invariant fused pass is identity: its output bounds are the operation's own bounds. Sizing
        // from the operation (rather than the pass's described output bounds) both survives an upstream opaque
        // node that did not advance the builder's logical bounds and sizes each fan-out branch correctly.
        Rect outBounds;
        int width, height;
        float w;
        bool skip;
        if (pass is FusedShaderPass || !linear)
        {
            outBounds = pass.OutputBounds.IsInvalid || pass is FusedShaderPass ? op.Bounds : pass.OutputBounds;
            w = RenderNodeContext.ClampWorkingScaleToBufferBudget(outBounds, workingScale);
            (width, height) = CustomFilterEffectContext.DeviceBufferSize(outBounds, w);
            skip = width <= 0 || height <= 0;
        }
        else
        {
            // Bake window and placement must be the exact rect ResolveResources sized the buffer from.
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
            return op;
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

        var disposables = new List<IDisposable>();
        try
        {
            SKShader composed = ComposeStages(pass.Stages, srcShader, diagnostics, disposables);
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
            if (outer != null)
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
        ImmutableArray<FusedStage> stages, SKShader srcShader,
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

                current = BuildRuntimeRun(run, current, diagnostics, disposables);
                i = j;
            }
        }

        return current;
    }

    private static SKShader BuildRuntimeRun(
        List<RuntimeShaderStage> run, SKShader srcChild,
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
                uniform.Apply(builder, prefix + uniform.Name);
            // A per-frame sampler/child shader (a LUT, curve textures) is bound here but owned by the graph, which
            // releases it after execution even when this pass is skipped for an empty ROI (contract A2).
            foreach (SamplerBinding sampler in run[k].Samplers)
                builder.Children[prefix + sampler.Name] = sampler.Shader;
            foreach (ChildBinding child in run[k].Children)
                builder.Children[prefix + child.Name] = child.Shader;
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
        (int bw, int bh) = CustomFilterEffectContext.DeviceBufferSize(op.Bounds, w);
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
        float inW = RenderNodeContext.ClampWorkingScaleToBufferBudget(op.Bounds, workingScale);
        (int inBw, int inBh) = CustomFilterEffectContext.DeviceBufferSize(op.Bounds, inW);
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

        try
        {
            var input = new EffectInput(inputTarget, op.Bounds, EffectiveScale.At(inW));
            using var canvas = new ImmediateCanvas(outputTarget, w, maxWorkingScale, logicalSize: outBounds.Size);
            canvas.Clear();
            var session = new GeometrySession(canvas, [input], outBounds, outputScale, inW, maxWorkingScale);
            pass.Render(session);
        }
        catch
        {
            outputTarget.Dispose();
            inputTarget.Dispose();
            op.Dispose();
            throw;
        }

        inputTarget.Dispose();
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

        float inW = RenderNodeContext.ClampWorkingScaleToBufferBudget(op.Bounds, workingScale);
        (int inBw, int inBh) = CustomFilterEffectContext.DeviceBufferSize(op.Bounds, inW);
        if (inBw <= 0 || inBh <= 0)
            return op;

        RenderTarget? inputTarget = MaterializeInput(op, inW, maxWorkingScale, diagnostics, pool);
        if (inputTarget == null)
            return DropOrThrow(op, maxWorkingScale, $"Compute input materialization failed ({inBw}x{inBh} px).");

        inputTarget.PrepareForSampling();
        ITexture2D? sourceTexture = inputTarget.Texture;

        RenderTarget? outputTarget = sourceTexture != null
            ? RenderTargetPool.Acquire(pool, width, height, diagnostics)
            : null;
        ITexture2D? destTexture = outputTarget?.Texture;
        if (sourceTexture == null || destTexture == null)
        {
            // No shared texture (raster surface behind a compute-capable context is not expected): fall back to
            // identity so the content survives rather than vanishing.
            outputTarget?.Dispose();
            inputTarget.Dispose();
            return op;
        }

        var scratch = new List<RenderTarget>();
        var depthScratch = new List<IDisposable>();
        try
        {
            var ctx = new ComputeContext(
                gfx, sourceTexture, destTexture, width, height, w, scratch, depthScratch, diagnostics, pool);
            pass.Dispatch(ctx);
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

        float inW = RenderNodeContext.ClampWorkingScaleToBufferBudget(op.Bounds, workingScale);
        RenderTarget? inputTarget = MaterializeInput(op, inW, maxWorkingScale, diagnostics, pool);
        if (inputTarget == null)
            return DropOrThrow(op, maxWorkingScale, "Compute CPU-fallback input materialization failed.");

        RenderTarget? outputTarget = RenderTargetPool.Acquire(pool, width, height, diagnostics);
        if (outputTarget == null)
        {
            inputTarget.Dispose();
            return DropOrThrow(op, maxWorkingScale, "Compute CPU-fallback output allocation failed.");
        }

        try
        {
            var input = new EffectInput(inputTarget, op.Bounds, EffectiveScale.At(inW));
            using var canvas = new ImmediateCanvas(outputTarget, w, maxWorkingScale, logicalSize: outBounds.Size);
            canvas.Clear();
            var session = new GeometrySession(canvas, [input], outBounds, outputScale, inW, maxWorkingScale);
            cpu(session);
        }
        catch
        {
            outputTarget.Dispose();
            inputTarget.Dispose();
            op.Dispose();
            throw;
        }

        inputTarget.Dispose();
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

                float inW = RenderNodeContext.ClampWorkingScaleToBufferBudget(op.Bounds, workingScale);
                (int bw, int bh) = CustomFilterEffectContext.DeviceBufferSize(op.Bounds, inW);
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

        Rect union = default;
        for (int i = 0; i < current.Count; i++)
        {
            Point offset = i < pass.InputOffsets.Length ? pass.InputOffsets[i] : default;
            union = union.Union(current[i].Bounds.Translate(offset));
        }

        float w = RenderNodeContext.ClampWorkingScaleToBufferBudget(union, workingScale);
        (int bw, int bh) = CustomFilterEffectContext.DeviceBufferSize(union, w);
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

        try
        {
            using var canvas = new ImmediateCanvas(target, w, maxWorkingScale, logicalSize: union.Size);
            canvas.Clear();
            using (canvas.PushTransform(Matrix.CreateTranslation(-union.X, -union.Y)))
            {
                for (int i = 0; i < current.Count; i++)
                {
                    Point offset = i < pass.InputOffsets.Length ? pass.InputOffsets[i] : default;
                    using (canvas.PushBlendMode(pass.BlendMode))
                    using (canvas.PushTransform(Matrix.CreateTranslation(offset.X, offset.Y)))
                    {
                        current[i].Render(canvas);
                    }
                }
            }
        }
        catch
        {
            target.Dispose();
            RenderNodeOperation.DisposeAll(CollectionsMarshal.AsSpan(current));
            current.Clear();
            throw;
        }

        RenderNodeOperation.DisposeAll(CollectionsMarshal.AsSpan(current));
        current.Clear();
        if (diagnostics != null)
            diagnostics.GpuPasses++;
        current.Add(RenderNodeOperation.CreateFromRenderTarget(union, union.Position, target, EffectiveScale.At(w)));
    }

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
                ?? throw new InvalidOperationException(
                    $"Compute ping-pong scratch allocation failed ({width}x{height} px).");
            colorScratch.Add(target);
            return target.Texture
                ?? throw new InvalidOperationException("Pooled compute scratch has no Vulkan texture.");
        }

        public ITexture2D AcquireDepthScratch()
        {
            if (pool != null)
            {
                PooledTextureLease lease = pool.AcquireTexture(width, height, TextureFormat.Depth32Float, diagnostics)
                    ?? throw new InvalidOperationException(
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
            (int bw, int bh) = CustomFilterEffectContext.DeviceBufferSize(logicalBounds, w);
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

            try
            {
                using var canvas = new ImmediateCanvas(target, w, maxWorkingScale, logicalSize: logicalBounds.Size);
                canvas.Clear();
                var session = new GeometrySession(
                    canvas, [input], logicalBounds, outputScale, workingScale, maxWorkingScale);
                render(session);
            }
            catch
            {
                target.Dispose();
                throw;
            }

            if (diagnostics != null)
                diagnostics.GpuPasses++;
            outputs.Add(RenderNodeOperation.CreateFromRenderTarget(
                logicalBounds, logicalBounds.Position, target, EffectiveScale.At(w)));
        }
    }
}
