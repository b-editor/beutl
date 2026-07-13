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

    // Test seam: injects a throw at the compute input's PrepareForSampling (a Vulkan layout-transition failure is
    // not forcible from a test). The caller restores the seam afterward.
    private static Exception? s_computePrepareFailureForTests;
    private static bool s_forceComputeFallbackForTests;

    internal static void ForceComputePrepareFailureForTests(Exception exception)
        => s_computePrepareFailureForTests = exception;

    internal static void ResetComputePrepareFailureForTests() => s_computePrepareFailureForTests = null;

    // Test seam: CI runs with a compute-capable Vulkan context, so a backend-independent regression test needs a
    // deterministic way to exercise the declared CPU fallback without mutating the process-wide graphics context.
    internal static void ForceComputeFallbackForTests() => s_forceComputeFallbackForTests = true;

    internal static void ResetComputeFallbackForTests() => s_forceComputeFallbackForTests = false;

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
        PrefixCaptureSink? captureSink = null,
        bool isRenderCacheEnabled = true)
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
        int maxDimension = resources.MaxDimension;
        var current = new List<RenderNodeOperation>(inputs);
        try
        {
            for (int k = startPass; k < plan.Passes.Length; k++)
            {
                CompiledPass pass = plan.Passes[k];

                // Count one FlushSyncs per planned Skia<->Vulkan boundary. Descriptor-declared CPU readbacks are
                // counted separately immediately before their callbacks; no same-backend draw/canvas disposal
                // synchronizes (C4.2). The backend barrier itself happens in the materialize/dispatch path.
                if (pass.SyncBefore && diagnostics != null)
                    diagnostics.FlushSyncs++;

                switch (pass)
                {
                    case SplitPass split:
                        ExecuteSplit(
                            split, current, outputScale, workingScale, maxWorkingScale, maxDimension, diagnostics, pool);
                        break;
                    case CompositePass composite:
                        ExecuteComposite(
                            composite, current, workingScale, maxWorkingScale, maxDimension, diagnostics, pool);
                        break;
                    case NestedGraphPass nestedGraph:
                        ExecuteNestedGraph(
                            nestedGraph, current, outputScale, workingScale, maxWorkingScale, maxDimension,
                            diagnostics, pool);
                        break;
                    case CustomRenderNodePass customNode:
                        ExecuteCustomRenderNode(
                            customNode, current, outputScale, maxWorkingScale, diagnostics, pool,
                            isRenderCacheEnabled);
                        break;
                    default:
                        MapDescriptorPass(
                            pass, resources.Passes[k], ExpectedInputBounds(plan, resources, k), current,
                            outputScale, workingScale, maxWorkingScale, maxDimension,
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

        // A static split branch that calls SetOutputBounds blits into a tight target while the branch target AND the
        // branches' shared input scratch are still leased (SplitEmitter.EmitShrunk) — one transient lease above the
        // declared bound. The single-op geometry shrink releases its input first (its tight lease reuses that slot),
        // but a split cannot: later branches still read the shared input. Model the transient instead of skipping.
        long splitShrinkAllowance = 0;
        foreach (CompiledPass pass in plan.Passes)
        {
            if (pass.IsDynamicOutputs)
                return;
            if (pass is SplitPass)
                splitShrinkAllowance = 1;
        }

        long measured = pool.PeakLiveLeaseCount - leaseBaseline;
        long bound = plan.Resources.PeakLiveCount + captureAllowance + splitShrinkAllowance;
        Debug.Assert(
            measured <= bound,
            $"FR-007 violated: measured peak of concurrently live pooled leases ({measured}) exceeds the plan's " +
            $"declared peak-live bound ({plan.Resources.PeakLiveCount}) plus the capture ({captureAllowance}) and " +
            $"split-shrink ({splitShrinkAllowance}) allowances.");
    }

    // Executes a nested-graph pass: per branch, describe the child graph at the branch's bounds and index, compile,
    // and recurse. Each branch's plan compiles fresh per frame (counted in PlanCompilations) — a nested description
    // is branch-index-dependent by contract, so the outer node's plan cache cannot hold it; program caching still
    // applies inside. An empty child graph is the identity (the branch passes through).
    private static void ExecuteNestedGraph(
        NestedGraphPass pass, List<RenderNodeOperation> current, float outputScale, float workingScale,
        float maxWorkingScale, int maxDimension, PipelineDiagnostics? diagnostics, RenderTargetPool? pool)
    {
        var outputs = new List<RenderNodeOperation>(current.Count);
        try
        {
            for (int i = 0; i < current.Count; i++)
            {
                RenderNodeOperation op = current[i];
                // The branch inherits the carried density of the op feeding it (FR-012/C3.2), like every
                // materializing single-op path: the raw outer workingScale would re-raise a density an
                // upstream clamped op already reduced.
                float branchScale = CarriedWorkingScale(op, workingScale);
                // A DescribeBranch that registers a native shader and then throws would strand it; abort the still-open
                // engine-owned builder (Build transfers ownership to the graph, after which Abort is a no-op).
                var builder = new EffectGraphBuilder(op.Bounds, outputScale, branchScale, maxWorkingScale);
                try
                {
                    pass.DescribeBranch(builder, i);
                    using EffectGraph graph = builder.Build();
                    CompiledPlan branchPlan = EffectGraphCompiler.Compile(graph, diagnostics);
                    FrameResources branchResources = EffectGraphCompiler.ResolveResources(
                        branchPlan, builder.Bounds, branchScale, maxDimension);
                    // Hand ownership of op to the recursion only once it is about to consume it: a DescribeBranch/
                    // Build/Compile/ResolveResources throw above still leaves op in current for the catch to dispose.
                    current[i] = null!;
                    outputs.AddRange(Execute(
                        branchPlan, branchResources, [op], outputScale, branchScale, maxWorkingScale,
                        diagnostics, pool));
                }
                finally
                {
                    builder.Abort();
                }
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

    // The input rect the resolver assumed pass k consumes: the previous pass's resolved output ROI (its full bounds
    // when render-time-resolved), or the graph input for the first pass. An actual op narrower than this signals a
    // render-time shrink the frame-start resolution could not see (an AutoClip SetOutputBounds).
    private static Rect ExpectedInputBounds(CompiledPlan plan, FrameResources resources, int k)
    {
        if (k == 0)
            return plan.Passes[0].InputBounds;

        CompiledPass prev = plan.Passes[k - 1];
        Rect roi = resources.Passes[k - 1].OutputRoi;
        if (!roi.IsInvalid)
            return roi;

        return prev.OutputBounds.IsInvalid ? prev.InputBounds : prev.OutputBounds;
    }

    // Executes a custom-render-node pass: drive the child effect's custom FilterEffectRenderNode over the current
    // ops as one node of this plan. The node re-materializes the ops through its own pipeline (a NodeGraphFilterEffect
    // re-evaluates its graph), so the whole op set is handed in as the child context's Input and the returned ops flow
    // onward. Diagnostics and pool are threaded so the child's work counts on the owning renderer and shares its pool,
    // exactly like the node-graph render boundary. The child derives its own working scale from the input ops'
    // EffectiveScale (the carried density, C3.2) and OutputScale/MaxWorkingScale — the same resolution a top-level
    // filter-effect node performs.
    //
    // The wrapper node is created per frame, not persisted. The executor is stateless and reentrant (nested-graph
    // recursion, prefix resume), so a passIndex-keyed node map cannot survive a branch plan recompiled fresh each
    // frame; and the realistic child — a NodeGraphFilterEffectRenderNode — rebuilds its inner RenderNodeProcessor on
    // every Process, so its render caches live on the persisted graph-model resources, not on this wrapper. Persisting
    // the wrapper would therefore save no cache while forcing that map through the reentrant executor. Disposing the
    // wrapper stays alive until every operation it returned has been disposed: plugin operations may lazily read
    // node-owned caches, child nodes, or native state when Render is called after Process returns.
    private static void ExecuteCustomRenderNode(
        CustomRenderNodePass pass, List<RenderNodeOperation> current, float outputScale, float maxWorkingScale,
        PipelineDiagnostics? diagnostics, RenderTargetPool? pool, bool isRenderCacheEnabled)
    {
        RenderNodeOperation[] inputs = current.ToArray();
        // Ownership passes to the child node (which disposes or returns each input); clearing here keeps the outer
        // Execute catch from disposing ops the child now owns, and stops a double-drop on the passthrough path.
        current.Clear();

        // The factory runs after current was cleared, so a Create throw would strand the detached inputs in neither
        // disposal sweep; release them here (C7).
        FilterEffectRenderNode node;
        try
        {
            node = pass.Resource.RenderNodeFactory.Create(pass.Resource);
        }
        catch
        {
            RenderNodeOperation.DisposeAll(inputs);
            throw;
        }

        bool nodeOwnedByOutputs = false;
        try
        {
            node.Update(pass.Resource);
            var childContext = new RenderNodeContext(inputs, outputScale, maxWorkingScale)
            {
                Diagnostics = diagnostics,
                Pool = pool,
                IsRenderCacheEnabled = isRenderCacheEnabled,
                // A custom node is opaque to the compiler, so its backward bounds contract is unknown. Passing the
                // outer crop directly would let a later expanding pass (blur, shadow, stroke) clip the halo before
                // it is produced. The custom node therefore receives the conservative full-input request; only
                // descriptor nodes with a compiler-visible bounds contract participate in ROI propagation.
                RequestedBounds = Rect.Invalid,
            };
            RenderNodeOperation[] outputs = node.Process(childContext);
            if (outputs.Length == 0)
                return;

            if (Array.Exists(outputs, static output => output is null))
            {
                RenderNodeOperation.DisposeAll(outputs);
                throw new InvalidOperationException("A custom render node returned a null operation.");
            }

            var lifetime = new CustomNodeLifetime(node, outputs.Length);
            var wrapped = new RenderNodeOperation[outputs.Length];
            int wrappedCount = 0;
            try
            {
                for (; wrappedCount < outputs.Length; wrappedCount++)
                {
                    RenderNodeOperation output = outputs[wrappedCount];
                    wrapped[wrappedCount] = RenderNodeOperation.CreateDecorator(
                        output, output.Render, output.HitTest, lifetime.CreateReleaseOnce());
                }
            }
            catch
            {
                RenderNodeOperation.DisposeAll(wrapped.AsSpan(0, wrappedCount));
                RenderNodeOperation.DisposeAll(outputs.AsSpan(wrappedCount));
                throw;
            }

            try
            {
                current.AddRange(wrapped);
                nodeOwnedByOutputs = true;
            }
            catch
            {
                RenderNodeOperation.DisposeAll(wrapped);
                throw;
            }
        }
        catch
        {
            // A throw mid-Process leaves ownership ambiguous; dispose the inputs best-effort (idempotent, so a
            // partially-consumed set is safe) so nothing the child had not yet adopted is stranded (C7).
            RenderNodeOperation.DisposeAll(inputs);
            throw;
        }
        finally
        {
            if (!nodeOwnedByOutputs)
                node.Dispose();
        }
    }

    private sealed class CustomNodeLifetime(FilterEffectRenderNode node, int references)
    {
        private int _references = references;

        public Action CreateReleaseOnce()
        {
            int released = 0;
            return () =>
            {
                if (Interlocked.Exchange(ref released, 1) == 0
                    && Interlocked.Decrement(ref _references) == 0)
                {
                    node.Dispose();
                }
            };
        }
    }

    // Applies a descriptor pass to every current operation independently. A single upstream operation uses the
    // per-frame resolution (resolved size, working-scale carry, empty-ROI skip); a fanned-out set (an upstream
    // opaque split) sizes each branch from its own bounds — a coordinate-invariant fused pass is identity, so an
    // operation's output bounds equal its input bounds.
    private static void MapDescriptorPass(
        CompiledPass pass, PassResolution resolution, Rect expectedInput, List<RenderNodeOperation> current,
        float outputScale, float workingScale, float maxWorkingScale, int maxDimension,
        PipelineDiagnostics? diagnostics, RenderTargetPool? pool, PrefixCaptureSink? captureSink = null)
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
                RenderNodeOperation? mapped;
                try
                {
                    mapped = MapOneOperation(
                        pass, resolution, expectedInput, linear, op, outputScale, workingScale, maxWorkingScale,
                        maxDimension, diagnostics, pool, sink);
                }
                catch
                {
                    // The op is already detached from `current`, so the outer sweeps (this method's outputs sweep,
                    // Execute's current sweep) can't reach it; a throw BEFORE MapOneOperation takes ownership — a
                    // plugin bounds lambda inside ForwardBounds — would strand its pooled lease. Dispose is
                    // idempotent, so the C7 paths that already disposed op before rethrowing are unaffected.
                    op.Dispose();
                    throw;
                }

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
        CompiledPass pass, PassResolution resolution, Rect expectedInput, bool linear, RenderNodeOperation op,
        float outputScale, float workingScale, float maxWorkingScale, int maxDimension,
        PipelineDiagnostics? diagnostics, RenderTargetPool? pool, PrefixCaptureSink? captureSink = null)
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
            // A linear invariant pass still has a real resolved ROI. Rendering op.Bounds here would discard the
            // compiler's backward-ROI result and can turn a small requested crop into a full-frame allocation. Keep
            // the resolved window when the op matches the resolver's expectation; intersect it with a render-time
            // shrink; and only fall back to the actual op for a shifted/grown dynamic predecessor whose describe-
            // time ROI is stale. Fan-out has no per-op resolution, so each branch keeps its own bounds.
            bool escaped = false;
            if (!linear)
            {
                outBounds = op.Bounds;
            }
            else
            {
                Rect resolved = resolution.OutputRoi.IsInvalid ? op.Bounds : resolution.OutputRoi;
                outBounds = resolved;
                if (op.Bounds != expectedInput)
                {
                    if (expectedInput.Contains(op.Bounds))
                        outBounds = resolved.Intersect(op.Bounds);
                    else
                    {
                        outBounds = op.Bounds;
                        escaped = true;
                    }
                }
            }

            // A dynamic escaped/fan-out op carries its own supply density. A matched or shrunk linear op keeps the
            // boundary-resolved density, whose output-scale floor must not be lowered by a sparse input supply.
            float requestedScale = !linear
                ? CarriedWorkingScale(op, workingScale)
                : escaped ? CarriedWorkingScale(op, resolution.WorkingScale) : resolution.WorkingScale;
            w = RenderNodeContext.ClampWorkingScaleToBufferBudget(outBounds, requestedScale, maxDimension);
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
            w = RenderNodeContext.ClampWorkingScaleToBufferBudget(
                outBounds, CarriedWorkingScale(op, workingScale), maxDimension);
            (width, height) = RenderNodeContext.DeviceBufferSize(outBounds, w);
            skip = width <= 0 || height <= 0;
        }
        else
        {
            // Linear single-op non-invariant pass: bake window and placement are the rect ResolveResources sized the
            // buffer from — but that resolution is a frame-start upper bound, unaware of a render-time change upstream.
            // Three cases diverge by how the actual op relates to the resolver's expected input (§C3.5). When the op
            // matches the expectation the resolved ROI is kept verbatim: a forward map is authored against the pass's
            // semantic input frame (a fixed Clipping deflates whatever rect it receives), so applying it to a merely
            // ROI-narrowed op would double-apply the crop.
            Rect resolved = resolution.OutputRoi.IsInvalid
                ? (pass.OutputBounds.IsInvalid ? op.Bounds : pass.OutputBounds)
                : resolution.OutputRoi;
            outBounds = resolved;
            bool escaped = false;
            if (op.Bounds != expectedInput)
            {
                Rect forward = pass.ForwardBounds(op.Bounds);
                if (expectedInput.Contains(op.Bounds))
                {
                    // True shrink: the op sits strictly inside the expected input (an upstream AutoClip
                    // SetOutputBounds tightened it). Re-derive shrink-only, keeping the resolve-time ROI as the
                    // upper bound so a downstream bounds-inflating pass cannot re-inflate past the tightening.
                    if (!forward.IsInvalid)
                        outBounds = resolved.Intersect(forward);
                }
                else
                {
                    // Shift OR grow: a dynamic CustomRenderNode/NestedGraph predecessor emitted an op that escapes the
                    // expected input — translated (neither contains the other) or inflated (op ⊇ expectedInput). The
                    // resolve-time ROI, derived from the pre-change describe-time bounds, is stale for this frame, so
                    // map the ACTUAL op forward directly (the fan-out branch's approach); intersecting with the stale
                    // ROI would clip or empty a shifted op and crop a grown one.
                    outBounds = forward.IsInvalid ? op.Bounds : forward;
                    escaped = true;
                }
            }

            // A shift/grow re-derives outBounds from the ACTUAL op, which can be larger than the ROI-narrowed rect
            // resolution.WorkingScale was clamped against; re-clamp so DeviceBufferSize cannot exceed the per-axis
            // budget (FR-037(b)). Only that escaped (dynamic-predecessor) path mins against the op's carried density
            // (C3.2 carry — its lower-density output must not be re-raised); a matched/shrunk op keeps the
            // boundary-resolved scale, whose sub-output-supply floor (w = max(s_out, supply)) a min-carry would
            // defeat — same rationale as the invariant branch above.
            w = RenderNodeContext.ClampWorkingScaleToBufferBudget(
                outBounds, escaped ? CarriedWorkingScale(op, resolution.WorkingScale) : resolution.WorkingScale, maxDimension);
            (width, height) = RenderNodeContext.DeviceBufferSize(outBounds, w);
            skip = width <= 0 || height <= 0;
        }

        if (skip)
        {
            // Two skip causes need opposite handling. When the INPUT op is itself empty, an identity/invariant pass
            // over nothing is nothing: pass the already-empty op through. When the input is non-empty but the pass's
            // resolved OUTPUT is empty (a shrinking pass, e.g. a fully-closed Clipping), the pass legitimately
            // produces nothing — drop the input rather than leaking it downstream (legacy Apply removed the target).
            float inW = RenderNodeContext.ClampWorkingScaleToBufferBudget(
                op.Bounds, CarriedWorkingScale(op, workingScale), maxDimension);
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
                    maxDimension, diagnostics, pool);
            case ComputePass computePass:
                return ExecuteCompute(
                    computePass, width, height, w, outBounds, op, outputScale, workingScale, maxWorkingScale,
                    maxDimension, diagnostics, pool);
        }

        // Skia filter factories are per-frame parameters. Build the chain before acquiring the output so an
        // all-null chain (notably an animated Blur at sigma zero) is a true runtime identity without changing the
        // graph shape or StructuralKey. A factory failure still consumes the detached input just as the old
        // post-acquire execution path did.
        SKImageFilter? preparedSkiaFilter = null;
        if (pass is SkiaFilterPass skiaPass)
        {
            try
            {
                preparedSkiaFilter = BuildSkiaFilter(skiaPass);
            }
            catch
            {
                op.Dispose();
                throw;
            }

            if (preparedSkiaFilter == null)
                return op;
        }

        RenderTarget? target = RenderTargetPool.Acquire(pool, width, height, diagnostics);
        if (target == null)
        {
            preparedSkiaFilter?.Dispose();
            return DropOrThrow(op, maxWorkingScale,
                $"Effect pass buffer allocation failed ({width}x{height} px, w {w}, bounds {outBounds}).");
        }

        // A non-invariant whole-source stage samples src across its backward-claimed input rect. When a downstream
        // deflate narrowed outBounds below that claim, the in-place bake would crop the halo the shader reads
        // (crop-after-shift ≠ shift-then-crop), so the source bakes into a separate pass-scoped buffer spanning the
        // claimed rect (§C3.1; declared as pass scratch). The contained common case keeps the in-place bake — zero
        // extra buffer, byte-identical to the frozen references. A NON-DECAL tile mode additionally requires the
        // src image extent to EQUAL the source footprint: tile modes only apply outside the image, so a union or
        // in-place bake whose rect exceeds op.Bounds pads INSIDE the image with transparency and Clamp/Repeat/Mirror
        // never engage at the real source edge (the legacy custom effect tiled the source's own snapshot). The tile
        // mode itself supplies every sample beyond the footprint, so the union halo is unnecessary there.
        Rect fusedSrcRect = outBounds;
        float fusedSrcScale = w;
        RenderTarget? fusedSrcTarget = null;
        if (pass is FusedShaderPass { CoordinateInvariant: false, Stages: [RuntimeShaderStage { Source.Kind: SkslSourceKind.WholeSource } wsStage] })
        {
            bool nonDecal = wsStage.SrcTileMode != SKShaderTileMode.Decal;
            Rect claimed = nonDecal ? op.Bounds : op.Bounds.Union(outBounds);
            if (nonDecal ? outBounds != op.Bounds : !outBounds.Contains(claimed))
            {
                fusedSrcScale = RenderNodeContext.ClampWorkingScaleToBufferBudget(claimed, w, maxDimension);
                (int sw, int sh) = RenderNodeContext.DeviceBufferSize(claimed, fusedSrcScale);
                fusedSrcTarget = RenderTargetPool.Acquire(pool, sw, sh, diagnostics);
                if (fusedSrcTarget == null)
                {
                    preparedSkiaFilter?.Dispose();
                    target.Dispose();
                    return DropOrThrow(op, maxWorkingScale,
                        $"Fused source-halo buffer allocation failed ({sw}x{sh} px, w {fusedSrcScale}, bounds {claimed}).");
                }

                fusedSrcRect = claimed;
            }
        }

        try
        {
            switch (pass)
            {
                case FusedShaderPass fused:
                    ExecuteFused(
                        fused, target, w, outBounds, op, maxWorkingScale, diagnostics,
                        fusedSrcTarget, fusedSrcRect, fusedSrcScale);
                    break;
                case SkiaFilterPass:
                    SKImageFilter filter = preparedSkiaFilter!;
                    preparedSkiaFilter = null;
                    ExecuteSkia(filter, target, w, outBounds, op, maxWorkingScale);
                    break;
                default:
                    throw new NotSupportedException(
                        $"Pass '{pass.GetType().Name}' is not executable by the descriptor path.");
            }
        }
        catch
        {
            preparedSkiaFilter?.Dispose();
            fusedSrcTarget?.Dispose();
            target.Dispose();
            op.Dispose();
            throw;
        }

        // The src snapshot must stay alive through the fused draw, so the halo buffer's lease releases only here —
        // it overlaps the output lease exactly as declared ([idx, idx] scratch, §C3.1).
        fusedSrcTarget?.Dispose();

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
        RenderNodeOperation source, float maxWorkingScale, PipelineDiagnostics? diagnostics,
        RenderTarget? srcTarget = null, Rect srcRect = default, float srcScale = 0f)
    {
        // srcTarget is the pass-scoped halo buffer (§C3.1): the source bakes over srcRect (⊇ outBounds) at srcScale
        // and the src shader's local matrix re-registers image coordinates to the output's device space, so the
        // shader samples the halo a downstream deflate cropped out of the output rect. Without it the source bakes
        // in place and snapshots the output target.
        if (srcTarget != null)
            BakeSource(srcTarget, srcScale, srcRect, source, maxWorkingScale, paint: null);
        else
            BakeSource(target, w, outBounds, source, maxWorkingScale, paint: null);

        // A whole-source stage samples src at arbitrary coordinates, so its declared tile mode governs out-of-bounds
        // reads (matching the legacy custom effect); a fused snippet run only samples the current pixel, so Decal.
        SKShaderTileMode srcTile = pass.Stages is [RuntimeShaderStage { Source.Kind: SkslSourceKind.WholeSource } ws]
            ? ws.SrcTileMode
            : SKShaderTileMode.Decal;
        using SKImage srcImage = (srcTarget ?? target).Value.Snapshot();
        using SKShader srcShader = srcTarget != null
            ? srcImage.ToShader(srcTile, srcTile, SrcHaloLocalMatrix(w, srcScale, outBounds, srcRect))
            : srcImage.ToShader(srcTile, srcTile);

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

    private static SKImageFilter? BuildSkiaFilter(SkiaFilterPass pass)
    {
        SKImageFilter? filter = null;
        try
        {
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

            return filter;
        }
        catch
        {
            filter?.Dispose();
            throw;
        }
    }

    private static void ExecuteSkia(
        SKImageFilter filter, RenderTarget target, float w, Rect outBounds,
        RenderNodeOperation source, float maxWorkingScale)
    {
        try
        {
            using var paint = new SKPaint { ImageFilter = filter };
            BakeSource(target, w, outBounds, source, maxWorkingScale, paint);
        }
        finally
        {
            // SKPaint.Dispose does not own its image filter. The prepared chain transfers here immediately before
            // the bake and is released whether drawing succeeds or throws.
            filter.Dispose();
        }
    }

    // Maps the halo-baked src image into the output pass's device space: output device coordinate c corresponds to
    // image pixel c·(srcScale/w) + (outBounds.Origin − srcRect.Origin)·srcScale, so the shader's src.eval reads the
    // same logical point whichever buffer holds the bake. srcScale can sit below w when the claimed rect crossed the
    // per-axis budget (FR-037(b)); the scale term keeps the sampling density-coherent then.
    private static SKMatrix SrcHaloLocalMatrix(float w, float srcScale, Rect outBounds, Rect srcRect)
    {
        float s = w / srcScale;
        float ox = (outBounds.X - srcRect.X) * srcScale;
        float oy = (outBounds.Y - srcRect.Y) * srcScale;
        return new SKMatrix(s, 0, -ox * s, 0, s, -oy * s, 0, 0, 1);
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
        // leased builder (which owns its SKRuntimeEffect) is reused, its per-frame uniforms/children overwritten and
        // Build() re-run below; only the lease is disposed here — disposing the builder would free the shared effect
        // (the cache disposes it on eviction). Its built shader is independent and IS disposed per frame. The lease
        // spans the whole bind: a deferred child resolved below can render a DrawableBrush whose nested pass requests
        // this same signature, and the lease is what routes that reentrant use onto its own builder.
        string signature = ProgramSignature(run, wholeSource);
        using ProgramCache.Lease lease = ProgramCache.GetOrCreate(
            signature,
            () => wholeSource ? run[0].Source.Source : SkslSnippetMerger.Merge(run.Select(s => s.Source).ToList()),
            diagnostics);
        SKRuntimeShaderBuilder builder = lease.Builder;
        // Clear the reused builder's prior-frame state before rebinding (still O(bindings) per frame): a same-signature
        // run that omits a binding this frame must see the program default, not the stale value — and a stale
        // executor-owned child would reference a shader disposed after that earlier draw.
        builder.Uniforms.Reset();
        builder.Children.Reset();
        builder.Children[childName] = srcChild;

        for (int k = 0; k < run.Count; k++)
        {
            string prefix = wholeSource ? string.Empty : SkslSnippetMerger.GetPrefix(run[k].Source, k);
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
        if (s_forceComputeFallbackForTests)
            return false;

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
        float outputScale, float workingScale, float maxWorkingScale, int maxDimension,
        PipelineDiagnostics? diagnostics, RenderTargetPool? pool)
    {
        float inW = RenderNodeContext.ClampWorkingScaleToBufferBudget(
            op.Bounds, CarriedWorkingScale(op, workingScale), maxDimension);
        (int inBw, int inBh) = RenderNodeContext.DeviceBufferSize(op.Bounds, inW);
        if (inBw <= 0 || inBh <= 0)
            return op;

        // MaterializeInput bakes op via source.Render (C7): a throw there must still release op's pooled lease, since
        // MapDescriptorPass already detached op from the working set and neither disposal sweep would otherwise reach it.
        RenderTarget? inputTarget;
        try
        {
            inputTarget = MaterializeInput(op, inW, maxWorkingScale, diagnostics, pool);
        }
        catch
        {
            op.Dispose();
            throw;
        }

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
        Rect? shrunk;
        try
        {
            if (pass.RequiresReadback)
            {
                inputTarget.PrepareForSampling();
                if (diagnostics != null)
                    diagnostics.FlushSyncs++;
            }

            var input = new EffectInput(
                inputTarget, op.Bounds, EffectiveScale.At(inW), readbackPrepared: pass.RequiresReadback);
            using var canvas = new ImmediateCanvas(outputTarget, w, maxWorkingScale, logicalSize: outBounds.Size);
            canvas.Clear();
            var session = new GeometrySession(canvas, [input], outBounds, outputScale, w, maxWorkingScale, diagnostics);
            pass.Render(session);
            discarded = session.IsOutputDiscarded;
            shrunk = session.ShrunkOutputBounds;
        }
        catch
        {
            outputTarget.Dispose();
            inputTarget.Dispose();
            op.Dispose();
            throw;
        }

        inputTarget.Dispose();
        // DiscardOutput supersedes a requested shrink (§C3): a dropped pass produces nothing regardless of order.
        if (discarded)
        {
            outputTarget.Dispose();
            op.Dispose();
            return null;
        }

        if (shrunk is { } tight)
            return EmitShrunkGeometry(tight, w, outBounds, outputTarget, op, maxWorkingScale, diagnostics, pool);

        op.Dispose();
        if (diagnostics != null)
            diagnostics.GpuPasses++;
        return RenderNodeOperation.CreateFromRenderTarget(
            outBounds, outBounds.Position, outputTarget, EffectiveScale.At(w));
    }

    // A render-time geometry pass (AutoClip) tightens its emitted output to a sub-rect of the allocated buffer via
    // GeometrySession.SetOutputBounds; copy that sub-rect into a tighter pooled target so the downstream operation's
    // bounds and hit-test region match the content, restoring the legacy Clipping.Apply target-tightening. The extra
    // pooled acquire is counted (C8 TargetAllocations/PoolAcquires); the geometry pass still counts one GpuPass. The
    // input op is disposed only after the tight acquire so a failed acquire still routes through DropOrThrow — and the
    // tight lease overlaps the full-output lease exactly as the input-scratch overlapped it during render, so the pass's
    // concurrent-lease peak is unchanged (no ResourcePlan declaration change; C3.1 / FR-007).
    private static RenderNodeOperation? EmitShrunkGeometry(
        Rect tight, float w, Rect outBounds, RenderTarget outputTarget, RenderNodeOperation op,
        float maxWorkingScale, PipelineDiagnostics? diagnostics, RenderTargetPool? pool)
    {
        (int tw, int th) = RenderNodeContext.DeviceBufferSize(tight, w);
        if (tw <= 0 || th <= 0)
        {
            // A degenerate (empty) shrink yields nothing, matching DiscardOutput and the §C3 empty-output drop.
            outputTarget.Dispose();
            op.Dispose();
            return null;
        }

        RenderTarget? tightTarget = RenderTargetPool.Acquire(pool, tw, th, diagnostics);
        if (tightTarget == null)
        {
            outputTarget.Dispose();
            return DropOrThrow(op, maxWorkingScale,
                $"Geometry shrink output allocation failed ({tw}x{th} px, w {w}, bounds {tight}).");
        }

        try
        {
            using var canvas = new ImmediateCanvas(tightTarget, w, maxWorkingScale, logicalSize: tight.Size);
            canvas.Clear();
            using (canvas.PushDeviceSpace())
            {
                // The full output holds the pass content with outBounds.Position at device origin; shift it left/up by
                // the sub-rect offset so the tight region lands at the tighter target's origin (the legacy blit).
                canvas.DrawRenderTarget(
                    outputTarget, new Point((outBounds.X - tight.X) * w, (outBounds.Y - tight.Y) * w));
            }
        }
        catch
        {
            tightTarget.Dispose();
            outputTarget.Dispose();
            op.Dispose();
            throw;
        }

        outputTarget.Dispose();
        op.Dispose();
        if (diagnostics != null)
            diagnostics.GpuPasses++;
        return RenderNodeOperation.CreateFromRenderTarget(
            tight, tight.Position, tightTarget, EffectiveScale.At(w));
    }

    private static RenderNodeOperation? ExecuteCompute(
        ComputePass pass, int width, int height, float w, Rect outBounds, RenderNodeOperation op,
        float outputScale, float workingScale, float maxWorkingScale, int maxDimension,
        PipelineDiagnostics? diagnostics, RenderTargetPool? pool)
    {
        IGraphicsContext? gfx = GraphicsContextFactory.SharedContext;
        if (s_forceComputeFallbackForTests || gfx is not { Supports3DRendering: true })
        {
            // Identity/Skip already returned in MapOneOperation; only CpuCallback reaches here without Vulkan.
            return ExecuteComputeCpuFallback(
                pass, width, height, w, outBounds, op, outputScale, workingScale, maxWorkingScale, maxDimension,
                diagnostics, pool);
        }

        // The source bakes at the pass-resolved w — not the boundary workingScale — because IComputeContext exposes
        // a single coordinate basis (Width/Height/WorkingScale at w) and dispatches texelFetch the source with
        // destination-derived coordinates; a boundary-scale bake would shift the sampling grid whenever the resolver
        // carried a lower w. w already mins against the op's carried density (C3.2), so this never re-raises a
        // reduced density; the budget clamp keeps the input allocatable (FR-037(b)).
        float inW = RenderNodeContext.ClampWorkingScaleToBufferBudget(op.Bounds, w, maxDimension);
        (int inBw, int inBh) = RenderNodeContext.DeviceBufferSize(op.Bounds, inW);
        if (inBw <= 0 || inBh <= 0)
            return op;

        // A bake throw must release op's already-detached pooled lease (C7); see ExecuteGeometry for the same guard.
        RenderTarget? inputTarget;
        try
        {
            inputTarget = MaterializeInput(op, inW, maxWorkingScale, diagnostics, pool);
        }
        catch
        {
            op.Dispose();
            throw;
        }

        if (inputTarget == null)
            return DropOrThrow(op, maxWorkingScale, $"Compute input materialization failed ({inBw}x{inBh} px).");

        // A layout-transition/context-loss throw here happens after op was detached from the working set and before
        // any cleanup scope owns the materialized input, so both must be released on the way out (C7).
        ITexture2D? sourceTexture;
        try
        {
            if (s_computePrepareFailureForTests is { } injected)
                throw injected;
            inputTarget.PrepareForSampling();
            if (diagnostics != null)
                diagnostics.FlushSyncs++;
            sourceTexture = inputTarget.Texture;
        }
        catch
        {
            inputTarget.Dispose();
            op.Dispose();
            throw;
        }
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
            outputTarget.PrepareForComputeWrite();
        }
        catch
        {
            outputTarget.Dispose();
            inputTarget.Dispose();
            op.Dispose();
            throw;
        }

        var ctx = new ComputeContext(
            gfx, sourceTexture, destTexture, width, height, w,
            pass.ColorScratchCount, pass.DepthScratchCount,
            scratch, depthScratch, diagnostics, pool);
        try
        {
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
        catch (Exception ex) when (
            pass.DispatchFailureBehavior == ComputeDispatchFailureBehavior.IdentityInPreview
            && ex is not OperationCanceledException
            && ex is not ComputeResourcePlanViolationException
            && !ComputeBackendPreparationFailure.IsMarked(ex)
            && !float.IsPositiveInfinity(maxWorkingScale))
        {
            // PixelSort's historic preview behavior: a transient dispatch failure keeps the source pixels when the
            // descriptor explicitly declares the dispatch policy. Delivery still throws rather than exporting an
            // unsorted frame; cancellation, resource-plan violations, and backend preparation failures always
            // propagate; allocation failures remain governed by C7's preview-drop contract.
            ReleaseComputeScratch(scratch, depthScratch);
            outputTarget.Dispose();
            inputTarget.Dispose();
            s_logger.LogWarning(ex,
                "Compute dispatch failed. Preview keeps the source because the pass declares IdentityInPreview.");
            return op;
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
        float outputScale, float workingScale, float maxWorkingScale, int maxDimension,
        PipelineDiagnostics? diagnostics, RenderTargetPool? pool)
    {
        if (pass.CpuCallback is not { } cpu)
            return op;

        // Same pass-resolved-w basis as the Vulkan path above: the callback's GeometrySession is sized at w, so the
        // baked input grid must match it (the EffectInput still carries inW for callbacks that bridge densities).
        float inW = RenderNodeContext.ClampWorkingScaleToBufferBudget(op.Bounds, w, maxDimension);
        (int inBw, int inBh) = RenderNodeContext.DeviceBufferSize(op.Bounds, inW);
        if (inBw <= 0 || inBh <= 0)
            return op;

        // A bake throw must release op's already-detached pooled lease (C7); see ExecuteGeometry for the same guard.
        RenderTarget? inputTarget;
        try
        {
            inputTarget = MaterializeInput(op, inW, maxWorkingScale, diagnostics, pool);
        }
        catch
        {
            op.Dispose();
            throw;
        }

        if (inputTarget == null)
            return DropOrThrow(op, maxWorkingScale, "Compute CPU-fallback input materialization failed.");

        RenderTarget? outputTarget = RenderTargetPool.Acquire(pool, width, height, diagnostics);
        if (outputTarget == null)
        {
            inputTarget.Dispose();
            return DropOrThrow(op, maxWorkingScale, "Compute CPU-fallback output allocation failed.");
        }

        bool discarded;
        Rect? shrunk;
        try
        {
            if (pass.CpuFallbackRequiresReadback)
            {
                inputTarget.PrepareForSampling();
                if (diagnostics != null)
                    diagnostics.FlushSyncs++;
            }

            var input = new EffectInput(
                inputTarget, op.Bounds, EffectiveScale.At(inW),
                readbackPrepared: pass.CpuFallbackRequiresReadback);
            using var canvas = new ImmediateCanvas(outputTarget, w, maxWorkingScale, logicalSize: outBounds.Size);
            canvas.Clear();
            var session = new GeometrySession(canvas, [input], outBounds, outputScale, w, maxWorkingScale, diagnostics);
            cpu(session);
            discarded = session.IsOutputDiscarded;
            shrunk = session.ShrunkOutputBounds;
        }
        catch
        {
            outputTarget.Dispose();
            inputTarget.Dispose();
            op.Dispose();
            throw;
        }

        inputTarget.Dispose();
        // DiscardOutput supersedes a requested shrink (§C3): a dropped pass produces nothing regardless of order.
        if (discarded)
        {
            outputTarget.Dispose();
            op.Dispose();
            return null;
        }

        if (shrunk is { } tight)
            return EmitShrunkGeometry(tight, w, outBounds, outputTarget, op, maxWorkingScale, diagnostics, pool);

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
        float maxWorkingScale, int maxDimension, PipelineDiagnostics? diagnostics, RenderTargetPool? pool)
    {
        var outputs = new List<RenderNodeOperation>(current.Count);
        try
        {
            for (int i = 0; i < current.Count; i++)
            {
                RenderNodeOperation op = current[i];
                current[i] = null!;

                float inW = RenderNodeContext.ClampWorkingScaleToBufferBudget(
            op.Bounds, CarriedWorkingScale(op, workingScale), maxDimension);
                (int bw, int bh) = RenderNodeContext.DeviceBufferSize(op.Bounds, inW);
                if (bw <= 0 || bh <= 0)
                {
                    outputs.Add(op);
                    continue;
                }

                // MaterializeInput bakes op via source.Render (C7): a throw there must still release op's pooled
                // lease, since the loop already detached op from current and neither disposal sweep would reach it
                // (the outer catch disposes only the branch outputs). Same guard as the three descriptor-path sites.
                RenderTarget? inputTarget;
                try
                {
                    inputTarget = MaterializeInput(op, inW, maxWorkingScale, diagnostics, pool);
                }
                catch
                {
                    op.Dispose();
                    throw;
                }

                if (inputTarget == null)
                {
                    // Delivery throws inside DropOrThrow; preview drops this input's branches and continues.
                    DropOrThrow(op, maxWorkingScale, $"Split input materialization failed ({bw}x{bh} px).");
                    continue;
                }

                try
                {
                    if (pass.RequiresReadback)
                    {
                        inputTarget.PrepareForSampling();
                        if (diagnostics != null)
                            diagnostics.FlushSyncs++;
                    }

                    var input = new EffectInput(
                        inputTarget, op.Bounds, EffectiveScale.At(inW), readbackPrepared: pass.RequiresReadback);
                    var emitter = new SplitEmitter(
                        input, inW, outputScale, maxWorkingScale, maxDimension, diagnostics, pool, outputs);
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
        int maxDimension, PipelineDiagnostics? diagnostics, RenderTargetPool? pool)
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

        float w = RenderNodeContext.ClampWorkingScaleToBufferBudget(union, workingScale, maxDimension);
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
        // SaveLayer the composite already opens per branch). The compose runs inside the try so an effect-supplied
        // factory that throws still releases the target lease and the branch ops.
        SKColorFilter? branchFilter = null;
        try
        {
            branchFilter = ComposeCompositeColorFilter(pass.InputColorFilters);
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
        try
        {
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
                    SKColorFilter next;
                    try
                    {
                        next = SKColorFilter.CreateCompose(filter, composed);
                    }
                    catch
                    {
                        filter.Dispose();
                        throw;
                    }

                    composed.Dispose();
                    filter.Dispose();
                    composed = next;
                }
            }
        }
        catch
        {
            // A mid-loop factory throw leaves the partially composed accumulator owned by no one; dispose it here.
            composed?.Dispose();
            throw;
        }

        return composed;
    }

    // A compute pass's ping-pong/depth scratch acquire failed. Distinct from a genuine dispatch bug so ExecuteCompute
    // can route it through the uniform C7 drop/throw (preview drops, delivery throws) rather than aborting preview.
    internal sealed class ComputeScratchAllocationException : Exception
    {
        public ComputeScratchAllocationException(string message) : base(message) { }

        public ComputeScratchAllocationException(string message, Exception inner) : base(message, inner) { }
    }

    // A callback exceeded the compiled resource plan. This is an authoring/contract violation, never a recoverable
    // preview dispatch failure, so it bypasses ComputeDispatchFailureBehavior.IdentityInPreview.
    private sealed class ComputeResourcePlanViolationException(string message) : InvalidOperationException(message);

    // Creates a non-pooled depth scratch target, normalizing a raw create failure to ComputeScratchAllocationException so
    // the pool-less branch routes through the same ExecuteCompute C7 drop/throw as the pooled branch, rather than aborting
    // preview as an unclassified dispatch bug.
    internal static ITexture2D CreateNonPooledDepthScratch(IGraphicsContext gfx, int width, int height)
    {
        try
        {
            return gfx.CreateTexture2D(width, height, TextureFormat.Depth32Float);
        }
        catch (Exception ex) when (ex is not ComputeScratchAllocationException)
        {
            throw new ComputeScratchAllocationException(
                $"Compute depth scratch allocation failed ({width}x{height} px).", ex);
        }
    }

    // The executor-owned resources handed to a compute node's dispatch callback: the materialized source and the
    // pass output texture, plus pooled color and depth scratch released when the pass ends.
    private sealed class ComputeContext(
        IGraphicsContext gfx, ITexture2D source, ITexture2D destination, int width, int height, float workingScale,
        int colorScratchLimit, int depthScratchLimit,
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
            if (colorScratch.Count >= colorScratchLimit)
            {
                throw new ComputeResourcePlanViolationException(
                    $"The compute callback exceeded its declared color scratch limit ({colorScratchLimit}).");
            }

            RenderTarget target = RenderTargetPool.Acquire(pool, width, height, diagnostics)
                ?? throw new ComputeScratchAllocationException(
                    $"Compute ping-pong scratch allocation failed ({width}x{height} px).");
            colorScratch.Add(target);
            // A pooled Skia surface may still have its acquire-time clear queued. Submit that work before Vulkan
            // writes the backing image, otherwise a later Skia flush can overwrite the compute result.
            ComputeBackendPreparationFailure.Run(target.PrepareForComputeWrite);
            return target.Texture
                ?? throw new ComputeScratchAllocationException("Pooled compute scratch has no Vulkan texture.");
        }

        public void CopySourceToDestination()
        {
            gfx.CopyTexture(source, destination);
        }

        public ITexture2D AcquireDepthScratch()
        {
            if (depthScratch.Count >= depthScratchLimit)
            {
                throw new ComputeResourcePlanViolationException(
                    $"The compute callback exceeded its declared depth scratch limit ({depthScratchLimit}).");
            }

            if (pool != null)
            {
                PooledTextureLease lease = pool.AcquireTexture(width, height, TextureFormat.Depth32Float, diagnostics)
                    ?? throw new ComputeScratchAllocationException(
                        $"Compute depth scratch allocation failed ({width}x{height} px).");
                depthScratch.Add(lease);
                return lease.Texture;
            }

            ITexture2D depth = CreateNonPooledDepthScratch(gfx, width, height);
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
        EffectInput input, float workingScale, float outputScale, float maxWorkingScale, int maxDimension,
        PipelineDiagnostics? diagnostics, RenderTargetPool? pool, List<RenderNodeOperation> outputs) : ISplitEmitter
    {
        public EffectInput Input => input;

        public float WorkingScale => workingScale;

        public void Emit(Rect logicalBounds, Action<GeometrySession> render)
        {
            ArgumentNullException.ThrowIfNull(render);

            float w = RenderNodeContext.ClampWorkingScaleToBufferBudget(logicalBounds, workingScale, maxDimension);
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
            Rect? shrunk;
            try
            {
                using var canvas = new ImmediateCanvas(target, w, maxWorkingScale, logicalSize: logicalBounds.Size);
                canvas.Clear();
                var session = new GeometrySession(
                    canvas, [input], logicalBounds, outputScale, w, maxWorkingScale, diagnostics);
                render(session);
                discarded = session.IsOutputDiscarded;
                shrunk = session.ShrunkOutputBounds;
            }
            catch
            {
                target.Dispose();
                throw;
            }

            // DiscardOutput supersedes a requested shrink (§C3), exactly as the single-op geometry path handles it.
            if (discarded)
            {
                target.Dispose();
                return;
            }

            if (shrunk is { } tight)
            {
                EmitShrunk(tight, w, logicalBounds, target);
                return;
            }

            if (diagnostics != null)
                diagnostics.GpuPasses++;
            outputs.Add(RenderNodeOperation.CreateFromRenderTarget(
                logicalBounds, logicalBounds.Position, target, EffectiveScale.At(w)));
        }

        // A split branch that called GeometrySession.SetOutputBounds tightens its emitted op to a sub-rect, mirroring
        // EmitShrunkGeometry: blit the sub-rect into a tighter pooled target and publish the tightened bounds. Unlike
        // the single-op geometry shrink, the branches' shared input scratch cannot be released first (later branches
        // still read it), so the tight lease transiently exceeds the declared bound by one — a dynamic split is
        // exempt from the static peak-live assert and a static split is covered by its split-shrink allowance
        // (AssertPeakLiveWithinPlan). The branch still counts one GpuPasses.
        private void EmitShrunk(Rect tight, float w, Rect logicalBounds, RenderTarget branchTarget)
        {
            (int tw, int th) = RenderNodeContext.DeviceBufferSize(tight, w);
            if (tw <= 0 || th <= 0)
            {
                // A degenerate (empty) shrink yields nothing, matching DiscardOutput and the §C3 empty-output drop.
                branchTarget.Dispose();
                return;
            }

            RenderTarget? tightTarget = RenderTargetPool.Acquire(pool, tw, th, diagnostics);
            if (tightTarget == null)
            {
                branchTarget.Dispose();
                if (float.IsPositiveInfinity(maxWorkingScale))
                    throw new InvalidOperationException($"Split branch shrink allocation failed ({tw}x{th} px, w {w}).");

                s_logger.LogWarning(
                    "Split branch shrink allocation failed ({Width}x{Height} px). Preview drops it.", tw, th);
                return;
            }

            try
            {
                using var canvas = new ImmediateCanvas(tightTarget, w, maxWorkingScale, logicalSize: tight.Size);
                canvas.Clear();
                using (canvas.PushDeviceSpace())
                {
                    canvas.DrawRenderTarget(
                        branchTarget, new Point((logicalBounds.X - tight.X) * w, (logicalBounds.Y - tight.Y) * w));
                }
            }
            catch
            {
                tightTarget.Dispose();
                branchTarget.Dispose();
                throw;
            }

            branchTarget.Dispose();
            if (diagnostics != null)
                diagnostics.GpuPasses++;
            outputs.Add(RenderNodeOperation.CreateFromRenderTarget(
                tight, tight.Position, tightTarget, EffectiveScale.At(w)));
        }
    }
}
