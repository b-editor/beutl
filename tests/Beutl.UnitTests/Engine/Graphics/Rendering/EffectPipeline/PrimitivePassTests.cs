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

    // SplitTree = SplitEffect(3x3) -> Saturate (fused) -> LayerEffect (still bridged). Rendered pool-less through
    // the real pull path, so the counts are structure-determined. Derivation:
    //   Split:      input bake (+1 FullFrameMaterialization) + 9 tile draws (+9 GpuPasses)
    //   Saturate:   one fused draw per tile (+9 GpuPasses)
    //   LayerEffect: the bridge flushes + opens one target per tile (+9 GpuPasses, +9 FullFrameMaterializations,
    //               +9 FlushSyncs via the legacy CustomFilterEffectContext.Open) plus one trailing bake
    // Totals: GpuPasses = 28, FullFrameMaterializations = FlushSyncs = 10, one compile. The mix of split, geometry
    // (bridged layer) and fused color passes is far below a naive per-effect-per-tile materialization model.
    [Test]
    public void SplitTree_CounterDerivation()
    {
        VulkanTestEnvironment.EnsureAvailable();
        PipelineDiagnosticsSnapshot c = VulkanTestEnvironment.InvokeOnRenderThread(
            () => RenderScene(SceneFixtures.SplitTree(SceneFixtures.ReferenceSize)));

        Assert.Multiple(() =>
        {
            Assert.That(c.PlanCompilations, Is.EqualTo(1), "one compile for the mixed split/fused/bridged plan");
            Assert.That(c.GpuPasses, Is.EqualTo(28), "9 tile draws + 9 fused Saturate draws + 9 layer + 1 bake");
            Assert.That(c.FullFrameMaterializations, Is.EqualTo(10), "split input bake + 9 bridged layer bakes");
            Assert.That(c.FlushSyncs, Is.EqualTo(10), "the bridged LayerEffect's per-tile Opens, plus the layer bake");
        });
    }

    // A Skia->Vulkan->Skia chain (Saturate -> PixelSort -> Invert): FlushSyncs equals the number of backend
    // transitions in the schedule (C4.2) — one entering the compute pass, one leaving it. GpuPasses = 1 fused + 3
    // compute dispatches + 1 fused = 5.
    [Test]
    public void PixelSortChain_FlushSyncsEqualBackendTransitions()
    {
        var context = VulkanTestEnvironment.EnsureAvailable();
        if (!context.Supports3DRendering)
            Assert.Ignore("PixelSort requires a Vulkan compute-capable context (Supports3DRendering == false).");

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
            using var node = new FilterEffectRenderNode(resource);
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
        if (!context.Supports3DRendering)
            Assert.Ignore("PixelSort requires a Vulkan compute-capable context (Supports3DRendering == false).");

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
    [TestCase("Geometry")]
    [TestCase("Compute")]
    [TestCase("Split")]
    public void NewPass_AllocationFailure_PreviewDropsDeliveryThrows(string kind)
    {
        var context = VulkanTestEnvironment.EnsureAvailable();
        if (kind == "Compute" && !context.Supports3DRendering)
            Assert.Ignore("Compute allocation-failure gate requires a Vulkan compute-capable context.");

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
                plan, frame, [Input()], s_bounds, outputScale: 1f, workingScale: 1f,
                maxWorkingScale: float.PositiveInfinity, diagnostics: diagnostics, pool: pool);

            int count = outputs.Length;
            RenderNodeOperation.DisposeAll(outputs);
            Assert.That(count, Is.EqualTo(1), "the composite fans the two split branches into one output");
        });
    }

    private static FilterEffect MakeEffect(string kind) => kind switch
    {
        "Geometry" => new Clipping { Left = { CurrentValue = 10 }, Top = { CurrentValue = 10 } },
        "Compute" => new PixelSortEffect(),
        "Split" => new SplitEffect { HorizontalDivisions = { CurrentValue = 2 }, VerticalDivisions = { CurrentValue = 2 } },
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
            plan, frame, inputs, s_bounds, outputScale: 1f, workingScale: 1f,
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
