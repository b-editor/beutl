using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Logging;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;
using Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Gates the migrated node primitives (feature 004, T048-T050): the SplitTree counter derivation, FlushSyncs at
/// backend transitions on a compute chain, structural recompile on a split division-count change, dynamic-output
/// counting + pool-leak checks for PartsSplit, and the C7 allocation-failure normalization for the new pass kinds.
/// </summary>
[NonParallelizable]
[TestFixture]
public class PrimitivePassTests
{
    private static readonly Rect s_bounds = new(0, 0, 160, 120);

    [OneTimeSetUp]
    public void SeedLoggerFactory()
    {
        Log.LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(static _ => { });
    }

    // SplitTree = SplitEffect(3x3) -> Saturate (fused) -> LayerEffect. LayerEffect is a CompositeNode (fan-in), and
    // the coordinate-invariant Saturate run that sits between the split and the composite folds into the composite's
    // per-branch draw (C9, step-6 perf follow-up): the composite draws each of the 9 tiles once, so applying the
    // Saturate SKColorFilter on each tile draw is identical to baking each tile through Saturate and then
    // compositing, which eliminates the 9 intermediate Saturate targets and their 9 draws. Rendered pool-less
    // through the real pull path, so the counts are structure-determined. Derivation:
    //   Split:       input bake (+1 FullFrameMaterialization) + 9 tile draws (+9 GpuPasses)
    //   Saturate:    folded into the composite — no standalone pass, no per-tile target
    //   LayerEffect: one composite draw folding all 9 tiles (each with the Saturate filter) into one output
    //                (+1 GpuPasses; no bake, no sync)
    // Totals: GpuPasses = 10 (was 19 before the fold), FullFrameMaterializations = 1, FlushSyncs = 0 (all Skia — no
    // backend transition), one compile. GpuPasses/allocations only ever fall through a fold — never rise.
    [Test]
    public void SplitTree_CounterDerivation()
    {
        VulkanTestEnvironment.EnsureAvailable();
        PipelineDiagnosticsSnapshot c = VulkanTestEnvironment.InvokeOnRenderThread(
            () => RenderScene(SceneFixtures.SplitTree(SceneFixtures.ReferenceSize)));

        Assert.Multiple(() =>
        {
            Assert.That(c.PlanCompilations, Is.EqualTo(1), "one compile for the split/fused/composite plan");
            Assert.That(c.GpuPasses, Is.EqualTo(10), "9 tile draws + 1 composite fan-in with the folded Saturate (C9)");
            Assert.That(c.FullFrameMaterializations, Is.EqualTo(1), "only the split input bake — the composite draws directly");
            Assert.That(c.FlushSyncs, Is.EqualTo(0), "the whole plan is Skia; no backend transition");
        });
    }

