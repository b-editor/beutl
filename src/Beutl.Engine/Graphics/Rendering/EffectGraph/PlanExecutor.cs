using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
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
    private const int NoBranchOrdinal = -1;
    private static readonly ILogger s_logger = Log.CreateLogger("PlanExecutor");

    private readonly record struct BranchOperation(RenderNodeOperation Op, int Ordinal);

    private readonly record struct BranchExecutionResult(
        BranchOperation[] Outputs, int OrdinalGeneration, int OrdinalSpan, PassBackend Backend);

    // Failure injection is scoped to the caller's execution context. Dispatcher operations capture that context, so
    // render-thread tests still see their hooks without mutating process-wide state or racing parallel fixtures.
    private static readonly AsyncLocal<TestHooks?> s_testHooks = new();

    internal sealed class TestHooks
    {
        public Exception? ComputePrepareFailure { get; set; }

        public Exception? ComputeOutputPrepareFailure { get; set; }

        public Exception? ComputeCopyFailure { get; set; }

        public Exception? ComputeCopyPrepareFailure { get; set; }

        public Exception? ComputeInputDisposeFailure { get; set; }

        public Exception? GeometryInputDisposeFailure { get; set; }

        public Exception? GeometryOutputDisposeFailure { get; set; }

        public Exception? GeometryShrinkDrawFailure { get; set; }

        public Exception? SplitInputDisposeFailure { get; set; }

        public Exception? SplitBranchDisposeFailure { get; set; }

        public Exception? CompositeFilterDisposeFailure { get; set; }

        public Exception? CompositeFoldStageDisposeFailure { get; set; }

        public Action<SKColorFilter>? CompositeFoldCreated { get; set; }

        public Exception? SkiaFilterDisposeFailure { get; set; }

        public bool ForceComputeFallback { get; set; }

        internal TestHooks Copy()
            => new()
            {
                ComputePrepareFailure = ComputePrepareFailure,
                ComputeOutputPrepareFailure = ComputeOutputPrepareFailure,
                ComputeCopyFailure = ComputeCopyFailure,
                ComputeCopyPrepareFailure = ComputeCopyPrepareFailure,
                ComputeInputDisposeFailure = ComputeInputDisposeFailure,
                GeometryInputDisposeFailure = GeometryInputDisposeFailure,
                GeometryOutputDisposeFailure = GeometryOutputDisposeFailure,
                GeometryShrinkDrawFailure = GeometryShrinkDrawFailure,
                SplitInputDisposeFailure = SplitInputDisposeFailure,
                SplitBranchDisposeFailure = SplitBranchDisposeFailure,
                CompositeFilterDisposeFailure = CompositeFilterDisposeFailure,
                CompositeFoldStageDisposeFailure = CompositeFoldStageDisposeFailure,
                CompositeFoldCreated = CompositeFoldCreated,
                SkiaFilterDisposeFailure = SkiaFilterDisposeFailure,
                ForceComputeFallback = ForceComputeFallback,
            };
    }

    internal static IDisposable UseTestHooks(Action<TestHooks> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        TestHooks? previous = s_testHooks.Value;
        TestHooks hooks = previous?.Copy() ?? new TestHooks();
        configure(hooks);
        s_testHooks.Value = hooks;
        return new TestHookScope(previous, hooks);
    }

    private sealed class TestHookScope(TestHooks? previous, TestHooks current) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            if (ReferenceEquals(s_testHooks.Value, current))
                s_testHooks.Value = previous;
        }
    }

    public static RenderNodeOperation[] Execute(
        CompiledPlan plan,
        FrameResources resources,
        RenderNodeOperation[] inputs,
        float outputScale,
        float workingScale,
        float maxWorkingScale,
        PipelineDiagnostics? diagnostics,
        RenderTargetPool? pool,
        RenderIntent renderIntent,
        int startPass = 0,
        PrefixCaptureSink? captureSink = null,
        bool isRenderCacheEnabled = true,
        RenderPullPurpose pullPurpose = RenderPullPurpose.Frame,
        Func<int, int, RenderTarget?>? renderTargetFactory = null)
    {
        renderIntent = RenderPolicyValidation.Validate(renderIntent, nameof(renderIntent));
        pullPurpose = RenderPolicyValidation.Validate(pullPurpose, nameof(pullPurpose));
        var inputBranches = new BranchOperation[inputs.Length];
        for (int i = 0; i < inputs.Length; i++)
            inputBranches[i] = new BranchOperation(inputs[i], NoBranchOrdinal);

        BranchExecutionResult result = ExecuteBranches(
            plan, resources, inputBranches, outputScale, workingScale, maxWorkingScale,
            diagnostics, pool, renderIntent, startPass, captureSink, isRenderCacheEnabled, pullPurpose,
            renderTargetFactory: renderTargetFactory);
        return result.Outputs.Select(static branch => branch.Op).ToArray();
    }

    private static BranchExecutionResult ExecuteBranches(
        CompiledPlan plan,
        FrameResources resources,
        BranchOperation[] inputs,
        float outputScale,
        float workingScale,
        float maxWorkingScale,
        PipelineDiagnostics? diagnostics,
        RenderTargetPool? pool,
        RenderIntent renderIntent,
        int startPass = 0,
        PrefixCaptureSink? captureSink = null,
        bool isRenderCacheEnabled = true,
        RenderPullPurpose pullPurpose = RenderPullPurpose.Frame,
        int ordinalGeneration = 0,
        int ordinalSpan = 0,
        PassBackend initialBackend = PassBackend.Skia,
        Func<int, int, RenderTarget?>? renderTargetFactory = null)
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
        var current = new List<BranchOperation>(inputs);
        PassBackend runtimeBackend = initialBackend;
        try
        {
            for (int k = startPass; k < plan.Passes.Length; k++)
            {
                CompiledPass pass = plan.Passes[k];
                bool tracksBackendRecursively = pass is NestedGraphPass;
                bool supportsCompute = pass is ComputePass && SupportsCompute();
                bool consumesOnSkia = pass switch
                {
                    ComputePass { Fallback.Kind: ComputeFallbackKind.Identity or ComputeFallbackKind.Skip }
                        when !supportsCompute => false,
                    _ => true,
                };

                // Count the runtime Vulkan -> Skia synchronization before materializing an output written by compute.
                // A real compute pass counts its Skia -> Vulkan synchronization at PrepareForSampling below. An
                // Identity/Skip fallback consumes nothing, so compiler metadata alone must not increment the counter.
                if (!tracksBackendRecursively
                    && current.Count > 0
                    && consumesOnSkia
                    && runtimeBackend == PassBackend.Vulkan
                    && diagnostics != null)
                    diagnostics.FlushSyncs++;

                bool wroteVulkanOutput = false;
                switch (pass)
                {
                    case SplitPass split:
                        int splitSpan = ExecuteSplit(
                            split, current, outputScale, workingScale, maxWorkingScale, maxDimension,
                            diagnostics, pool, renderIntent, pullPurpose);
                        if (split.IsDynamicOutputs)
                        {
                            ordinalGeneration++;
                            ordinalSpan = splitSpan;
                        }
                        else
                        {
                            ordinalSpan = Math.Max(1, ordinalSpan) * split.BranchCount;
                        }
                        break;
                    case CompositePass composite:
                        ExecuteComposite(
                            composite, current, outputScale, workingScale, maxWorkingScale, maxDimension,
                            diagnostics, pool, renderIntent, pullPurpose);
                        ordinalGeneration = 0;
                        ordinalSpan = 0;
                        break;
                    case NestedGraphPass nestedGraph:
                        ExecuteNestedGraph(
                            nestedGraph, current, outputScale, workingScale, maxWorkingScale, maxDimension,
                            diagnostics, pool, isRenderCacheEnabled, pullPurpose, renderIntent,
                            ref ordinalGeneration, ref ordinalSpan, ref runtimeBackend, renderTargetFactory);
                        break;
                    case CustomRenderNodePass customNode:
                        int customSpan = ExecuteCustomRenderNode(
                            customNode, current, outputScale, maxWorkingScale, diagnostics, pool,
                            isRenderCacheEnabled, pullPurpose, renderIntent, renderTargetFactory);
                        if (customSpan >= 0)
                        {
                            ordinalGeneration++;
                            ordinalSpan = customSpan;
                        }
                        break;
                    default:
                        wroteVulkanOutput = MapDescriptorPass(
                            pass, resources.Passes[k], ExpectedInputBounds(plan, resources, k), current,
                            outputScale, workingScale, maxWorkingScale, maxDimension,
                            diagnostics, pool, captureSink != null && k == captureSink.CapturePassIndex ? captureSink : null,
                            renderIntent, pullPurpose);
                        break;
                }

                if (!tracksBackendRecursively && current.Count > 0)
                {
                    if (wroteVulkanOutput)
                        runtimeBackend = PassBackend.Vulkan;
                    else if (consumesOnSkia)
                        runtimeBackend = PassBackend.Skia;
                }
            }

            // A capture frame deliberately retains the prefix pass's buffer past its plan-declared last use (the C10
            // cross-frame lease): exactly one buffer, so the frame's intra-frame peak is the plan's declared bound + 1.
            // Assert against that inflated bound rather than skipping, so a capture that over-retains is still caught.
            AssertPeakLiveWithinPlan(plan, inputs.Length, pool, leaseBaseline, captureSink?.Captured == true ? 1 : 0);

            return new BranchExecutionResult(current.ToArray(), ordinalGeneration, ordinalSpan, runtimeBackend);
        }
        catch
        {
            DisposeBranchOperations(CollectionsMarshal.AsSpan(current));
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
        if (pool == null || inputCount != 1 || !plan.Resources.IsStaticallyBounded)
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

    // Executes a nested-graph pass: per branch, describe the child graph at the branch's bounds and index, then
    // rebind onto that branch's persistent plan-cache entry or compile on a structural/context miss. Each branch owns
    // a child cache scope for deeper nesting, so local node ordinals never collide across branches. An empty child
    // graph is the identity (the branch passes through).
    private static void ExecuteNestedGraph(
        NestedGraphPass pass, List<BranchOperation> current,
        float outputScale, float workingScale,
        float maxWorkingScale, int maxDimension, PipelineDiagnostics? diagnostics, RenderTargetPool? pool,
        bool isRenderCacheEnabled, RenderPullPurpose pullPurpose, RenderIntent renderIntent,
        ref int ordinalGeneration, ref int ordinalSpan, ref PassBackend runtimeBackend,
        Func<int, int, RenderTarget?>? renderTargetFactory)
    {
        var branchResults = new List<BranchExecutionResult>(current.Count);
        var branchEntryOrdinals = new List<int>(current.Count);
        int initialGeneration = ordinalGeneration;
        int initialSpan = Math.Max(1, ordinalSpan);
        var liveBranchIndices = new HashSet<int>();
        for (int i = 0; i < current.Count; i++)
            liveBranchIndices.Add(ResolveBranchOrdinal(current[i].Ordinal, i));
        pass.PlanCache.PruneBranches(liveBranchIndices);
        try
        {
            for (int i = 0; i < current.Count; i++)
            {
                RenderNodeOperation op = current[i].Op;
                int branchIndex = ResolveBranchOrdinal(current[i].Ordinal, i);
                // The branch inherits the carried density of the op feeding it (FR-012/C3.2), like every
                // materializing single-op path: the raw outer workingScale would re-raise a density an
                // upstream clamped op already reduced.
                float branchScale = CarriedWorkingScale(op, workingScale);
                // A DescribeBranch that registers a native shader and then throws would strand it; abort the still-open
                // engine-owned builder (Build transfers ownership to the graph, after which Abort is a no-op).
                NestedGraphBranchPlanCache branchCache = pass.PlanCache.GetBranch(branchIndex);
                var builder = new EffectGraphBuilder(
                    op.Bounds, outputScale, branchScale, renderIntent, maxWorkingScale, branchCache.Children,
                    pullPurpose);
                try
                {
                    pass.DescribeBranch(builder, branchIndex);
                    using EffectGraph graph = builder.Build();
                    object contextId = GraphicsContextFactory.SharedContext ?? NestedGraphPlanCache.NoGraphicsContext;
                    StructuralKey key = StructuralKey.Compute(graph);
                    CompiledPlan branchPlan;
                    if (branchCache.Plan.TryGet(key, contextId, out CompiledPlan cached))
                    {
                        branchPlan = ParameterBlock.Extract(graph).RebindOnto(cached);
                    }
                    else
                    {
                        branchPlan = EffectGraphCompiler.Compile(graph, diagnostics);
                        branchCache.Plan.Store(key, contextId, branchPlan);
                    }
                    FrameResources branchResources = EffectGraphCompiler.ResolveResources(
                        branchPlan, builder.Bounds, branchScale, maxDimension);
                    // Hand ownership of op to the recursion only once it is about to consume it: a DescribeBranch/
                    // Build/Compile/ResolveResources throw above still leaves op in current for the catch to dispose.
                    current[i] = default;
                    BranchExecutionResult branchResult = ExecuteBranches(
                        branchPlan, branchResources, [new BranchOperation(op, branchIndex)],
                        outputScale, branchScale, maxWorkingScale, diagnostics, pool, renderIntent,
                        isRenderCacheEnabled: isRenderCacheEnabled,
                        pullPurpose: pullPurpose,
                        ordinalGeneration: ordinalGeneration,
                        ordinalSpan: ordinalSpan,
                        initialBackend: runtimeBackend,
                        renderTargetFactory: renderTargetFactory);
                    runtimeBackend = branchResult.Backend;
                    branchResults.Add(branchResult);
                    branchEntryOrdinals.Add(branchIndex);
                }
                finally
                {
                    builder.Abort();
                }
            }
        }
        catch
        {
            foreach (BranchExecutionResult result in branchResults)
                DisposeBranchOperations(result.Outputs);
            DisposeBranchOperations(CollectionsMarshal.AsSpan(current));
            current.Clear();
            throw;
        }

        current.Clear();
        bool resetInsideChild = branchResults.Exists(result => result.OrdinalGeneration > initialGeneration);
        if (!resetInsideChild)
        {
            foreach (BranchExecutionResult result in branchResults)
                current.AddRange(result.Outputs);
            if (branchResults.Count > 0)
            {
                ordinalGeneration = branchResults.Max(static result => result.OrdinalGeneration);
                ordinalSpan = branchResults.Max(static result => result.OrdinalSpan);
            }

            return;
        }

        // A dynamic fan-out inside a child plan starts a local execution-time ordinal namespace. Concatenate each
        // parent branch's local namespace in authored parent order so sibling recursive executions cannot both
        // publish ordinal zero. The span includes discarded emits, preserving holes for downstream offsets.
        int nextOrdinal = 0;
        int resultGeneration = initialGeneration + 1;
        for (int resultIndex = 0; resultIndex < branchResults.Count; resultIndex++)
        {
            BranchExecutionResult result = branchResults[resultIndex];
            bool locallyReset = result.OrdinalGeneration > initialGeneration;
            int localBase;
            int localSpan;
            if (locallyReset)
            {
                localBase = 0;
                localSpan = result.OrdinalSpan;
                resultGeneration = Math.Max(resultGeneration, result.OrdinalGeneration);
            }
            else
            {
                // A non-reset child preserves the incoming ordinal namespace. Derive this sibling's true authored
                // slice from the branch ordinal it entered with and the span multiplication performed by static
                // splits. Looking at surviving outputs would collapse leading/trailing dropped emits.
                localSpan = Math.Max(1, result.OrdinalSpan / initialSpan);
                localBase = branchEntryOrdinals[resultIndex] * localSpan;
            }

            foreach (BranchOperation output in result.Outputs)
            {
                int localOrdinal = output.Ordinal == NoBranchOrdinal ? 0 : output.Ordinal - localBase;
                current.Add(new BranchOperation(output.Op, nextOrdinal + localOrdinal));
            }

            nextOrdinal += localSpan;
        }

        ordinalGeneration = resultGeneration;
        ordinalSpan = nextOrdinal;
    }

    // The input rect the resolver assumed pass k consumes: the previous pass's resolved output ROI (its full bounds
    // when full-frame), or the graph input for the first pass. An actual op narrower than this signals a
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
    // The node instance lives in the owning plan node's hierarchical runtime cache. Keeping that cache outside the
    // CPU-only CompiledPlan preserves custom node state across parameter rebinds while nested node/branch scopes keep
    // equal local ordinals isolated. Cache replacement/pruning retires the node immediately, but active output leases
    // defer its actual disposal until every lazily-rendered operation it returned has expired.
    // Returns the local ordinal span when the node's execution-time output count started a fresh dynamic ordinal
    // namespace (the caller bumps the generation, exactly like a dynamic split), or -1 for an identity mapping.
    private static int ExecuteCustomRenderNode(
        CustomRenderNodePass pass, List<BranchOperation> current,
        float outputScale, float maxWorkingScale,
        PipelineDiagnostics? diagnostics, RenderTargetPool? pool, bool isRenderCacheEnabled,
        RenderPullPurpose pullPurpose,
        RenderIntent renderIntent,
        Func<int, int, RenderTarget?>? renderTargetFactory)
    {
        RenderNodeOperation[] inputs = current.Select(static branch => branch.Op).ToArray();
        int[] inputOrdinals = current.Select(static branch => branch.Ordinal).ToArray();
        // Ownership passes to the child node (which disposes or returns each input); clearing here keeps the outer
        // Execute catch from disposing ops the child now owns, and stops a double-drop on the passthrough path.
        current.Clear();

        // Cache replacement/factory creation runs after current was cleared, so a throw would strand the detached
        // inputs in neither disposal sweep; release them here (C7).
        CustomRenderNodePlanCache.NodeEntry nodeEntry;
        try
        {
            nodeEntry = pass.NodeCache.GetOrCreate(pass.Resource, pass.Factory);
        }
        catch
        {
            RenderNodeOperation.DisposeAll(inputs);
            throw;
        }

        try
        {
            FilterEffectRenderNode node = nodeEntry.Node;
            node.Update(pass.Resource);
            var childContext = new RenderNodeContext(
                inputs, renderIntent, outputScale, maxWorkingScale, pullPurpose)
            {
                Diagnostics = diagnostics,
                Pool = pool,
                RenderTargetFactory = renderTargetFactory,
                IsRenderCacheEnabled = isRenderCacheEnabled,
                // Inputs arrive from the executing parent plan rather than this node's container children. Their
                // pixels may change while bounds and density stay fixed, so a nested plan must fail closed instead
                // of resuming a content-blind retained prefix.
                InputSubtreeStableOverride = false,
                // A custom node is opaque to the compiler, so its backward bounds contract is unknown. Passing the
                // outer crop directly would let a later expanding pass (blur, shadow, stroke) clip the halo before
                // it is produced. The custom node therefore receives the conservative full-input request; only
                // descriptor nodes with a compiler-visible bounds contract participate in ROI propagation.
                RequestedBounds = Rect.Invalid,
            };
            RenderNodeOperation[] outputs = node.Process(childContext)
                ?? throw new InvalidOperationException("A custom render node returned a null operation array.");
            if (outputs.Length > 0 && Array.Exists(outputs, static output => output is null))
            {
                RenderNodeOperation.DisposeAll(outputs);
                throw new InvalidOperationException("A custom render node returned a null operation.");
            }

            int[] outputOrdinals;
            bool startsLocalNamespace;
            try
            {
                outputOrdinals = MapCustomOutputOrdinals(inputOrdinals, outputs.Length, out startsLocalNamespace);
            }
            catch
            {
                RenderNodeOperation.DisposeAll(outputs);
                throw;
            }

            var mapped = new BranchOperation[outputs.Length];
            int mappedCount = 0;
            try
            {
                for (; mappedCount < outputs.Length; mappedCount++)
                {
                    RenderNodeOperation output = outputs[mappedCount];
                    Action release = nodeEntry.AcquireOutputLease();
                    RenderNodeOperation leased;
                    try
                    {
                        leased = RenderNodeOperation.CreateDecorator(
                            output, output.Render, output.HitTest, release);
                    }
                    catch
                    {
                        release();
                        throw;
                    }

                    mapped[mappedCount] = new BranchOperation(leased, outputOrdinals[mappedCount]);
                }

                current.AddRange(mapped);
            }
            catch
            {
                DisposeBranchOperations(mapped.AsSpan(0, mappedCount));
                RenderNodeOperation.DisposeAll(outputs.AsSpan(mappedCount));
                throw;
            }

            return startsLocalNamespace ? outputs.Length : -1;
        }
        catch
        {
            // A throw mid-Process leaves ownership ambiguous; dispose the inputs best-effort (idempotent, so a
            // partially-consumed set is safe) so nothing the child had not yet adopted is stranded (C7).
            RenderNodeOperation.DisposeAll(inputs);
            throw;
        }
    }

    private static int[] MapCustomOutputOrdinals(int[] inputOrdinals, int outputCount, out bool startsLocalNamespace)
    {
        startsLocalNamespace = false;
        if (outputCount == inputOrdinals.Length)
            return inputOrdinals;
        if (inputOrdinals.Length == 1)
        {
            // The output count is execution-time-resolved and can differ across sibling branches, so no
            // parent-ordinal stride is collision-free; a local dynamic namespace (like a dynamic split) relies on
            // the nested-graph reconciliation to concatenate siblings.
            startsLocalNamespace = true;
            return Enumerable.Range(0, outputCount).ToArray();
        }
        if (Array.TrueForAll(inputOrdinals, static ordinal => ordinal == NoBranchOrdinal))
            return Enumerable.Repeat(NoBranchOrdinal, outputCount).ToArray();
        if (outputCount == 0)
            return [];

        throw new InvalidOperationException(
            "A custom render node changed the size of an active split branch set, so stable branch identity cannot "
            + "be preserved. Composite the branch set before invoking the custom node, or return one output per input.");
    }

    // Applies a descriptor pass to every current operation independently. A single upstream operation uses the
    // per-frame resolution (resolved size, working-scale carry, empty-ROI skip); a fanned-out set (an upstream
    // opaque split) sizes each branch from its own bounds — a coordinate-invariant fused pass is identity, so an
    // operation's output bounds equal its input bounds.
    private static bool MapDescriptorPass(
        CompiledPass pass, PassResolution resolution, Rect expectedInput, List<BranchOperation> current,
        float outputScale, float workingScale, float maxWorkingScale, int maxDimension,
        PipelineDiagnostics? diagnostics, RenderTargetPool? pool, PrefixCaptureSink? captureSink,
        RenderIntent renderIntent, RenderPullPurpose pullPurpose)
    {
        bool linear = current.Count == 1;
        // The prefix cache only ever captures a linear (single-op) pass output (C10 v1 scope), so a fanned-out set is
        // never a capture site; drop the sink in that case so no partial branch is retained.
        PrefixCaptureSink? sink = linear ? captureSink : null;
        var outputs = new List<BranchOperation>(current.Count);
        bool wroteVulkanOutput = false;
        try
        {
            for (int i = 0; i < current.Count; i++)
            {
                RenderNodeOperation op = current[i].Op;
                int branchOrdinal = current[i].Ordinal;
                current[i] = default;
                // A null result drops this pass output and continues: either an empty resolved output (a shrinking
                // pass) or a preview allocation-failure (C7; delivery renders throw instead of returning null).
                RenderNodeOperation? mapped;
                try
                {
                    mapped = MapOneOperation(
                        pass, resolution, expectedInput, linear, op, outputScale, workingScale, maxWorkingScale,
                        maxDimension, diagnostics, pool, sink, renderIntent, pullPurpose,
                        out bool operationWroteVulkanOutput);
                    wroteVulkanOutput |= operationWroteVulkanOutput;
                }
                catch
                {
                    // The op is already detached from `current`, so the outer sweeps (this method's outputs sweep,
                    // Execute's current sweep) can't reach it; a throw BEFORE MapOneOperation takes ownership — a
                    // plugin bounds lambda inside ForwardBounds — would strand its pooled lease. Dispose is
                    // idempotent, so the C7 paths that already disposed op before rethrowing are unaffected.
                    Exception? cleanupFailure = null;
                    CaptureDisposeFailure(op, ref cleanupFailure);
                    LogCleanupFailure(cleanupFailure, "descriptor map failure cleanup");
                    throw;
                }

                if (mapped != null)
                    outputs.Add(new BranchOperation(mapped, branchOrdinal));
            }
        }
        catch
        {
            DisposeBranchOperations(CollectionsMarshal.AsSpan(outputs));
            throw;
        }

        current.Clear();
        current.AddRange(outputs);
        return wroteVulkanOutput;
    }

    private static RenderNodeOperation? MapOneOperation(
        CompiledPass pass, PassResolution resolution, Rect expectedInput, bool linear, RenderNodeOperation op,
        float outputScale, float workingScale, float maxWorkingScale, int maxDimension,
        PipelineDiagnostics? diagnostics, RenderTargetPool? pool, PrefixCaptureSink? captureSink,
        RenderIntent renderIntent, RenderPullPurpose pullPurpose, out bool wroteVulkanOutput)
    {
        wroteVulkanOutput = false;
        // A compute pass on a context without Vulkan takes its declared fallback before any allocation (C6/A7).
        if (pass is ComputePass compute && !SupportsCompute())
        {
            switch (compute.Fallback.Kind)
            {
                case ComputeFallbackKind.Identity:
                    return op;
                case ComputeFallbackKind.Skip:
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
                    else if (pass is ComputePass)
                        outBounds = resolved.Intersect(op.Bounds);
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
                    maxDimension, diagnostics, pool, renderIntent, pullPurpose);
            case ComputePass computePass:
                return ExecuteCompute(
                    computePass, width, height, w, outBounds, op, outputScale, workingScale, maxWorkingScale,
                    maxDimension, diagnostics, pool, renderIntent, pullPurpose, out wroteVulkanOutput);
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
                Exception? cleanupFailure = null;
                CaptureDisposeFailure(op, ref cleanupFailure);
                LogCleanupFailure(cleanupFailure, "Skia-filter factory failure cleanup");
                throw;
            }

            if (preparedSkiaFilter == null)
                return op;
        }

        RenderTarget? target = RenderTargetPool.Acquire(pool, width, height, diagnostics);
        if (target == null)
        {
            Exception? cleanupFailure = null;
            CaptureDisposeFailure(preparedSkiaFilter, ref cleanupFailure);
            LogCleanupFailure(cleanupFailure, "descriptor output-allocation cleanup");
            return DropOrThrow(op, renderIntent,
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
        if (pass is FusedShaderPass { NeedsSourceHaloBake: true, Stages: [RuntimeShaderStage wsStage] })
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
                    Exception? cleanupFailure = null;
                    CaptureDisposeFailure(preparedSkiaFilter, ref cleanupFailure);
                    CaptureDisposeFailure(target, ref cleanupFailure);
                    LogCleanupFailure(cleanupFailure, "fused source-halo allocation cleanup");
                    return DropOrThrow(op, renderIntent,
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
                        fused, target, w, outBounds, op, maxWorkingScale, diagnostics, renderIntent,
                        pullPurpose, fusedSrcTarget, fusedSrcRect, fusedSrcScale);
                    break;
                case SkiaFilterPass:
                    SKImageFilter filter = preparedSkiaFilter!;
                    preparedSkiaFilter = null;
                    ExecuteSkia(filter, target, w, outBounds, op, maxWorkingScale, renderIntent, pullPurpose);
                    break;
                default:
                    throw new NotSupportedException(
                        $"Pass '{pass.GetType().Name}' is not executable by the descriptor path.");
            }
        }
        catch
        {
            Exception? cleanupFailure = null;
            CaptureDisposeFailure(preparedSkiaFilter, ref cleanupFailure);
            CaptureDisposeFailure(fusedSrcTarget, ref cleanupFailure);
            CaptureDisposeFailure(target, ref cleanupFailure);
            CaptureDisposeFailure(op, ref cleanupFailure);
            LogCleanupFailure(cleanupFailure, "descriptor-pass failure cleanup");
            throw;
        }

        // The src snapshot must stay alive through the fused draw, so the halo buffer's lease releases only here —
        // it overlaps the output lease exactly as declared ([idx, idx] scratch, §C3.1).
        Exception? sourceCleanupFailure = null;
        CaptureDisposeFailure(fusedSrcTarget, ref sourceCleanupFailure);
        if (sourceCleanupFailure is { } sourceFailure)
        {
            CaptureDisposeFailure(target, ref sourceCleanupFailure);
            CaptureDisposeFailure(op, ref sourceCleanupFailure);
            ExceptionDispatchInfo.Capture(sourceFailure).Throw();
        }

        if (diagnostics != null)
            diagnostics.GpuPasses++;

        // Retain a shallow copy for the pass-prefix output cache (C10) before the op is threaded downstream: the
        // ref keeps the pooled buffer alive across frames so the next frame can resume from this pass's output.
        captureSink?.Capture(target, outBounds, EffectiveScale.At(w));

        Exception? operationCleanupFailure = null;
        CaptureDisposeFailure(op, ref operationCleanupFailure);
        if (operationCleanupFailure is { } operationFailure)
        {
            CaptureDisposeFailure(target, ref operationCleanupFailure);
            ExceptionDispatchInfo.Capture(operationFailure).Throw();
        }

        return RenderNodeOperation.CreateFromRenderTarget(
            outBounds, outBounds.Position, target, EffectiveScale.At(w));
    }

    // The C7 allocation-failure normalization for a per-operation pass: delivery throws and preview drops the pass
    // output (returns null) and logs. Either way the consumed input is released.
    private static RenderNodeOperation? DropOrThrow(
        RenderNodeOperation op, RenderIntent renderIntent, string message)
    {
        if (renderIntent == RenderIntent.Delivery)
        {
            Exception? cleanupFailure = null;
            CaptureDisposeFailure(op, ref cleanupFailure);
            LogCleanupFailure(cleanupFailure, "delivery allocation-failure cleanup");
            throw new InvalidOperationException(message);
        }

        Exception? previewCleanupFailure = null;
        CaptureDisposeFailure(op, ref previewCleanupFailure);
        LogCleanupFailure(previewCleanupFailure, "preview allocation-failure cleanup");
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
        RenderIntent renderIntent, RenderPullPurpose pullPurpose,
        RenderTarget? srcTarget = null, Rect srcRect = default, float srcScale = 0f)
    {
        // srcTarget is the pass-scoped halo buffer (§C3.1): the source bakes over srcRect (⊇ outBounds) at srcScale
        // and the src shader's local matrix re-registers image coordinates to the output's device space, so the
        // shader samples the halo a downstream deflate cropped out of the output rect. Without it the source bakes
        // in place and snapshots the output target.
        if (srcTarget != null)
            BakeSource(srcTarget, srcScale, srcRect, source, maxWorkingScale, renderIntent, pullPurpose, paint: null);
        else
            BakeSource(target, w, outBounds, source, maxWorkingScale, renderIntent, pullPurpose, paint: null);

        // A whole-source stage samples src at arbitrary coordinates, so its declared tile mode governs out-of-bounds
        // reads (matching the legacy custom effect); a fused snippet run only samples the current pixel, so Decal.
        SKShaderTileMode srcTile = pass.Stages is [RuntimeShaderStage { Source.Kind: SkslSourceKind.WholeSource } ws]
            ? ws.SrcTileMode
            : SKShaderTileMode.Decal;
        using SKImage srcImage = (srcTarget ?? target).Value.Snapshot();
        using SKShader srcShader = srcTarget != null
            ? srcImage.ToShader(srcTile, srcTile, SrcHaloLocalMatrix(w, srcScale, outBounds, srcRect))
            : srcImage.ToShader(srcTile, srcTile);

        var uniformContext = new PassUniformContext(
            w, target.Width, target.Height, outBounds, renderIntent, pullPurpose, diagnostics);
        var disposables = new List<IDisposable>();
        Exception? executionFailure = null;
        try
        {
            SKShader composed = ComposeStages(pass, srcShader, uniformContext, diagnostics, disposables);
            using var paint = new SKPaint { Shader = composed };
            using var canvas = new ImmediateCanvas(
                target, renderIntent, w, maxWorkingScale, logicalSize: outBounds.Size,
                pullPurpose: pullPurpose);
            canvas.Clear();
            using (canvas.PushDeviceSpace())
            {
                canvas.Canvas.DrawRect(new SKRect(0, 0, target.Width, target.Height), paint);
            }
        }
        catch (Exception ex)
        {
            executionFailure = ex;
            throw;
        }
        finally
        {
            Exception? cleanupFailure = DisposeDisposablesCapturingFailure(disposables);
            if (cleanupFailure != null)
            {
                if (executionFailure != null)
                    LogCleanupFailure(cleanupFailure, "fused stage cleanup");
                else
                    ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
            }
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
                    SKImageFilter? predecessor = filter;
                    filter = outer;
                    predecessor?.Dispose();
                    if (predecessor != null && s_testHooks.Value?.SkiaFilterDisposeFailure is { } injected)
                    {
                        s_testHooks.Value.SkiaFilterDisposeFailure = null;
                        throw injected;
                    }
                }
            }

            return filter;
        }
        catch
        {
            Exception? cleanupFailure = null;
            CaptureDisposeFailure(filter, ref cleanupFailure);
            LogCleanupFailure(cleanupFailure, "Skia-filter construction failure cleanup");
            throw;
        }
    }

    private static void ExecuteSkia(
        SKImageFilter filter, RenderTarget target, float w, Rect outBounds,
        RenderNodeOperation source, float maxWorkingScale, RenderIntent renderIntent,
        RenderPullPurpose pullPurpose)
    {
        try
        {
            using var paint = new SKPaint { ImageFilter = filter };
            BakeSource(target, w, outBounds, source, maxWorkingScale, renderIntent, pullPurpose, paint);
        }
        catch
        {
            Exception? cleanupFailure = null;
            CaptureDisposeFailure(filter, ref cleanupFailure);
            LogCleanupFailure(cleanupFailure, "Skia-filter draw failure cleanup");
            throw;
        }

        // SKPaint.Dispose does not own its image filter. A successful draw transfers cleanup here; a failed draw
        // releases it in the catch above without allowing a native cleanup fault to replace the draw failure.
        filter.Dispose();
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
        RenderTarget target, float w, Rect outBounds, RenderNodeOperation source, float maxWorkingScale,
        RenderIntent renderIntent, RenderPullPurpose pullPurpose, SKPaint? paint)
    {
        using var canvas = new ImmediateCanvas(
            target, renderIntent, w, maxWorkingScale, logicalSize: outBounds.Size,
            pullPurpose: pullPurpose);
        canvas.Clear();
        using (canvas.PushTransform(Matrix.CreateTranslation(-outBounds.X, -outBounds.Y)))
        using (paint != null ? canvas.PushPaint(paint) : default)
        {
            source.Render(canvas);
        }
    }

    private static SKShader ComposeStages(
        FusedShaderPass pass, SKShader srcShader, in PassUniformContext uniformContext,
        PipelineDiagnostics? diagnostics, List<IDisposable> disposables)
    {
        ImmutableArray<FusedStage> stages = pass.Stages;
        ImmutableArray<RuntimeProgram> programs = pass.ProgramLayout.RuntimePrograms;
        SKShader current = srcShader;
        int i = 0;
        int programIndex = 0;
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
                RuntimeProgram program = programs[programIndex++];
                current = BuildRuntimeRun(
                    program, stages, current, uniformContext, diagnostics, disposables);
                i += program.StageCount;
            }
        }

        return current;
    }

    private static SKShader BuildRuntimeRun(
        RuntimeProgram program, ImmutableArray<FusedStage> stages, SKShader srcChild,
        in PassUniformContext uniformContext,
        PipelineDiagnostics? diagnostics, List<IDisposable> disposables)
    {
        // The program (merged/whole SKSL parse) is structural, so it is cached process-wide by a source-identity
        // descriptor: a warm run neither allocates run metadata, re-merges, takes the global map lock, nor re-parses,
        // keeping ProgramCreations at zero (SC-002). The
        // leased builder (which owns its SKRuntimeEffect) is reused, its per-frame uniforms/children overwritten and
        // Build() re-run below; only the lease is disposed here — disposing the builder would free the shared effect
        // (the cache disposes it on eviction). Its built shader is independent and IS disposed per frame. The lease
        // spans the whole bind: a deferred child resolved below can render a DrawableBrush whose nested pass requests
        // this same signature, and the lease is what routes that reentrant use onto its own builder.
        using ProgramCache.Lease lease = ProgramCache.GetOrCreate(program, diagnostics);
        SKRuntimeShaderBuilder builder = lease.Builder;
        // Clear the reused builder's prior-frame state before rebinding (still O(bindings) per frame): a same-signature
        // run that omits a binding this frame must see the program default, not the stale value — and a stale
        // executor-owned child would reference a shader disposed after that earlier draw.
        builder.Uniforms.Reset();
        builder.Children.Reset();
        builder.Children[program.ChildName] = srcChild;

        for (int k = 0; k < program.StageCount; k++)
        {
            var stage = (RuntimeShaderStage)stages[program.StartStage + k];
            SkslSource source = stage.Source;
            foreach (UniformBinding uniform in stage.Uniforms)
            {
                string name = program.IsWholeSource
                    ? uniform.Name
                    : SkslSnippetMerger.GetPrefixedName(source, k, uniform.Name);
                uniform.Apply(builder, name, in uniformContext);
            }
            // An eager child/sampler (a LUT, curve textures) is graph-/caller-owned and left alone; a deferred
            // child's shader is produced here from this pass's real density (executorOwned == true) and tracked for
            // disposal after the draw. Either way the graph releases eager bindings after execution even when this
            // pass is skipped for an empty ROI (contract A2).
            foreach (ChildBinding child in stage.Children)
            {
                SKShader childShader = child.Resolve(in uniformContext, out bool executorOwned);
                if (executorOwned)
                    disposables.Add(childShader);
                string name = program.IsWholeSource
                    ? child.Name
                    : SkslSnippetMerger.GetPrefixedName(source, k, child.Name);
                builder.Children[name] = childShader;
            }
        }

        return Track(builder.Build(), disposables);
    }

    private static SKShader Track(SKShader shader, List<IDisposable> disposables)
    {
        disposables.Add(shader);
        return shader;
    }

    private static bool SupportsCompute()
    {
        if (s_testHooks.Value?.ForceComputeFallback == true)
            return false;

        IGraphicsContext? gfx = GraphicsContextFactory.SharedContext;
        return gfx is { Supports3DRendering: true };
    }

    // Bakes an operation into a freshly acquired pooled buffer sized to its bounds at density w, so a geometry /
    // compute / split pass can sample it as a texture. Counts one FullFrameMaterializations (C8). Returns null when
    // the pool cannot allocate (the caller applies the C7 drop/throw); an empty-size input is handled by the caller.
    private static RenderTarget? MaterializeInput(
        RenderNodeOperation op, float w, float maxWorkingScale, PipelineDiagnostics? diagnostics,
        RenderTargetPool? pool, RenderIntent renderIntent, RenderPullPurpose pullPurpose)
    {
        (int bw, int bh) = RenderNodeContext.DeviceBufferSize(op.Bounds, w);
        RenderTarget? target = RenderTargetPool.Acquire(pool, bw, bh, diagnostics);
        if (target == null)
            return null;

        try
        {
            BakeSource(target, w, op.Bounds, op, maxWorkingScale, renderIntent, pullPurpose, paint: null);
        }
        catch
        {
            Exception? cleanupFailure = null;
            CaptureDisposeFailure(target, ref cleanupFailure);
            LogCleanupFailure(cleanupFailure, "input materialization failure cleanup");
            throw;
        }

        if (diagnostics != null)
            diagnostics.FullFrameMaterializations++;
        return target;
    }

    private static RenderNodeOperation? ExecuteGeometry(
        GeometryPass pass, int width, int height, float w, Rect outBounds, RenderNodeOperation op,
        float outputScale, float workingScale, float maxWorkingScale, int maxDimension,
        PipelineDiagnostics? diagnostics, RenderTargetPool? pool, RenderIntent renderIntent,
        RenderPullPurpose pullPurpose)
    {
        float inW = RenderNodeContext.ClampWorkingScaleToBufferBudget(
            op.Bounds, CarriedWorkingScale(op, workingScale), maxDimension);
        (int inBw, int inBh) = RenderNodeContext.DeviceBufferSize(op.Bounds, inW);
        return ExecuteSessionPass(
            width, height, w, outBounds, op, outputScale, inW, inBw, inBh, maxWorkingScale,
            diagnostics, pool, renderIntent, pullPurpose, pass.Render, pass.RequiresReadback,
            CaptureGeometryInputDisposeFailure,
            $"Geometry input materialization failed ({inBw}x{inBh} px).",
            $"Geometry output allocation failed ({width}x{height} px, w {w}, bounds {outBounds}).",
            "geometry");
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
        float maxWorkingScale, PipelineDiagnostics? diagnostics, RenderTargetPool? pool,
        RenderIntent renderIntent, RenderPullPurpose pullPurpose)
    {
        (int tw, int th) = RenderNodeContext.DeviceBufferSize(tight, w);
        if (tw <= 0 || th <= 0)
        {
            // A degenerate (empty) shrink yields nothing, matching DiscardOutput and the §C3 empty-output drop.
            Exception? emptyCleanupFailure = null;
            CaptureGeometryOutputDisposeFailure(outputTarget, ref emptyCleanupFailure);
            CaptureDisposeFailure(op, ref emptyCleanupFailure);
            if (emptyCleanupFailure is { } failure)
                ExceptionDispatchInfo.Capture(failure).Throw();

            return null;
        }

        RenderTarget? tightTarget = RenderTargetPool.Acquire(pool, tw, th, diagnostics);
        if (tightTarget == null)
        {
            Exception? allocationCleanupFailure = null;
            CaptureGeometryOutputDisposeFailure(outputTarget, ref allocationCleanupFailure);
            LogCleanupFailure(allocationCleanupFailure, "geometry shrink allocation-failure cleanup");
            return DropOrThrow(op, renderIntent,
                $"Geometry shrink output allocation failed ({tw}x{th} px, w {w}, bounds {tight}).");
        }

        try
        {
            using var canvas = new ImmediateCanvas(
                tightTarget, renderIntent, w, maxWorkingScale, logicalSize: tight.Size,
                pullPurpose: pullPurpose);
            canvas.Clear();
            using (canvas.PushDeviceSpace())
            {
                if (s_testHooks.Value?.GeometryShrinkDrawFailure is { } injected)
                {
                    s_testHooks.Value.GeometryShrinkDrawFailure = null;
                    throw injected;
                }

                // The full output holds the pass content with outBounds.Position at device origin; shift it left/up by
                // the sub-rect offset so the tight region lands at the tighter target's origin (the legacy blit).
                canvas.DrawRenderTarget(
                    outputTarget, new Point((outBounds.X - tight.X) * w, (outBounds.Y - tight.Y) * w));
            }
        }
        catch
        {
            Exception? drawCleanupFailure = null;
            CaptureDisposeFailure(tightTarget, ref drawCleanupFailure);
            CaptureGeometryOutputDisposeFailure(outputTarget, ref drawCleanupFailure);
            CaptureDisposeFailure(op, ref drawCleanupFailure);
            LogCleanupFailure(drawCleanupFailure, "geometry shrink draw failure cleanup");
            throw;
        }

        Exception? successCleanupFailure = null;
        CaptureGeometryOutputDisposeFailure(outputTarget, ref successCleanupFailure);
        CaptureDisposeFailure(op, ref successCleanupFailure);
        if (successCleanupFailure is { } cleanupFailure)
        {
            CaptureDisposeFailure(tightTarget, ref successCleanupFailure);
            ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
        }

        if (diagnostics != null)
            diagnostics.GpuPasses++;
        try
        {
            return RenderNodeOperation.CreateFromRenderTarget(
                tight, tight.Position, tightTarget, EffectiveScale.At(w));
        }
        catch
        {
            Exception? creationCleanupFailure = null;
            CaptureDisposeFailure(tightTarget, ref creationCleanupFailure);
            LogCleanupFailure(creationCleanupFailure, "geometry shrink operation-creation failure cleanup");
            throw;
        }
    }

    private static RenderNodeOperation? ExecuteCompute(
        ComputePass pass, int width, int height, float w, Rect outBounds, RenderNodeOperation op,
        float outputScale, float workingScale, float maxWorkingScale, int maxDimension,
        PipelineDiagnostics? diagnostics, RenderTargetPool? pool, RenderIntent renderIntent,
        RenderPullPurpose pullPurpose, out bool wroteVulkanOutput)
    {
        wroteVulkanOutput = false;
        IGraphicsContext? gfx = GraphicsContextFactory.SharedContext;
        if (s_testHooks.Value?.ForceComputeFallback == true || gfx is not { Supports3DRendering: true })
        {
            // Identity/Skip already returned in MapOneOperation; only CpuCallback reaches here without Vulkan.
            return ExecuteComputeCpuFallback(
                pass, width, height, w, outBounds, op, outputScale, workingScale, maxWorkingScale, maxDimension,
                diagnostics, pool, renderIntent, pullPurpose);
        }

        // Start the source bake from the pass-resolved destination density, not the raw boundary workingScale, then
        // clamp it independently for the source bounds. IComputeContext exposes the resulting SourceScale/SourceBounds
        // separately from WorkingScale/TargetBounds, so a kernel can bridge the grids exactly if this local source
        // budget clamp lowers the input further. Resource resolution already carries dynamic-predecessor density where
        // C3.2 requires it; this clamp is only the per-input FR-037(b) allocation guard.
        float inW = RenderNodeContext.ClampWorkingScaleToBufferBudget(op.Bounds, w, maxDimension);
        (int inBw, int inBh) = RenderNodeContext.DeviceBufferSize(op.Bounds, inW);
        if (inBw <= 0 || inBh <= 0)
            return op;

        // A bake throw must release op's already-detached pooled lease (C7); see ExecuteGeometry for the same guard.
        RenderTarget? inputTarget;
        try
        {
            inputTarget = MaterializeInput(
                op, inW, maxWorkingScale, diagnostics, pool, renderIntent, pullPurpose);
        }
        catch
        {
            Exception? cleanupFailure = null;
            CaptureDisposeFailure(op, ref cleanupFailure);
            LogCleanupFailure(cleanupFailure, "compute materialization failure cleanup");
            throw;
        }

        if (inputTarget == null)
            return DropOrThrow(op, renderIntent, $"Compute input materialization failed ({inBw}x{inBh} px).");

        // A layout-transition/context-loss throw here happens after op was detached from the working set and before
        // any cleanup scope owns the materialized input, so both must be released on the way out (C7).
        ITexture2D? sourceTexture;
        try
        {
            sourceTexture = inputTarget.Texture;
            if (sourceTexture == null)
            {
                // A raster backing has no Skia -> Vulkan transition to perform or count. Keep the source op as
                // identity without touching the compute output allocation path.
                inputTarget.Dispose();
                return op;
            }

            if (s_testHooks.Value?.ComputePrepareFailure is { } injected)
                throw injected;
            inputTarget.PrepareForSampling();
            if (diagnostics != null)
                diagnostics.FlushSyncs++;
        }
        catch
        {
            Exception? cleanupFailure = null;
            CaptureComputeInputDisposeFailure(inputTarget, ref cleanupFailure);
            CaptureDisposeFailure(op, ref cleanupFailure);
            LogCleanupFailure(cleanupFailure, "compute sampling-preparation failure cleanup");
            throw;
        }
        RenderTarget? outputTarget = RenderTargetPool.Acquire(pool, width, height, diagnostics);
        if (outputTarget == null)
        {
            Exception? cleanupFailure = null;
            CaptureComputeInputDisposeFailure(inputTarget, ref cleanupFailure);
            LogCleanupFailure(cleanupFailure, "compute output-allocation cleanup");
            return DropOrThrow(op, renderIntent,
                $"Compute output allocation failed ({width}x{height} px, w {w}, bounds {outBounds}).");
        }

        ITexture2D? destTexture = outputTarget.Texture;
        if (destTexture == null)
        {
            Exception? cleanupFailure = null;
            CaptureDisposeFailure(outputTarget, ref cleanupFailure);
            CaptureComputeInputDisposeFailure(inputTarget, ref cleanupFailure);
            LogCleanupFailure(cleanupFailure, "texture-less compute output cleanup");
            return DropOrThrow(op, renderIntent, $"Pooled compute output has no Vulkan texture ({width}x{height} px).");
        }

        var scratch = new List<RenderTarget>();
        try
        {
            if (s_testHooks.Value?.ComputeOutputPrepareFailure is { } injected)
                throw injected;
            outputTarget.PrepareForComputeWrite();
        }
        catch
        {
            Exception? cleanupFailure = null;
            CaptureDisposeFailure(outputTarget, ref cleanupFailure);
            CaptureComputeInputDisposeFailure(inputTarget, ref cleanupFailure);
            CaptureDisposeFailure(op, ref cleanupFailure);
            LogCleanupFailure(cleanupFailure, "compute write-preparation failure cleanup");
            throw;
        }

        var ctx = new ComputeContext(
            gfx, sourceTexture, destTexture, width, height, op.Bounds, outBounds, inW, w,
            pass.PassCount, pass.ColorScratchCount, scratch, diagnostics, pool);
        try
        {
            pass.Dispatch(ctx);
            ctx.ValidateCompletion();
        }
        catch (ComputeScratchAllocationException ex)
        {
            // A ping-pong scratch acquire failed mid-dispatch: normalize like every other pass kind (C7,
            // review M1) — preview drops and continues, delivery throws — instead of aborting preview by rethrowing.
            Exception? cleanupFailure = null;
            ReleaseComputeScratch(scratch, ref cleanupFailure);
            CaptureDisposeFailure(outputTarget, ref cleanupFailure);
            CaptureComputeInputDisposeFailure(inputTarget, ref cleanupFailure);
            LogCleanupFailure(cleanupFailure, "compute scratch-allocation cleanup");
            return DropOrThrow(op, renderIntent, ex.Message);
        }
        catch (Exception ex) when (
            pass.DispatchFailureBehavior == ComputeDispatchFailureBehavior.IdentityInPreview
            && ex is not OperationCanceledException
            && ex is not ComputeContractViolationException
            && !ComputeBackendPreparationFailure.IsMarked(ex)
            && renderIntent == RenderIntent.Preview)
        {
            // PixelSort's historic preview behavior: a transient dispatch failure keeps the source pixels when the
            // descriptor explicitly declares the dispatch policy. Delivery still throws rather than exporting an
            // unsorted frame; cancellation, resource-plan violations, and backend preparation failures always
            // propagate; allocation failures remain governed by C7's preview-drop contract.
            Exception? cleanupFailure = null;
            ReleaseComputeScratch(scratch, ref cleanupFailure);
            CaptureDisposeFailure(outputTarget, ref cleanupFailure);
            CaptureComputeInputDisposeFailure(inputTarget, ref cleanupFailure);
            s_logger.LogWarning(ex,
                "Compute dispatch failed. Preview keeps the source because the pass declares IdentityInPreview.");
            if (cleanupFailure is { } firstCleanupFailure)
            {
                CaptureDisposeFailure(op, ref cleanupFailure);
                ExceptionDispatchInfo.Capture(firstCleanupFailure).Throw();
            }

            return op;
        }
        catch
        {
            Exception? cleanupFailure = null;
            ReleaseComputeScratch(scratch, ref cleanupFailure);
            CaptureDisposeFailure(outputTarget, ref cleanupFailure);
            CaptureDisposeFailure(inputTarget, ref cleanupFailure);
            CaptureDisposeFailure(op, ref cleanupFailure);
            LogCleanupFailure(cleanupFailure, "compute failure cleanup");
            throw;
        }

        Exception? successCleanupFailure = null;
        ReleaseComputeScratch(scratch, ref successCleanupFailure);
        CaptureComputeInputDisposeFailure(inputTarget, ref successCleanupFailure);
        CaptureDisposeFailure(op, ref successCleanupFailure);
        if (successCleanupFailure is { } computeCleanupFailure)
        {
            CaptureDisposeFailure(outputTarget, ref successCleanupFailure);
            ExceptionDispatchInfo.Capture(computeCleanupFailure).Throw();
        }

        RenderNodeOperation result = RenderNodeOperation.CreateFromRenderTarget(
            outBounds, outBounds.Position, outputTarget, EffectiveScale.At(w));
        wroteVulkanOutput = true;
        return result;
    }

    private static RenderNodeOperation? ExecuteComputeCpuFallback(
        ComputePass pass, int width, int height, float w, Rect outBounds, RenderNodeOperation op,
        float outputScale, float workingScale, float maxWorkingScale, int maxDimension,
        PipelineDiagnostics? diagnostics, RenderTargetPool? pool, RenderIntent renderIntent,
        RenderPullPurpose pullPurpose)
    {
        if (pass.Fallback.CpuCallback is not { } cpu)
            return op;

        // Same pass-resolved-w basis as the Vulkan path above: the callback's GeometrySession is sized at w, so the
        // baked input grid must match it (the EffectInput still carries inW for callbacks that bridge densities).
        float inW = RenderNodeContext.ClampWorkingScaleToBufferBudget(op.Bounds, w, maxDimension);
        (int inBw, int inBh) = RenderNodeContext.DeviceBufferSize(op.Bounds, inW);
        return ExecuteSessionPass(
            width, height, w, outBounds, op, outputScale, inW, inBw, inBh, maxWorkingScale,
            diagnostics, pool, renderIntent, pullPurpose, cpu, pass.Fallback.RequiresReadback,
            CaptureComputeInputDisposeFailure,
            "Compute CPU-fallback input materialization failed.",
            "Compute CPU-fallback output allocation failed.",
            "compute CPU-fallback");
    }

    private delegate void InputDisposeCapture(RenderTarget inputTarget, ref Exception? cleanupFailure);

    private static RenderNodeOperation? ExecuteSessionPass(
        int width, int height, float w, Rect outBounds, RenderNodeOperation op,
        float outputScale, float inW, int inBw, int inBh, float maxWorkingScale,
        PipelineDiagnostics? diagnostics, RenderTargetPool? pool, RenderIntent renderIntent,
        RenderPullPurpose pullPurpose,
        Action<GeometrySession> render, bool requiresReadback, InputDisposeCapture captureInputDispose,
        string inputAllocationFailure, string outputAllocationFailure, string logPrefix)
    {
        if (inBw <= 0 || inBh <= 0)
            return op;

        // MaterializeInput bakes op via source.Render (C7): a throw there must still release op's pooled lease, since
        // MapDescriptorPass already detached op from the working set and neither disposal sweep would otherwise reach it.
        RenderTarget? inputTarget;
        try
        {
            inputTarget = MaterializeInput(
                op, inW, maxWorkingScale, diagnostics, pool, renderIntent, pullPurpose);
        }
        catch
        {
            Exception? cleanupFailure = null;
            CaptureDisposeFailure(op, ref cleanupFailure);
            LogCleanupFailure(cleanupFailure, $"{logPrefix} materialization failure cleanup");
            throw;
        }

        if (inputTarget == null)
            return DropOrThrow(op, renderIntent, inputAllocationFailure);

        RenderTarget? outputTarget = RenderTargetPool.Acquire(pool, width, height, diagnostics);
        if (outputTarget == null)
        {
            Exception? cleanupFailure = null;
            captureInputDispose(inputTarget, ref cleanupFailure);
            LogCleanupFailure(cleanupFailure, $"{logPrefix} output-allocation cleanup");
            return DropOrThrow(op, renderIntent, outputAllocationFailure);
        }

        bool discarded;
        Rect? shrunk;
        try
        {
            if (requiresReadback)
            {
                inputTarget.PrepareForSampling();
                if (diagnostics != null)
                    diagnostics.FlushSyncs++;
            }

            var input = new EffectInput(
                inputTarget, op.Bounds, EffectiveScale.At(inW), readbackPrepared: requiresReadback);
            using var canvas = new ImmediateCanvas(
                outputTarget, renderIntent, w, maxWorkingScale, logicalSize: outBounds.Size,
                pullPurpose: pullPurpose);
            canvas.Clear();
            var session = new GeometrySession(canvas, [input], outBounds, outputScale, w, maxWorkingScale, diagnostics);
            render(session);
            discarded = session.IsOutputDiscarded;
            shrunk = session.ShrunkOutputBounds;
        }
        catch
        {
            Exception? cleanupFailure = null;
            CaptureDisposeFailure(outputTarget, ref cleanupFailure);
            captureInputDispose(inputTarget, ref cleanupFailure);
            CaptureDisposeFailure(op, ref cleanupFailure);
            LogCleanupFailure(cleanupFailure, $"{logPrefix} failure cleanup");
            throw;
        }

        Exception? inputCleanupFailure = null;
        captureInputDispose(inputTarget, ref inputCleanupFailure);
        if (inputCleanupFailure is { } inputFailure)
        {
            CaptureDisposeFailure(outputTarget, ref inputCleanupFailure);
            CaptureDisposeFailure(op, ref inputCleanupFailure);
            ExceptionDispatchInfo.Capture(inputFailure).Throw();
        }
        // DiscardOutput supersedes a requested shrink (§C3): a dropped pass produces nothing regardless of order.
        if (discarded)
        {
            Exception? cleanupFailure = null;
            CaptureGeometryOutputDisposeFailure(outputTarget, ref cleanupFailure);
            CaptureDisposeFailure(op, ref cleanupFailure);
            if (cleanupFailure != null)
                ExceptionDispatchInfo.Capture(cleanupFailure).Throw();

            return null;
        }

        if (shrunk is { } tight)
            return EmitShrunkGeometry(
                tight, w, outBounds, outputTarget, op, maxWorkingScale, diagnostics, pool,
                renderIntent, pullPurpose);

        Exception? operationCleanupFailure = null;
        CaptureDisposeFailure(op, ref operationCleanupFailure);
        if (operationCleanupFailure is { } operationFailure)
        {
            CaptureDisposeFailure(outputTarget, ref operationCleanupFailure);
            ExceptionDispatchInfo.Capture(operationFailure).Throw();
        }
        if (diagnostics != null)
            diagnostics.GpuPasses++;
        return RenderNodeOperation.CreateFromRenderTarget(
            outBounds, outBounds.Position, outputTarget, EffectiveScale.At(w));
    }

    private static void ReleaseComputeScratch(List<RenderTarget> scratch, ref Exception? cleanupFailure)
    {
        foreach (RenderTarget t in scratch)
            CaptureDisposeFailure(t, ref cleanupFailure);
    }

    private static void CaptureDisposeFailure(IDisposable? disposable, ref Exception? cleanupFailure)
    {
        try
        {
            disposable?.Dispose();
        }
        catch (Exception ex)
        {
            cleanupFailure ??= ex;
        }
    }

    internal static Exception? DisposeDisposablesCapturingFailure(IReadOnlyList<IDisposable> disposables)
    {
        Exception? cleanupFailure = null;
        for (int i = disposables.Count - 1; i >= 0; i--)
        {
            CaptureDisposeFailure(disposables[i], ref cleanupFailure);
        }

        return cleanupFailure;
    }

    private static void CaptureOperationDisposeFailures(
        ReadOnlySpan<BranchOperation> operations, ref Exception? cleanupFailure)
    {
        foreach (BranchOperation branch in operations)
        {
            CaptureDisposeFailure(branch.Op, ref cleanupFailure);
        }
    }

    private static void DisposeBranchOperations(ReadOnlySpan<BranchOperation> operations)
    {
        foreach (BranchOperation branch in operations)
        {
            try
            {
                branch.Op?.Dispose();
            }
            catch
            {
                // Best-effort: one faulting branch must not stop the remaining branch cleanup sweep.
            }
        }
    }

    private static void CaptureComputeInputDisposeFailure(RenderTarget inputTarget, ref Exception? cleanupFailure)
    {
        CaptureDisposeFailure(inputTarget, ref cleanupFailure);
        if (s_testHooks.Value?.ComputeInputDisposeFailure is { } inputDisposeFailure)
        {
            s_testHooks.Value.ComputeInputDisposeFailure = null;
            cleanupFailure ??= inputDisposeFailure;
        }
    }

    private static void CaptureGeometryInputDisposeFailure(RenderTarget inputTarget, ref Exception? cleanupFailure)
    {
        CaptureDisposeFailure(inputTarget, ref cleanupFailure);
        if (s_testHooks.Value?.GeometryInputDisposeFailure is { } inputDisposeFailure)
        {
            s_testHooks.Value.GeometryInputDisposeFailure = null;
            cleanupFailure ??= inputDisposeFailure;
        }
    }

    private static void CaptureGeometryOutputDisposeFailure(RenderTarget outputTarget, ref Exception? cleanupFailure)
    {
        CaptureDisposeFailure(outputTarget, ref cleanupFailure);
        if (s_testHooks.Value?.GeometryOutputDisposeFailure is { } outputDisposeFailure)
        {
            s_testHooks.Value.GeometryOutputDisposeFailure = null;
            cleanupFailure ??= outputDisposeFailure;
        }
    }

    private static void CaptureSplitBranchDisposeFailure(RenderTarget target, ref Exception? cleanupFailure)
    {
        CaptureDisposeFailure(target, ref cleanupFailure);
        if (s_testHooks.Value?.SplitBranchDisposeFailure is { } branchDisposeFailure)
        {
            s_testHooks.Value.SplitBranchDisposeFailure = null;
            cleanupFailure ??= branchDisposeFailure;
        }
    }

    private static void LogCleanupFailure(Exception? cleanupFailure, string operation)
    {
        if (cleanupFailure != null)
            s_logger.LogWarning(cleanupFailure, "A resource failed to dispose during {Operation}", operation);
    }

    // Fan-out: each current op is materialized once and split into the branches its callback emits (a static count
    // or, for dynamic outputs, an execution-time-resolved count the executor allocates, counts and releases).
    private static int ExecuteSplit(
        SplitPass pass, List<BranchOperation> current,
        float outputScale, float workingScale,
        float maxWorkingScale, int maxDimension, PipelineDiagnostics? diagnostics, RenderTargetPool? pool,
        RenderIntent renderIntent, RenderPullPurpose pullPurpose)
    {
        var outputs = new List<BranchOperation>(current.Count);
        int nextBranchOrdinal = 0;
        try
        {
            for (int i = 0; i < current.Count; i++)
            {
                RenderNodeOperation op = current[i].Op;
                int inputOrdinal = current[i].Ordinal;
                current[i] = default;
                int firstBranchOrdinal = pass.IsDynamicOutputs
                    ? nextBranchOrdinal
                    : ResolveBranchOrdinal(inputOrdinal, i) * pass.BranchCount;

                float inW = RenderNodeContext.ClampWorkingScaleToBufferBudget(
            op.Bounds, CarriedWorkingScale(op, workingScale), maxDimension);
                (int bw, int bh) = RenderNodeContext.DeviceBufferSize(op.Bounds, inW);
                if (bw <= 0 || bh <= 0)
                {
                    outputs.Add(new BranchOperation(op, firstBranchOrdinal));
                    if (pass.IsDynamicOutputs)
                        nextBranchOrdinal++;
                    continue;
                }

                // MaterializeInput bakes op via source.Render (C7): a throw there must still release op's pooled
                // lease, since the loop already detached op from current and neither disposal sweep would reach it
                // (the outer catch disposes only the branch outputs). Same guard as the three descriptor-path sites.
                RenderTarget? inputTarget;
                try
                {
                    inputTarget = MaterializeInput(
                        op, inW, maxWorkingScale, diagnostics, pool, renderIntent, pullPurpose);
                }
                catch
                {
                    Exception? cleanupFailure = null;
                    CaptureDisposeFailure(op, ref cleanupFailure);
                    LogCleanupFailure(cleanupFailure, "split materialization failure cleanup");
                    throw;
                }

                if (inputTarget == null)
                {
                    // Delivery throws inside DropOrThrow; preview drops this input's branches and continues.
                    DropOrThrow(op, renderIntent, $"Split input materialization failed ({bw}x{bh} px).");
                    continue;
                }

                Exception? renderFailure = null;
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
                        input, inW, outputScale, maxWorkingScale, maxDimension,
                        pass.IsDynamicOutputs ? null : pass.BranchCount, diagnostics, pool,
                        outputs, firstBranchOrdinal, renderIntent, pullPurpose);
                    pass.Render(emitter);
                    emitter.ValidateCompletion();
                    if (pass.IsDynamicOutputs)
                        nextBranchOrdinal += emitter.EmitCount;
                }
                catch (Exception ex)
                {
                    renderFailure = ex;
                    throw;
                }
                finally
                {
                    Exception? cleanupFailure = null;
                    CaptureDisposeFailure(inputTarget, ref cleanupFailure);
                    if (s_testHooks.Value?.SplitInputDisposeFailure is { } inputDisposeFailure)
                    {
                        s_testHooks.Value.SplitInputDisposeFailure = null;
                        cleanupFailure ??= inputDisposeFailure;
                    }
                    CaptureDisposeFailure(op, ref cleanupFailure);
                    if (cleanupFailure != null)
                    {
                        if (renderFailure != null)
                            LogCleanupFailure(cleanupFailure, "split failure cleanup");
                        else
                            ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
                    }
                }
            }
        }
        catch
        {
            DisposeBranchOperations(CollectionsMarshal.AsSpan(outputs));
            throw;
        }

        current.Clear();
        current.AddRange(outputs);
        return pass.IsDynamicOutputs ? nextBranchOrdinal : 0;
    }

    // Fan-in: composite the whole current branch set into one output under the blend mode, applying each branch's
    // per-input offset. Draws each branch once onto a single pooled target.
    private static void ExecuteComposite(
        CompositePass pass, List<BranchOperation> current,
        float outputScale, float workingScale,
        float maxWorkingScale, int maxDimension, PipelineDiagnostics? diagnostics, RenderTargetPool? pool,
        RenderIntent renderIntent, RenderPullPurpose pullPurpose)
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
            Point offset = ResolveCompositeOffset(pass, current[i].Ordinal, i);
            union = union.Union(current[i].Op.Bounds.Translate(offset));
        }

        float w = RenderNodeContext.ClampWorkingScaleToBufferBudget(union, workingScale, maxDimension);
        (int bw, int bh) = RenderNodeContext.DeviceBufferSize(union, w);
        if (bw <= 0 || bh <= 0)
        {
            DisposeBranchOperations(CollectionsMarshal.AsSpan(current));
            current.Clear();
            return;
        }

        // A paint color-filter runs after texture interpolation. The C9 fold is therefore equivalent to a standalone
        // branch pass only when each raster branch already has the composite's density (or is vector/unbounded). If a
        // concrete branch carries fewer pixels, execute the preserved color-filter pass at that carried density before
        // the composite resamples it. This keeps the fast folded path for the canonical vector SplitTree while
        // preserving filter-before-resample ordering for mixed-density fan-in.
        bool materializeFoldedFilters = pass.InputColorFilterFallback is { }
            && current.Any(branch => !branch.Op.EffectiveScale.IsUnbounded && branch.Op.EffectiveScale.Value != w);
        if (materializeFoldedFilters)
        {
            ExecuteCompositeColorFilterFallback(
                pass.InputColorFilterFallback!, current,
                outputScale, workingScale, maxWorkingScale,
                maxDimension, diagnostics, pool, renderIntent, pullPurpose);
            if (current.Count == 0)
                return;

            union = default;
            for (int i = 0; i < current.Count; i++)
            {
                Point offset = ResolveCompositeOffset(pass, current[i].Ordinal, i);
                union = union.Union(current[i].Op.Bounds.Translate(offset));
            }

            w = RenderNodeContext.ClampWorkingScaleToBufferBudget(union, workingScale, maxDimension);
            (bw, bh) = RenderNodeContext.DeviceBufferSize(union, w);
            if (bw <= 0 || bh <= 0)
            {
                DisposeBranchOperations(CollectionsMarshal.AsSpan(current));
                current.Clear();
                return;
            }
        }

        RenderTarget? target = RenderTargetPool.Acquire(pool, bw, bh, diagnostics);
        if (target == null)
        {
            Exception? allocationCleanupFailure = null;
            CaptureOperationDisposeFailures(CollectionsMarshal.AsSpan(current), ref allocationCleanupFailure);
            current.Clear();
            if (allocationCleanupFailure is { } failure)
                ExceptionDispatchInfo.Capture(failure).Throw();

            if (renderIntent == RenderIntent.Delivery)
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
            branchFilter = materializeFoldedFilters ? null : ComposeCompositeColorFilter(pass.InputColorFilters);
            using var canvas = new ImmediateCanvas(
                target, renderIntent, w, maxWorkingScale, logicalSize: union.Size,
                pullPurpose: pullPurpose);
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
                    RenderNodeOperation op = current[i].Op;
                    Point offset = ResolveCompositeOffset(pass, current[i].Ordinal, i);
                    using (canvas.PushTransform(Matrix.CreateTranslation(offset.X, offset.Y)))
                    {
                        if (pass.BlendMode == BlendMode.SrcOver)
                        {
                            if (branchFilter == null)
                            {
                                op.Render(canvas);
                            }
                            else
                            {
                                using (canvas.PushBlendMode(pass.BlendMode, branchFilter, op.Bounds))
                                    op.Render(canvas);
                            }
                        }
                        else
                        {
                            if (diagnostics != null)
                                diagnostics.CompositeLayerSaves++;
                            using (canvas.PushBlendMode(pass.BlendMode, branchFilter))
                                op.Render(canvas);
                        }
                    }
                }
            }
        }
        catch
        {
            Exception? cleanupFailure = null;
            CaptureDisposeFailure(branchFilter, ref cleanupFailure);
            CaptureDisposeFailure(target, ref cleanupFailure);
            DisposeBranchOperations(CollectionsMarshal.AsSpan(current));
            current.Clear();
            LogCleanupFailure(cleanupFailure, "composite failure cleanup");
            throw;
        }

        Exception? filterCleanupFailure = null;
        CaptureDisposeFailure(branchFilter, ref filterCleanupFailure);
        if (s_testHooks.Value?.CompositeFilterDisposeFailure is { } filterDisposeFailure)
        {
            s_testHooks.Value.CompositeFilterDisposeFailure = null;
            filterCleanupFailure ??= filterDisposeFailure;
        }

        if (filterCleanupFailure is { } compositeCleanupFailure)
        {
            CaptureDisposeFailure(target, ref filterCleanupFailure);
            DisposeBranchOperations(CollectionsMarshal.AsSpan(current));
            current.Clear();
            ExceptionDispatchInfo.Capture(compositeCleanupFailure).Throw();
        }

        Exception? branchCleanupFailure = null;
        CaptureOperationDisposeFailures(CollectionsMarshal.AsSpan(current), ref branchCleanupFailure);
        current.Clear();
        if (branchCleanupFailure is { } branchFailure)
        {
            CaptureDisposeFailure(target, ref branchCleanupFailure);
            ExceptionDispatchInfo.Capture(branchFailure).Throw();
        }

        if (diagnostics != null)
            diagnostics.GpuPasses++;
        current.Add(new BranchOperation(
            RenderNodeOperation.CreateFromRenderTarget(union, union.Position, target, EffectiveScale.At(w)),
            NoBranchOrdinal));
    }

    private static Point ResolveCompositeOffset(CompositePass pass, int branchOrdinal, int liveIndex)
    {
        int offsetIndex = ResolveBranchOrdinal(branchOrdinal, liveIndex);
        return offsetIndex < pass.InputOffsets.Length ? pass.InputOffsets[offsetIndex] : default;
    }

    private static int ResolveBranchOrdinal(int branchOrdinal, int liveIndex)
        => branchOrdinal == NoBranchOrdinal ? liveIndex : branchOrdinal;

    private static void ExecuteCompositeColorFilterFallback(
        FusedShaderPass fallback, List<BranchOperation> current,
        float outputScale, float workingScale,
        float maxWorkingScale, int maxDimension, PipelineDiagnostics? diagnostics, RenderTargetPool? pool,
        RenderIntent renderIntent, RenderPullPurpose pullPurpose)
    {
        bool linear = current.Count == 1;
        Rect expectedInput = linear ? current[0].Op.Bounds : Rect.Invalid;
        float resolvedScale = linear
            ? RenderNodeContext.ClampWorkingScaleToBufferBudget(
                expectedInput, CarriedWorkingScale(current[0].Op, workingScale), maxDimension)
            : workingScale;
        (int width, int height) = linear
            ? RenderNodeContext.DeviceBufferSize(expectedInput, resolvedScale)
            : default;
        var resolution = new PassResolution(expectedInput, width, height, resolvedScale, SkipEmpty: false);
        MapDescriptorPass(
            fallback, resolution, expectedInput, current,
            outputScale, workingScale, maxWorkingScale,
            maxDimension, diagnostics, pool, captureSink: null,
            renderIntent: renderIntent, pullPurpose: pullPurpose);
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
                    catch (Exception primaryFailure)
                    {
                        Exception? cleanupFailure = null;
                        CaptureDisposeFailure(filter, ref cleanupFailure);
                        LogCleanupFailure(cleanupFailure, "composite color-filter creation cleanup");
                        ExceptionDispatchInfo.Capture(primaryFailure).Throw();
                        throw;
                    }

                    s_testHooks.Value?.CompositeFoldCreated?.Invoke(next);
                    Exception? stageCleanupFailure = null;
                    CaptureDisposeFailure(composed, ref stageCleanupFailure);
                    CaptureDisposeFailure(filter, ref stageCleanupFailure);
                    if (s_testHooks.Value?.CompositeFoldStageDisposeFailure is { } injected)
                    {
                        s_testHooks.Value.CompositeFoldStageDisposeFailure = null;
                        stageCleanupFailure ??= injected;
                    }

                    if (stageCleanupFailure is { } failure)
                    {
                        CaptureDisposeFailure(next, ref stageCleanupFailure);
                        composed = null;
                        ExceptionDispatchInfo.Capture(failure).Throw();
                    }

                    composed = next;
                }
            }
        }
        catch (Exception primaryFailure)
        {
            // A mid-loop factory or cleanup throw leaves the partially composed accumulator owned by no one.
            Exception? cleanupFailure = null;
            CaptureDisposeFailure(composed, ref cleanupFailure);
            composed = null;
            LogCleanupFailure(cleanupFailure, "composite color-filter failure cleanup");
            ExceptionDispatchInfo.Capture(primaryFailure).Throw();
            throw;
        }

        return composed;
    }

    // A compute pass's ping-pong scratch acquire failed. Distinct from a genuine dispatch bug so ExecuteCompute
    // can route it through the uniform C7 drop/throw (preview drops, delivery throws) rather than aborting preview.
    internal sealed class ComputeScratchAllocationException : Exception
    {
        public ComputeScratchAllocationException(string message) : base(message) { }

        public ComputeScratchAllocationException(string message, Exception inner) : base(message, inner) { }
    }

    // A callback violated its compiled dispatch/resource contract. This is an authoring error, never a recoverable
    // preview dispatch failure, so it bypasses ComputeDispatchFailureBehavior.IdentityInPreview.
    private sealed class ComputeContractViolationException(string message) : InvalidOperationException(message);

    // The executor-owned resources handed to a compute node's dispatch callback: the materialized source and the
    // pass output texture, plus pooled color scratch released when the pass ends.
    private sealed class ComputeContext(
        IGraphicsContext gfx, ITexture2D source, ITexture2D destination, int width, int height,
        Rect sourceBounds, Rect targetBounds, float sourceScale, float workingScale,
        int passCount, int colorScratchLimit, List<RenderTarget> colorScratch, PipelineDiagnostics? diagnostics,
        RenderTargetPool? pool) : IComputeContext
    {
        private int _completedDispatches;
        private bool _copiedSource;

        public ITexture2D Source => source;

        public ITexture2D Destination => destination;

        public int Width => width;

        public int Height => height;

        public Rect SourceBounds => sourceBounds;

        public Rect TargetBounds => targetBounds;

        public float SourceScale => sourceScale;

        public float WorkingScale => workingScale;

        public ITexture2D AcquireColorScratch()
        {
            ThrowIfIdentityCompleted();
            if (colorScratch.Count >= colorScratchLimit)
            {
                throw new ComputeContractViolationException(
                    $"The compute callback exceeded its declared color scratch limit ({colorScratchLimit}).");
            }

            RenderTarget target = RenderTargetPool.Acquire(pool, width, height, diagnostics)
                ?? throw new ComputeScratchAllocationException(
                    $"Compute ping-pong scratch allocation failed ({width}x{height} px).");
            colorScratch.Add(target);
            // A reused Skia surface may still carry queued work from its previous lease. Submit it before Vulkan
            // writes the backing image, otherwise a later Skia flush can overwrite the compute result.
            ComputeBackendPreparationFailure.Run(target.PrepareForComputeWrite);
            return target.Texture
                ?? throw new ComputeScratchAllocationException("Pooled compute scratch has no Vulkan texture.");
        }

        public void CopySourceToDestination()
        {
            if (_copiedSource || _completedDispatches != 0)
            {
                throw new ComputeContractViolationException(
                    "CopySourceToDestination is an exclusive terminal operation and cannot be combined with dispatches.");
            }

            ComputeBackendPreparationFailure.Run(() =>
            {
                if (s_testHooks.Value?.ComputeCopyFailure is { } injected)
                {
                    s_testHooks.Value.ComputeCopyFailure = null;
                    throw injected;
                }

                gfx.CopyTexture(source, destination);
                if (s_testHooks.Value?.ComputeCopyPrepareFailure is { } prepareFailure)
                {
                    s_testHooks.Value.ComputeCopyPrepareFailure = null;
                    throw prepareFailure;
                }

                destination.PrepareForSampling();
            });
            _copiedSource = true;
        }

        public void Run<T>(GLSLShader shader, ITexture2D src, ITexture2D dst, T pushConstants)
            where T : unmanaged
        {
            BeforeRun();
            shader.ExecuteSingleTarget(src, dst, pushConstants);
            _completedDispatches++;
            if (diagnostics != null)
                diagnostics.GpuPasses++;
        }

        public void Run<T>(
            GLSLShader shader, ITexture2D src, ITexture2D mask, ITexture2D dst, T pushConstants)
            where T : unmanaged
        {
            BeforeRun();
            shader.ExecuteSingleTargetWithMask(src, mask, dst, pushConstants);
            _completedDispatches++;
            if (diagnostics != null)
                diagnostics.GpuPasses++;
        }

        public void ValidateCompletion()
        {
            if (!_copiedSource && _completedDispatches != passCount)
            {
                throw new ComputeContractViolationException(
                    $"The compute callback completed {_completedDispatches} dispatches but declared {passCount}.");
            }
        }

        private void BeforeRun()
        {
            ThrowIfIdentityCompleted();
            if (_completedDispatches >= passCount)
            {
                throw new ComputeContractViolationException(
                    $"The compute callback exceeded its declared dispatch count ({passCount}).");
            }
        }

        private void ThrowIfIdentityCompleted()
        {
            if (_copiedSource)
            {
                throw new ComputeContractViolationException(
                    "CopySourceToDestination is terminal; no scratch acquisition or dispatch may follow it.");
            }
        }
    }

    // The fan-out sink a split callback drives. Each Emit allocates one pooled branch target, opens a bracketed
    // session over it, runs the branch draw, and appends the branch op — counting one GpuPasses per branch and
    // applying the C7 drop/throw on a failed allocation.
    private sealed class SplitEmitter(
        EffectInput input, float workingScale, float outputScale, float maxWorkingScale, int maxDimension,
        int? declaredBranchCount, PipelineDiagnostics? diagnostics, RenderTargetPool? pool,
        List<BranchOperation> outputs, int firstBranchOrdinal,
        RenderIntent renderIntent, RenderPullPurpose pullPurpose) : ISplitEmitter
    {
        private int _emitCount;

        public EffectInput Input => input;

        public float WorkingScale => workingScale;

        public int EmitCount => _emitCount;

        public void Emit(Rect logicalBounds, Action<GeometrySession> render)
        {
            ArgumentNullException.ThrowIfNull(render);
            if (declaredBranchCount is { } count && _emitCount >= count)
            {
                throw new InvalidOperationException(
                    $"The static split exceeded its declared branch count ({count}).");
            }

            int branchOrdinal = firstBranchOrdinal + _emitCount++;

            float w = RenderNodeContext.ClampWorkingScaleToBufferBudget(logicalBounds, workingScale, maxDimension);
            (int bw, int bh) = RenderNodeContext.DeviceBufferSize(logicalBounds, w);
            if (bw <= 0 || bh <= 0)
                return;

            RenderTarget? target = RenderTargetPool.Acquire(pool, bw, bh, diagnostics);
            if (target == null)
            {
                if (renderIntent == RenderIntent.Delivery)
                    throw new InvalidOperationException($"Split branch allocation failed ({bw}x{bh} px, w {w}).");

                s_logger.LogWarning("Split branch allocation failed ({Width}x{Height} px). Preview drops it.", bw, bh);
                return;
            }

            bool discarded;
            Rect? shrunk;
            try
            {
                using var canvas = new ImmediateCanvas(
                    target, renderIntent, w, maxWorkingScale, logicalSize: logicalBounds.Size,
                    pullPurpose: pullPurpose);
                canvas.Clear();
                var session = new GeometrySession(
                    canvas, [input], logicalBounds, outputScale, w, maxWorkingScale, diagnostics);
                render(session);
                discarded = session.IsOutputDiscarded;
                shrunk = session.ShrunkOutputBounds;
            }
            catch (Exception primaryFailure)
            {
                Exception? cleanupFailure = null;
                CaptureSplitBranchDisposeFailure(target, ref cleanupFailure);
                LogCleanupFailure(cleanupFailure, "split branch callback failure cleanup");
                ExceptionDispatchInfo.Capture(primaryFailure).Throw();
                throw;
            }

            // DiscardOutput supersedes a requested shrink (§C3), exactly as the single-op geometry path handles it.
            if (discarded)
            {
                Exception? cleanupFailure = null;
                CaptureSplitBranchDisposeFailure(target, ref cleanupFailure);
                if (cleanupFailure is { } failure)
                    ExceptionDispatchInfo.Capture(failure).Throw();

                return;
            }

            if (shrunk is { } tight)
            {
                EmitShrunk(tight, w, logicalBounds, target, branchOrdinal);
                return;
            }

            if (diagnostics != null)
                diagnostics.GpuPasses++;
            outputs.Add(new BranchOperation(
                RenderNodeOperation.CreateFromRenderTarget(
                    logicalBounds, logicalBounds.Position, target, EffectiveScale.At(w)),
                branchOrdinal));
        }

        public void ValidateCompletion()
        {
            if (declaredBranchCount is { } count && _emitCount != count)
            {
                throw new InvalidOperationException(
                    $"The static split emitted {_emitCount} branches but declared {count}.");
            }
        }

        // A split branch that called GeometrySession.SetOutputBounds tightens its emitted op to a sub-rect, mirroring
        // EmitShrunkGeometry: blit the sub-rect into a tighter pooled target and publish the tightened bounds. Unlike
        // the single-op geometry shrink, the branches' shared input scratch cannot be released first (later branches
        // still read it), so the tight lease transiently exceeds the declared bound by one — a dynamic split is
        // exempt from the static peak-live assert and a static split is covered by its split-shrink allowance
        // (AssertPeakLiveWithinPlan). The branch still counts one GpuPasses.
        private void EmitShrunk(
            Rect tight, float w, Rect logicalBounds, RenderTarget branchTarget, int branchOrdinal)
        {
            (int tw, int th) = RenderNodeContext.DeviceBufferSize(tight, w);
            if (tw <= 0 || th <= 0)
            {
                // A degenerate (empty) shrink yields nothing, matching DiscardOutput and the §C3 empty-output drop.
                Exception? emptyCleanupFailure = null;
                CaptureSplitBranchDisposeFailure(branchTarget, ref emptyCleanupFailure);
                if (emptyCleanupFailure is { } failure)
                    ExceptionDispatchInfo.Capture(failure).Throw();

                return;
            }

            RenderTarget? tightTarget = RenderTargetPool.Acquire(pool, tw, th, diagnostics);
            if (tightTarget == null)
            {
                Exception? allocationCleanupFailure = null;
                CaptureSplitBranchDisposeFailure(branchTarget, ref allocationCleanupFailure);
                LogCleanupFailure(allocationCleanupFailure, "split branch shrink allocation-failure cleanup");
                if (renderIntent == RenderIntent.Delivery)
                    throw new InvalidOperationException($"Split branch shrink allocation failed ({tw}x{th} px, w {w}).");

                s_logger.LogWarning(
                    "Split branch shrink allocation failed ({Width}x{Height} px). Preview drops it.", tw, th);
                return;
            }

            try
            {
                using var canvas = new ImmediateCanvas(
                    tightTarget, renderIntent, w, maxWorkingScale, logicalSize: tight.Size,
                    pullPurpose: pullPurpose);
                canvas.Clear();
                using (canvas.PushDeviceSpace())
                {
                    canvas.DrawRenderTarget(
                        branchTarget, new Point((logicalBounds.X - tight.X) * w, (logicalBounds.Y - tight.Y) * w));
                }
            }
            catch
            {
                Exception? drawCleanupFailure = null;
                CaptureDisposeFailure(tightTarget, ref drawCleanupFailure);
                CaptureSplitBranchDisposeFailure(branchTarget, ref drawCleanupFailure);
                LogCleanupFailure(drawCleanupFailure, "split branch shrink draw failure cleanup");
                throw;
            }

            Exception? branchCleanupFailure = null;
            CaptureSplitBranchDisposeFailure(branchTarget, ref branchCleanupFailure);
            if (branchCleanupFailure is { } cleanupFailure)
            {
                CaptureDisposeFailure(tightTarget, ref branchCleanupFailure);
                ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
            }

            if (diagnostics != null)
                diagnostics.GpuPasses++;
            RenderNodeOperation? output = null;
            try
            {
                output = RenderNodeOperation.CreateFromRenderTarget(
                    tight, tight.Position, tightTarget, EffectiveScale.At(w));
                outputs.Add(new BranchOperation(output, branchOrdinal));
            }
            catch
            {
                Exception? creationCleanupFailure = null;
                if (output != null)
                    CaptureDisposeFailure(output, ref creationCleanupFailure);
                else
                    CaptureDisposeFailure(tightTarget, ref creationCleanupFailure);
                LogCleanupFailure(creationCleanupFailure, "split branch shrink operation-creation failure cleanup");
                throw;
            }
        }
    }
}