    // A Skia->Vulkan->Skia chain (Saturate -> PixelSort -> Invert): FlushSyncs equals the number of backend
    // transitions in the schedule (C4.2) — one entering the compute pass, one leaving it. GpuPasses = 1 fused + 3
    // compute dispatches + 1 fused = 5.
    [Test]
    public void PixelSortChain_FlushSyncsEqualBackendTransitions()
    {
        var context = VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.RequireComputeCapable(context, "PixelSort");

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var diagnostics = new PipelineDiagnostics();
            using var pool = new RenderTargetPool();
            RenderNodeOperation[] outputs = RenderThroughPlan(
                [
                    new Saturate { Amount = { CurrentValue = 1.3f } },
                    new PixelSortEffect { ThresholdMin = { CurrentValue = 20f }, ThresholdMax = { CurrentValue = 80f } },
                    new Invert { Amount = { CurrentValue = 1f } },
                ],
                [Input()], float.PositiveInfinity, diagnostics, pool);
            RenderNodeOperation.DisposeAll(outputs);

            PipelineDiagnosticsSnapshot c = diagnostics.Snapshot();
            Assert.Multiple(() =>
            {
                Assert.That(c.FlushSyncs, Is.EqualTo(2), "one Skia->Vulkan and one Vulkan->Skia transition");
                Assert.That(c.GpuPasses, Is.EqualTo(5), "1 fused + 3 compute dispatches + 1 fused");
            });
        });
    }

    [Test]
    public void ConsecutiveComputePasses_CountMaterializationRoundTrips()
    {
        var context = VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.RequireComputeCapable(context, "consecutive compute synchronization");

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            static ComputeNodeDescriptor Copy(string token) => ComputeNodeDescriptor.Create(
                static ctx => ctx.CopySourceToDestination(), 1, ComputeFallback.Identity, structuralToken: token);

            var builder = new EffectGraphBuilder(s_bounds, outputScale: 1f, workingScale: 1f)
                .Compute(Copy("first"))
                .Compute(Copy("second"));
            using EffectGraph graph = builder.Build();
            var diagnostics = new PipelineDiagnostics();
            using var pool = new RenderTargetPool();
            CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics);
            FrameResources frame = EffectGraphCompiler.ResolveResources(plan, Rect.Invalid, workingScale: 1f);

            RenderNodeOperation[] outputs = PlanExecutor.Execute(
                plan, frame, [Input()], outputScale: 1f, workingScale: 1f,
                maxWorkingScale: float.PositiveInfinity, diagnostics, pool);
            RenderNodeOperation.DisposeAll(outputs);

            Assert.That(diagnostics.Snapshot().FlushSyncs, Is.EqualTo(3),
                "first Skia->Vulkan plus Vulkan->Skia->Vulkan before the second compute");
        });
    }

    // C3.6: the split division count is structural, so animating it recompiles exactly once per change while a
    // parameter-only frame (spacing) hits the cache.
    [Test]
    public void SplitEffect_DivisionCountAnimation_RecompilesPerChange()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var split = new SplitEffect
            {
                HorizontalDivisions = { CurrentValue = 2 },
                VerticalDivisions = { CurrentValue = 2 },
            };
            var resource = (FilterEffect.Resource)split.ToResource(CompositionContext.Default);
            using var node = new PlanFilterEffectRenderNode(resource);
            var diagnostics = new PipelineDiagnostics();
            using var pool = new RenderTargetPool();

            // frame 0: initial compile; 1: spacing-only (hit); 2: division change (recompile); 3: stable (hit).
            int[] divisions = [2, 2, 3, 3];
            float[] spacing = [0f, 6f, 6f, 6f];
            var compiles = new long[divisions.Length];
            for (int f = 0; f < divisions.Length; f++)
            {
                pool.Trim(f);
                split.HorizontalDivisions.CurrentValue = divisions[f];
                split.VerticalDivisions.CurrentValue = divisions[f];
                split.HorizontalSpacing.CurrentValue = spacing[f];
                bool updateOnly = false;
                resource.Update(split, CompositionContext.Default, ref updateOnly);
                node.Update(resource);

                diagnostics.Reset();
                var context = new RenderNodeContext([Input()]) { Diagnostics = diagnostics, Pool = pool };
                RenderNodeOperation.DisposeAll(node.Process(context));
                compiles[f] = diagnostics.Snapshot().PlanCompilations;
            }

            Assert.Multiple(() =>
            {
                Assert.That(compiles[0], Is.EqualTo(1), "frame 0 compiles the initial 2x2 structure");
                Assert.That(compiles[1], Is.EqualTo(0), "a spacing-only change hits the cache");
                Assert.That(compiles[2], Is.EqualTo(1), "the 2x2 -> 3x3 division change recompiles once");
                Assert.That(compiles[3], Is.EqualTo(0), "the stable 3x3 frame hits the cache");
            });
        });
    }

    [Test]
    public void SplitEffect_SubpixelTiles_UsesDynamicEmptyResourcePlan()
    {
        var bounds = new Rect(0, 0, 100, 100);
        var split = new SplitEffect
        {
            HorizontalDivisions = { CurrentValue = 1000 },
            VerticalDivisions = { CurrentValue = 1000 },
        };
        var builder = new EffectGraphBuilder(bounds, outputScale: 1f, workingScale: 1f);
        split.Describe(builder, (FilterEffect.Resource)split.ToResource(CompositionContext.Default));
        using EffectGraph graph = builder.Build();

        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
        var pass = (SplitPass)plan.Passes.Single();

        Assert.Multiple(() =>
        {
            Assert.That(pass.IsDynamicOutputs, Is.True,
                "sub-pixel tiles emit no branches and must not declare one million static outputs");
            Assert.That(pass.BranchCount, Is.Zero);
            Assert.That(plan.Resources.Intermediates, Is.Empty,
                "a dynamic empty split has no static resource declarations");
        });
    }

    [Test]
    public void SplitEffect_RenderableTiles_RemainsStatic()
    {
        var split = new SplitEffect
        {
            HorizontalDivisions = { CurrentValue = 2 },
            VerticalDivisions = { CurrentValue = 3 },
        };
        var builder = new EffectGraphBuilder(s_bounds, outputScale: 1f, workingScale: 1f);
        split.Describe(builder, (FilterEffect.Resource)split.ToResource(CompositionContext.Default));
        using EffectGraph graph = builder.Build();

        var pass = (SplitPass)EffectGraphCompiler.Compile(graph, diagnostics: null).Passes.Single();

        Assert.Multiple(() =>
        {
            Assert.That(pass.IsDynamicOutputs, Is.False);
            Assert.That(pass.BranchCount, Is.EqualTo(6),
                "renderable division counts remain structural for plan-cache invalidation");
        });
    }

    [Test]
    public void SplitEffect_AfterFanOut_UsesDynamicBranchContract()
    {
        var first = new SplitEffect
        {
            HorizontalDivisions = { CurrentValue = 2 },
            VerticalDivisions = { CurrentValue = 2 },
        };
        var second = new SplitEffect
        {
            HorizontalDivisions = { CurrentValue = 2 },
            VerticalDivisions = { CurrentValue = 2 },
        };
        var builder = new EffectGraphBuilder(s_bounds, outputScale: 1f, workingScale: 1f);
        using FilterEffect.Resource firstResource
            = (FilterEffect.Resource)first.ToResource(CompositionContext.Default);
        using FilterEffect.Resource secondResource
            = (FilterEffect.Resource)second.ToResource(CompositionContext.Default);
        first.Describe(builder, firstResource);
        second.Describe(builder, secondResource);
        using EffectGraph graph = builder.Build();

        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);

        Assert.Multiple(() =>
        {
            Assert.That(((SplitPass)plan.Passes[0]).IsDynamicOutputs, Is.False);
            Assert.That(((SplitPass)plan.Passes[1]).IsDynamicOutputs, Is.True,
                "the second split receives per-branch bounds and cannot promise its graph-level output count");
        });
    }

    [Test]
    public void SplitEffect_LargeRenderableGrid_UsesDynamicResourcePlan()
    {
        var split = new SplitEffect
        {
            HorizontalDivisions = { CurrentValue = 80 },
            VerticalDivisions = { CurrentValue = 60 },
        };
        var builder = new EffectGraphBuilder(s_bounds, outputScale: 1f, workingScale: 1f);
        split.Describe(builder, (FilterEffect.Resource)split.ToResource(CompositionContext.Default));
        using EffectGraph graph = builder.Build();

        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
        var pass = (SplitPass)plan.Passes.Single();

        Assert.Multiple(() =>
        {
            Assert.That(pass.IsDynamicOutputs, Is.True,
                "a 4,800-branch renderable grid must not create a proportional static resource plan");
            Assert.That(pass.BranchCount, Is.Zero);
            Assert.That(plan.Resources.Intermediates, Is.Empty);
        });
    }

    // PartsSplit's dynamic outputs (C3.5): the executor allocates one pooled target per discovered contour, counts
    // each acquire, and releases every branch within the frame — so a second identical frame reuses them all with
    // zero fresh allocations (the pool-leak assertion).
    [Test]
    public void PartsSplit_DynamicOutputs_CountedAndReleasedWithoutLeak()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var diagnostics = new PipelineDiagnostics();
            using var pool = new RenderTargetPool();

            pool.Trim(0);
            diagnostics.Reset();
            RenderNodeOperation[] frame0 = RenderThroughPlan(
                [new PartsSplitEffect()], [ContourInput()], float.PositiveInfinity, diagnostics, pool);
            int parts = frame0.Length;
            PipelineDiagnosticsSnapshot s0 = diagnostics.Snapshot();
            RenderNodeOperation.DisposeAll(frame0);

            pool.Trim(1);
            diagnostics.Reset();
            RenderNodeOperation[] frame1 = RenderThroughPlan(
                [new PartsSplitEffect()], [ContourInput()], float.PositiveInfinity, diagnostics, pool);
            PipelineDiagnosticsSnapshot s1 = diagnostics.Snapshot();
            RenderNodeOperation.DisposeAll(frame1);

            Assert.Multiple(() =>
            {
                Assert.That(parts, Is.GreaterThan(1), "the two disjoint blobs discover more than one part");
                Assert.That(s0.PoolAcquires, Is.EqualTo(parts + 1),
                    "one acquire per dynamic branch plus the input materialization");
                Assert.That(s1.TargetAllocations, Is.EqualTo(0),
                    "frame 1 reuses every branch buffer — proof frame 0's dynamic outputs were all released");
                Assert.That(s1.PoolMisses, Is.EqualTo(0), "no leaked branch forces a fresh allocation");
            });
        });
    }

    // FR-006 steady state on a compute-bearing chain: every intermediate — including the compute pass's ping-pong
    // color scratch AND its Depth32Float attachment — is pooled, so a structurally constant PixelSort chain
    // allocates only on frame 1; frames 2..K are all pool hits with zero fresh target creations.
    [Test]
    public void PixelSortChain_SteadyState_AllocatesNothingAfterWarmup()
    {
        var context = VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.RequireComputeCapable(context, "PixelSort");

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            const int frames = 4;
            var diagnostics = new PipelineDiagnostics();
            using var pool = new RenderTargetPool();
            var snapshots = new PipelineDiagnosticsSnapshot[frames];

            for (int f = 0; f < frames; f++)
            {
                pool.Trim(f);
                diagnostics.Reset();
                RenderNodeOperation[] outputs = RenderThroughPlan(
                    [
                        new Saturate { Amount = { CurrentValue = 1.3f } },
                        new PixelSortEffect { ThresholdMin = { CurrentValue = 20f }, ThresholdMax = { CurrentValue = 80f } },
                        new Invert { Amount = { CurrentValue = 1f } },
                    ],
                    [Input()], float.PositiveInfinity, diagnostics, pool);
                RenderNodeOperation.DisposeAll(outputs);
                snapshots[f] = diagnostics.Snapshot();
            }

            Assert.Multiple(() =>
            {
                // Frame 1 may already reuse intra-frame (a buffer released mid-frame satisfies a later same-size
                // acquire), so misses <= acquires; what matters is that every miss — incl. the depth attachment —
                // is a counted fresh creation.
                Assert.That(snapshots[0].PoolMisses, Is.GreaterThan(0), "frame 1 warms the pool");
                Assert.That(snapshots[0].TargetAllocations, Is.EqualTo(snapshots[0].PoolMisses),
                    "each frame-1 miss is one fresh target creation — the depth attachment is counted, not silent");

                for (int f = 1; f < frames; f++)
                {
                    Assert.That(snapshots[f].TargetAllocations, Is.EqualTo(0),
                        $"frame {f + 1} allocates no fresh targets (FR-006 steady state incl. depth)");
                    Assert.That(snapshots[f].PoolMisses, Is.EqualTo(0), $"frame {f + 1} has no pool misses");
                    Assert.That(snapshots[f].PoolAcquires, Is.EqualTo(snapshots[0].PoolAcquires),
                        $"frame {f + 1} re-acquires the same buffer set, now all hits");
                }
            });
        });
    }

    // C7 normalization for the new pass kinds under forced pool exhaustion (the pool test seam): preview drops the
    // pass output and continues; delivery throws with the parity message.
    [TestCase("Fused")]
    [TestCase("Geometry")]
    [TestCase("Compute")]
    [TestCase("Split")]
    [TestCase("Composite")]
    public void NewPass_AllocationFailure_PreviewDropsDeliveryThrows(string kind)
    {
        var context = VulkanTestEnvironment.EnsureAvailable();
        if (kind == "Compute")
            VulkanTestEnvironment.RequireComputeCapable(context, "Compute allocation-failure gate");

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            // Delivery (MaxWorkingScale == +Inf) must throw with the parity message.
            using (var deliveryPool = new RenderTargetPool())
            {
                deliveryPool.SetBackingFactoryForTest(static (_, _) => null);
                Assert.That(
                    () => RenderNodeOperation.DisposeAll(RenderThroughPlan(
                        [MakeEffect(kind)], [Input()], float.PositiveInfinity, new PipelineDiagnostics(), deliveryPool)),
                    Throws.TypeOf<InvalidOperationException>(),
                    "delivery render throws on allocation failure");
            }

            // Preview (finite MaxWorkingScale) drops the pass output and continues without throwing.
            using (var previewPool = new RenderTargetPool())
            {
                previewPool.SetBackingFactoryForTest(static (_, _) => null);
                RenderNodeOperation[] outputs = RenderThroughPlan(
                    [MakeEffect(kind)], [Input()], maxWorkingScale: 2f, new PipelineDiagnostics(), previewPool);
                Assert.That(outputs, Is.Empty, "preview drops the failed pass output");
                RenderNodeOperation.DisposeAll(outputs);
            }
        });
    }

    // CompositeNode has no built-in pilot, so cover the fan-in directly: a static split into two branches followed
    // by a composite collapses the branch set back into one blended output op.
    [Test]
    public void Composite_FansInSplitBranches()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var builder = new EffectGraphBuilder(s_bounds, outputScale: 1f, workingScale: 1f);
            builder.Split(SplitNodeDescriptor.Static(
                emitter =>
                {
                    EffectInput input = emitter.Input;
                    for (int i = 0; i < 2; i++)
                    {
                        emitter.Emit(input.Bounds, session =>
                        {
                            ImmediateCanvas canvas = session.OpenCanvas();
                            using (canvas.PushDeviceSpace())
                                input.Draw(canvas, default);
                        });
                    }
                },
                branchCount: 2,
                structuralToken: "test-split"));
            builder.Composite(CompositeNodeDescriptor.Create(BlendMode.SrcOver, structuralToken: "test-composite"));

            using EffectGraph graph = builder.Build();
            var diagnostics = new PipelineDiagnostics();
            using var pool = new RenderTargetPool();
            CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics);
            FrameResources frame = EffectGraphCompiler.ResolveResources(plan, Rect.Invalid, workingScale: 1f);
            RenderNodeOperation[] outputs = PlanExecutor.Execute(
                plan, frame, [Input()], outputScale: 1f, workingScale: 1f,
                maxWorkingScale: float.PositiveInfinity, diagnostics: diagnostics, pool: pool);

            int count = outputs.Length;
            RenderNodeOperation.DisposeAll(outputs);
            Assert.That(count, Is.EqualTo(1), "the composite fans the two split branches into one output");
        });
    }

    // C7 for compute at the OUTPUT acquire (input materialized OK, output allocation fails): must drop (preview) /
    // throw (delivery), NOT silently fall back to identity and export a no-op pass (review M1). The fail-after seam
    // lets the input materialize (acquire 1) succeed and fails the output acquire (2).
    [Test]
    public void Compute_OutputAllocationFailure_PreviewDropsDeliveryThrows()
    {
        var context = VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.RequireComputeCapable(context, "Compute output-failure gate");

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using (var deliveryPool = new RenderTargetPool())
            {
                deliveryPool.SetBackingFactoryFailingAfterForTest(1);
                Assert.That(
                    () => RenderNodeOperation.DisposeAll(RenderThroughPlan(
                        [new PixelSortEffect()], [Input()], float.PositiveInfinity, new PipelineDiagnostics(), deliveryPool)),
                    Throws.TypeOf<InvalidOperationException>(),
                    "delivery must throw on compute output allocation failure, not export a silent identity");
            }

            using (var previewPool = new RenderTargetPool())
            {
                previewPool.SetBackingFactoryFailingAfterForTest(1);
                RenderNodeOperation[] outputs = RenderThroughPlan(
                    [new PixelSortEffect()], [Input()], maxWorkingScale: 2f, new PipelineDiagnostics(), previewPool);
                Assert.That(outputs, Is.Empty, "preview drops the compute pass output on output allocation failure");
                RenderNodeOperation.DisposeAll(outputs);
            }
        });
    }

    // C7 for compute at a ping-pong SCRATCH acquire (input + output OK, a scratch fails mid-dispatch): must drop
    // (preview) / throw (delivery), NOT abort preview by rethrowing the raw scratch allocation exception (review M1).
    // Fail-after 2 lets the input + output acquires succeed and fails the first color scratch acquire.
    [Test]
    public void Compute_ScratchAllocationFailure_PreviewDropsDeliveryThrows()
    {
        var context = VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.RequireComputeCapable(context, "Compute scratch-failure gate");

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using (var deliveryPool = new RenderTargetPool())
            {
                deliveryPool.SetBackingFactoryFailingAfterForTest(2);
                Assert.That(
                    () => RenderNodeOperation.DisposeAll(RenderThroughPlan(
                        [new PixelSortEffect()], [Input()], float.PositiveInfinity, new PipelineDiagnostics(), deliveryPool)),
                    Throws.TypeOf<InvalidOperationException>(),
                    "delivery throws on compute scratch allocation failure");
            }

            using (var previewPool = new RenderTargetPool())
            {
                previewPool.SetBackingFactoryFailingAfterForTest(2);
                RenderNodeOperation[] outputs = RenderThroughPlan(
                    [new PixelSortEffect()], [Input()], maxWorkingScale: 2f, new PipelineDiagnostics(), previewPool);
                Assert.That(outputs, Is.Empty, "preview drops the compute pass output on scratch allocation failure");
                RenderNodeOperation.DisposeAll(outputs);
            }
        });
    }

    // PixelSort historically kept the original operation when a normal Vulkan dispatch failed during preview.
    // ComputeDispatchFailureBehavior.IdentityInPreview carries that contract separately from the no-Vulkan fallback;
    // delivery must still fail rather than export unsorted pixels, and every thrown path releases the detached input.
    [Test]
    public void ComputeDispatchFailure_IdentityPreviewPassesThroughDeliveryAndCancellationThrow()
    {
        var context = VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.RequireComputeCapable(context, "compute dispatch-failure fallback gate");

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            static CompiledPlan CompileThrowingPlan(out FrameResources frame)
            {
                var builder = new EffectGraphBuilder(s_bounds, outputScale: 1f, workingScale: 1f)
                    .Compute(ComputeNodeDescriptor.Create(
                        static _ => throw new InvalidOperationException("simulated Vulkan dispatch failure"),
                        passCount: 1,
                        ComputeFallback.Identity,
                        structuralToken: "throwing-dispatch",
                        dispatchFailureBehavior: ComputeDispatchFailureBehavior.IdentityInPreview));
                using EffectGraph graph = builder.Build();
                CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
                frame = EffectGraphCompiler.ResolveResources(plan, Rect.Invalid, workingScale: 1f);
                return plan;
            }

            CompiledPlan plan = CompileThrowingPlan(out FrameResources frame);
            using var pool = new RenderTargetPool();

            RenderNodeOperation previewInput = Input();
            RenderNodeOperation[] preview = PlanExecutor.Execute(
                plan, frame, [previewInput], outputScale: 1f, workingScale: 1f, maxWorkingScale: 2f,
                diagnostics: null, pool);
            try
            {
                Assert.That(preview, Is.EqualTo(new[] { previewInput }),
                    "an identity-fallback preview keeps the source operation after dispatch failure");
            }
            finally
            {
                RenderNodeOperation.DisposeAll(preview);
            }

            bool prepareInputDisposed = false;
            RenderTarget.SetComputeWritePreparedObserverForTest(
                static () => throw new InvalidOperationException("simulated output prepare failure"));
            try
            {
                RenderNodeOperation prepareInput = RenderNodeOperation.CreateLambda(
                    s_bounds, static _ => { }, onDispose: () => prepareInputDisposed = true);
                Assert.That(
                    () => PlanExecutor.Execute(
                        plan, frame, [prepareInput], outputScale: 1f, workingScale: 1f, maxWorkingScale: 2f,
                        diagnostics: null, pool),
                    Throws.TypeOf<InvalidOperationException>(),
                    "IdentityInPreview applies to the callback, not output layout/flush preparation");
            }
            finally
            {
                RenderTarget.SetComputeWritePreparedObserverForTest(null);
            }
            Assert.That(prepareInputDisposed, Is.True,
                "a failed output preparation releases the detached input operation");

            bool deliveryInputDisposed = false;
            RenderNodeOperation deliveryInput = RenderNodeOperation.CreateLambda(
                s_bounds, static _ => { }, onDispose: () => deliveryInputDisposed = true);
            Assert.That(
                () => PlanExecutor.Execute(
                    plan, frame, [deliveryInput], outputScale: 1f, workingScale: 1f,
                    maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(deliveryInputDisposed, Is.True,
                "the delivery failure propagates only after releasing its detached input operation");

            var cancellationBuilder = new EffectGraphBuilder(s_bounds, outputScale: 1f, workingScale: 1f)
                .Compute(ComputeNodeDescriptor.Create(
                    static _ => throw new OperationCanceledException("simulated cancellation"),
                    passCount: 1,
                    ComputeFallback.Identity,
                    structuralToken: "cancelled-dispatch",
                    dispatchFailureBehavior: ComputeDispatchFailureBehavior.IdentityInPreview));
            using EffectGraph cancellationGraph = cancellationBuilder.Build();
            CompiledPlan cancellationPlan = EffectGraphCompiler.Compile(cancellationGraph, diagnostics: null);
            FrameResources cancellationFrame = EffectGraphCompiler.ResolveResources(
                cancellationPlan, Rect.Invalid, workingScale: 1f);
            RenderNodeOperation cancellationInput = Input();

            Assert.That(
                () => PlanExecutor.Execute(
                    cancellationPlan, cancellationFrame, [cancellationInput], outputScale: 1f, workingScale: 1f,
                    maxWorkingScale: 2f, diagnostics: null, pool),
                Throws.TypeOf<OperationCanceledException>(),
                "cancellation must propagate even during an identity-fallback preview");

            var defaultBuilder = new EffectGraphBuilder(s_bounds, outputScale: 1f, workingScale: 1f)
                .Compute(ComputeNodeDescriptor.Create(
                    static _ => throw new InvalidOperationException("default dispatch failure"),
                    passCount: 1,
                    ComputeFallback.Identity,
                    structuralToken: "default-throwing-dispatch"));
            using EffectGraph defaultGraph = defaultBuilder.Build();
            CompiledPlan defaultPlan = EffectGraphCompiler.Compile(defaultGraph, diagnostics: null);
            FrameResources defaultFrame = EffectGraphCompiler.ResolveResources(
                defaultPlan, Rect.Invalid, workingScale: 1f);

            Assert.That(
                () => PlanExecutor.Execute(
                    defaultPlan, defaultFrame, [Input()], outputScale: 1f, workingScale: 1f,
                    maxWorkingScale: 2f, diagnostics: null, pool),
                Throws.TypeOf<InvalidOperationException>(),
                "the no-Vulkan identity fallback must not change the default dispatch-failure policy");
        });
    }

    [Test]
    public void ComputeDispatchFailure_ScratchBackendPreparationAlwaysPropagates()
    {
        var context = VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.RequireComputeCapable(context, "compute scratch preparation failure gate");

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var builder = new EffectGraphBuilder(s_bounds, outputScale: 1f, workingScale: 1f)
                .Compute(ComputeNodeDescriptor.Create(
                    static ctx => _ = ctx.AcquireColorScratch(),
                    passCount: 1,
                    ComputeFallback.Identity,
                    colorScratchCount: 1,
                    structuralToken: "scratch-prepare-failure",
                    dispatchFailureBehavior: ComputeDispatchFailureBehavior.IdentityInPreview));
            using EffectGraph graph = builder.Build();
            CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
            FrameResources frame = EffectGraphCompiler.ResolveResources(plan, Rect.Invalid, workingScale: 1f);
            using var pool = new RenderTargetPool();

            int preparations = 0;
            bool inputDisposed = false;
            RenderTarget.SetComputeWritePreparedObserverForTest(() =>
            {
                preparations++;
                if (preparations == 2)
                    throw new InvalidOperationException("simulated scratch backend preparation failure");
            });
            try
            {
                RenderNodeOperation input = RenderNodeOperation.CreateLambda(
                    s_bounds, static _ => { }, onDispose: () => inputDisposed = true);
                Assert.That(
                    () => PlanExecutor.Execute(
                        plan, frame, [input], outputScale: 1f, workingScale: 1f,
                        maxWorkingScale: 2f, diagnostics: null, pool),
                    Throws.TypeOf<InvalidOperationException>(),
                    "a backend preparation failure inside the callback must not become an identity preview");
            }
            finally
            {
                RenderTarget.SetComputeWritePreparedObserverForTest(null);
            }

            Assert.Multiple(() =>
            {
                Assert.That(preparations, Is.EqualTo(2), "the injected failure occurred during scratch preparation");
                Assert.That(inputDisposed, Is.True, "the propagated backend failure releases the detached input");
            });
        });
    }

    // N3: an empty input op reaching a compute pass must pass straight through — on either backend. The
    // GPU path (ExecuteCompute) already guards empty input; the CPU-fallback path (no Vulkan) lacked the same
    // guard and spuriously threw in delivery. Neither the dispatch nor the CPU callback may run for an empty input.
    // On a 3D-capable host this exercises the GPU guard; on a no-Vulkan host it exercises the added fallback guard.
    [Test]
    public void EmptyInputToComputePass_PassesThroughOnEitherBackend()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var compute = ComputeNodeDescriptor.Create(
                dispatch: static _ => throw new InvalidOperationException("dispatch must not run for an empty input"),
                passCount: 1,
                fallback: ComputeFallback.CpuCallback,
                cpuCallback: static _ => throw new InvalidOperationException("CPU callback must not run for an empty input"));

            var builder = new EffectGraphBuilder(s_bounds, outputScale: 1f, workingScale: 1f);
            builder.Compute(compute);
            using EffectGraph graph = builder.Build();
            CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
            FrameResources frame = EffectGraphCompiler.ResolveResources(plan, Rect.Invalid, workingScale: 1f);

            RenderNodeOperation empty = RenderNodeOperation.CreateLambda(
                new Rect(10, 10, 0, 0), static _ => { }, hitTest: static _ => false);

            RenderNodeOperation[] outputs = PlanExecutor.Execute(
                plan, frame, [empty], outputScale: 1f, workingScale: 1f,
                maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: null);

            int count = outputs.Length;
            RenderNodeOperation.DisposeAll(outputs);
            Assert.That(count, Is.EqualTo(1), "the empty input passes through the compute pass without throwing");
        });
    }

    private static FilterEffect MakeEffect(string kind) => kind switch
    {
        "Fused" => new Saturate { Amount = { CurrentValue = 1.3f } },
        "Geometry" => new Clipping { Left = { CurrentValue = 10 }, Top = { CurrentValue = 10 } },
        "Compute" => new PixelSortEffect(),
        "Split" => new SplitEffect { HorizontalDivisions = { CurrentValue = 2 }, VerticalDivisions = { CurrentValue = 2 } },
        "Composite" => new LayerEffect(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static PipelineDiagnosticsSnapshot RenderScene(Drawable.Resource resource)
    {
        PixelSize size = SceneFixtures.ReferenceSize;
        using RenderTarget target = RenderTarget.Create(size.Width, size.Height)
            ?? throw new InvalidOperationException("RenderTarget.Create returned null.");
        using var canvas = new ImmediateCanvas(target, 1f, logicalSize: size.ToSize(1));
        canvas.Clear(Colors.Black);

        using var node = new DrawableRenderNode(resource);
        using (var ctx = new GraphicsContext2D(node, size.ToSize(1), 1f))
        {
            resource.GetOriginal().Render(ctx, resource);
        }

        var processor = new RenderNodeProcessor(node, useRenderCache: false, outputScale: 1f);
        RenderNodeOperation[] ops = processor.PullToRoot();
        foreach (RenderNodeOperation op in ops)
        {
            op.Render(canvas);
            op.Dispose();
        }

        return processor.Diagnostics.Snapshot();
    }

    private static RenderNodeOperation[] RenderThroughPlan(
        IReadOnlyList<FilterEffect> effects, RenderNodeOperation[] inputs, float maxWorkingScale,
        PipelineDiagnostics diagnostics, RenderTargetPool pool)
    {
        var builder = new EffectGraphBuilder(s_bounds, outputScale: 1f, workingScale: 1f);
        foreach (FilterEffect effect in effects)
        {
            effect.Describe(builder, (FilterEffect.Resource)(object)effect.ToResource(CompositionContext.Default));
        }

        using EffectGraph graph = builder.Build();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics);
        FrameResources frame = EffectGraphCompiler.ResolveResources(plan, Rect.Invalid, workingScale: 1f);
        return PlanExecutor.Execute(
            plan, frame, inputs, outputScale: 1f, workingScale: 1f,
            maxWorkingScale: maxWorkingScale, diagnostics: diagnostics, pool: pool);
    }

    private static RenderNodeOperation Input()
    {
        return RenderNodeOperation.CreateLambda(
            s_bounds,
            canvas => canvas.DrawRectangle(s_bounds.Deflate(16), Brushes.Resource.White, null),
            hitTest: s_bounds.Contains);
    }

    // Two disjoint filled blobs so PartsSplit's contour tracer discovers more than one part.
    private static RenderNodeOperation ContourInput()
    {
        return RenderNodeOperation.CreateLambda(
            s_bounds,
            canvas =>
            {
                canvas.DrawRectangle(new Rect(20, 20, 40, 40), Brushes.Resource.White, null);
                canvas.DrawRectangle(new Rect(100, 60, 40, 40), Brushes.Resource.White, null);
            },
            hitTest: s_bounds.Contains);
    }
}
