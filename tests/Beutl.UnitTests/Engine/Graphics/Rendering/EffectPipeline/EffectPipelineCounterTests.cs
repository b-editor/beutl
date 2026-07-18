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
/// Pins the effect-pipeline cost model on the ColorChain / MixedChain fixtures (feature 004, T008/T030;
/// contracts/observability.md O1/O2, research §0). Counters are asserted as exact equalities derived from the
/// compiled plan's pass schedule; the derivation is documented next to each assertion. A render at output
/// scale 1.0 is structure-determined, so the counts are size-independent and rendered at the small reference
/// size for speed.
/// </summary>
[NonParallelizable]
[TestFixture]
public class EffectPipelineCounterTests
{
    // LutEffect's static constructor resolves an ILogger via the application logger factory; seed it so
    // MixedChain (which uses LutEffect) can instantiate in the bare unit-test harness.
    [OneTimeSetUp]
    public void SeedLoggerFactory()
    {
        Log.LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(static _ => { });
    }

    // ColorChain = Gamma, HueRotate, Saturate, Brightness, Invert — now all migrated and coordinate-invariant.
    //
    // SUPERSEDED legacy model (rollout step 1, now obsolete by design — the same fixture fuses): the flattened
    // group ran five separate render nodes; the two custom SKSL effects (Gamma, Invert) each forced a Flush bake
    // plus their own pass while the interleaved color filters deferred into those bakes. Totals were
    // FullFrameMaterializations = 2, GpuPasses = 4, TargetAllocations = 4, FlushSyncs = 4 (research §0's
    // "≥ 1 materialization + ≥ 1 pass per custom item").
    //
    // SC-001 fused model: the group is one render node, so all five effects are coordinate-invariant nodes in one
    // graph and compile to a single FusedShaderPass. The executor bakes the input into one pooled target and draws
    // the composed shader (two SKSL snippets wrapping three color filters) exactly once — no legacy Flush
    // (FullFrameMaterializations == 0) and, being Skia-only, no backend-transition sync (FlushSyncs == 0, C4.2):
    // GpuPasses == 1 over ≤ 1 intermediate.
    [Test]
    public void ColorChain_FusesToSinglePass()
    {
        PipelineDiagnosticsSnapshot counters = RenderAndSnapshot(
            SceneFixtures.ColorChain(SceneFixtures.ReferenceSize));

        Assert.Multiple(() =>
        {
            Assert.That(counters.GpuPasses, Is.EqualTo(1), "SC-001: a run of invariant color effects is one GPU pass");
            Assert.That(counters.TargetAllocations, Is.LessThanOrEqualTo(1), "SC-001: at most one intermediate");
            Assert.That(counters.FullFrameMaterializations, Is.EqualTo(0), "no legacy Flush bake on the fused path");
            Assert.That(counters.FlushSyncs, Is.EqualTo(0), "Skia-only plan has no backend-transition sync (C4.2)");
        });
    }

    // MixedChain = Blur, Gamma, Invert, DropShadow, LutEffect.
    //
    // SUPERSEDED legacy baseline (rollout step 1): the five effects ran as separate render nodes; the two Skia
    // filters deferred but each of the three custom SKSL effects forced a Flush bake plus a pass. Totals were
    // FullFrameMaterializations = 3, GpuPasses = 6, TargetAllocations = 6, FlushSyncs = 6.
    //
    // Re-derived for step 5b: Blur and DropShadow left the bridge, so the plan is now fully declarative —
    // [SkiaFilter Blur, fused Gamma+Invert, SkiaFilter DropShadow, fused LUT]. Fusion never crosses the Skia
    // filters (C2 groups only *adjacent* Skia filters, and these are separated by fused color runs), so each of the
    // four passes bakes its input into one pooled target and draws once:
    //   Blur (SkiaFilter) ............... +1 GpuPass, +1 TargetAllocation
    //   Gamma+Invert (fused) ............ +1 GpuPass, +1 TargetAllocation
    //   DropShadow (SkiaFilter) ......... +1 GpuPass, +1 TargetAllocation
    //   LUT (fused) ..................... +1 GpuPass, +1 TargetAllocation
    // Totals: GpuPasses = 4, TargetAllocations = 4, FullFrameMaterializations = 0, FlushSyncs = 0. This is higher
    // than the transitional bridge's 2/2 (the bridge folded each deferred Skia filter into the next fused bake for
    // free), but that fold is a bridge artifact; the honest declarative passes are cacheable and still far below the
    // 6 / 6 / 3 / 6 pre-redesign legacy baseline (US1-AS2), which is the invariant this gate enforces.
    [Test]
    public void MixedChain_BelowLegacyBaseline()
    {
        PipelineDiagnosticsSnapshot counters = RenderAndSnapshot(
            SceneFixtures.MixedChain(SceneFixtures.ReferenceSize));

        Assert.Multiple(() =>
        {
            Assert.That(counters.GpuPasses, Is.EqualTo(4), nameof(counters.GpuPasses));
            Assert.That(counters.TargetAllocations, Is.EqualTo(4), nameof(counters.TargetAllocations));
            Assert.That(counters.FullFrameMaterializations, Is.EqualTo(0), nameof(counters.FullFrameMaterializations));
            Assert.That(counters.FlushSyncs, Is.EqualTo(0), nameof(counters.FlushSyncs));

            // US1-AS2: strictly below the recorded legacy baseline (6 / 6 / 3 / 6).
            Assert.That(counters.GpuPasses, Is.LessThan(6));
            Assert.That(counters.TargetAllocations, Is.LessThan(6));
            Assert.That(counters.FullFrameMaterializations, Is.LessThan(3));
            Assert.That(counters.FlushSyncs, Is.LessThan(6));
        });
    }

    // The counters are additive and nullable: a render handed no PipelineDiagnostics must run every effect-path
    // branch (describe, compile, resolve, execute) with no throw and produce a real output operation.
    [Test]
    public void NullDiagnostics_EffectPathStillRenders()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var bounds = new Rect(0, 0, 64, 48);
            RenderNodeOperation op = RenderNodeOperation.CreateLambda(
                bounds,
                canvas => canvas.DrawRectangle(bounds, Brushes.Resource.White, null),
                hitTest: _ => false);

            var gamma = new Gamma();
            gamma.Amount.CurrentValue = 1.5f;
            var resource = (FilterEffect.Resource)(object)gamma.ToResource(Beutl.Composition.CompositionContext.Default);

            var builder = new EffectGraphBuilder(bounds, outputScale: 1f, workingScale: 1f, renderIntent: RenderIntent.Delivery);
            gamma.Describe(builder, resource);
            using EffectGraph graph = builder.Build();
            CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
            FrameResources frame = EffectGraphCompiler.ResolveResources(plan, bounds, workingScale: 1f);

            RenderNodeOperation[] outputs = null!;
            Assert.That(
                () => outputs = PlanExecutor.Execute(
                    plan, frame, [op], outputScale: 1f, workingScale: 1f,
                    maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: null, renderIntent: RenderIntent.Delivery),
                Throws.Nothing);
            Assert.That(outputs, Has.Length.EqualTo(1));
            RenderNodeOperation.DisposeAll(outputs);
        });
    }

    // T014 (re-pinned to the fused plan): a structurally-constant scene rendered K times through one shared pool
    // acquires the fused chain's single intermediate every frame, but only frame 1 allocates it — frames 2..K add
    // ZERO fresh allocations / pool misses (all hits), the steady-state gate for US3 (SC-003). ColorChain now fuses
    // to one pass, so there is exactly one acquire site (was four under the legacy per-effect model).
    //
    // The full golden + frozen-reference suites remain unchanged: they render through bare, pool-less
    // processors (GoldenImageHarness / EffectReferenceFreezeTests), so pooling cannot perturb their bytes.
    [Test]
    public void ColorChain_SteadyState_ReusesPooledTargetsAfterFirstFrame()
    {
        const int frames = 4;
        PipelineDiagnosticsSnapshot[] perFrame = RenderFramesWithPool(
            () => SceneFixtures.ColorChain(SceneFixtures.ReferenceSize), frames);

        Assert.Multiple(() =>
        {
            Assert.That(perFrame[0].PoolAcquires, Is.EqualTo(1), "frame 1: the single fused pass acquires one target");
            Assert.That(perFrame[0].TargetAllocations, Is.EqualTo(perFrame[0].PoolMisses), "each miss allocates once");
            Assert.That(perFrame[0].PoolMisses, Is.EqualTo(1), "frame 1 warms the pool with the one intermediate");

            for (int f = 1; f < frames; f++)
            {
                Assert.That(perFrame[f].TargetAllocations, Is.EqualTo(0), $"frame {f + 1} adds no fresh allocations");
                Assert.That(perFrame[f].PoolMisses, Is.EqualTo(0), $"frame {f + 1} has no pool misses");
                Assert.That(perFrame[f].PoolAcquires, Is.EqualTo(1),
                    $"frame {f + 1} acquires the same one buffer, now a hit");
            }
        });
    }

    // Renders the same scene structure `frames` times through one shared RenderTargetPool, mirroring how
    // Renderer drives per-frame processors over a persistent pool (Trim at frame start, fresh node/processor
    // per frame, ops disposed within the frame so leases return to the pool). Counters are reset per frame so
    // each snapshot is that frame's delta.
    private static PipelineDiagnosticsSnapshot[] RenderFramesWithPool(Func<Drawable.Resource> makeScene, int frames)
    {
        VulkanTestEnvironment.EnsureAvailable();
        return VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            PixelSize size = SceneFixtures.ReferenceSize;
            var diagnostics = new PipelineDiagnostics();
            using var pool = new RenderTargetPool();
            var snapshots = new PipelineDiagnosticsSnapshot[frames];

            for (int f = 0; f < frames; f++)
            {
                pool.Trim(f);
                diagnostics.Reset();

                using RenderTarget target = RenderTarget.Create(size.Width, size.Height)
                                            ?? throw new InvalidOperationException("RenderTarget.Create returned null.");
                using var canvas = new ImmediateCanvas(target, RenderIntent.Delivery, 1f, logicalSize: size.ToSize(1));
                canvas.Clear(Colors.Black);

                Drawable.Resource resource = makeScene();
                using var node = new DrawableRenderNode(resource);
                using (var ctx = new GraphicsContext2D(node, size.ToSize(1), 1f))
                {
                    resource.GetOriginal().Render(ctx, resource);
                }

                var processor = new RenderNodeProcessor(
                    pool, node, useRenderCache: false, RenderIntent.Delivery, outputScale: 1f,
                    diagnostics: diagnostics);
                RenderNodeOperation[] ops = processor.PullToRoot();
                foreach (RenderNodeOperation op in ops)
                {
                    op.Render(canvas);
                    op.Dispose();
                }

                snapshots[f] = diagnostics.Snapshot();
            }

            return snapshots;
        });
    }

    // Renders a scene at output scale 1.0 through the real pull path and returns the renderer's counter
    // snapshot. Mirrors GoldenImageHarness.RenderAtScale but exposes the processor's PipelineDiagnostics.
    private static PipelineDiagnosticsSnapshot RenderAndSnapshot(Drawable.Resource resource)
    {
        VulkanTestEnvironment.EnsureAvailable();
        return VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            PixelSize size = SceneFixtures.ReferenceSize;
            using RenderTarget target = RenderTarget.Create(size.Width, size.Height)
                                        ?? throw new InvalidOperationException("RenderTarget.Create returned null.");
            using var canvas = new ImmediateCanvas(target, RenderIntent.Delivery, 1f, logicalSize: size.ToSize(1));
            canvas.Clear(Colors.Black);

            using var node = new DrawableRenderNode(resource);
            using (var ctx = new GraphicsContext2D(node, size.ToSize(1), 1f))
            {
                resource.GetOriginal().Render(ctx, resource);
            }

            var processor = new RenderNodeProcessor(node, useRenderCache: false, RenderIntent.Delivery, outputScale: 1f);
            RenderNodeOperation[] ops = processor.PullToRoot();
            foreach (RenderNodeOperation op in ops)
            {
                op.Render(canvas);
                op.Dispose();
            }

            return processor.Diagnostics.Snapshot();
        });
    }

    // ---- SC-002: animated parameters don't rebuild the pipeline (T036) ----------------------------------
    //
    // These drive ONE persistent FilterEffectRenderNode across frames (as the real render graph reuses a node via
    // Update), so its single-entry PlanCache spans the animation. A parameter-only frame re-describes the graph and
    // rebinds the frame's uniforms/factories/contexts + re-resolved bounds without recompiling: PlanCompilations
    // stays at 1 and, given the warm process-wide ProgramCache, ProgramCreations is 0 after frame 1.

    private static readonly Rect s_animBounds = new(0, 0, 128, 96);

    // A structurally constant chain plus a per-frame setter over its own effect instances (so a fresh-compile
    // control can reproduce any frame's values on an independent chain).
    private sealed record Chain(FilterEffect Root, Action<int> SetFrame);

    // SC-002 (1): 100 frames of a fusing ColorChain with an animated gamma ⇒ one compilation, zero program
    // creations after frame 1, and the middle frame matches an independent fresh-compile render.
    [Test]
    public void ColorChain_AnimatedGamma_CompilesOnceAndReusesProgram()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            ProgramCache.Clear();
            const int frames = 100;
            const int probe = 50;

            DriveResult drive = DriveAnimated(MakeAnimatedColorChain, frames, [probe]);
            PipelineDiagnosticsSnapshot[] snaps = drive.Snapshots;
            Bitmap?[] bmps = drive.Bitmaps;
            using Bitmap? cachedMid = bmps[probe];
            using Bitmap fresh = RenderFresh(MakeAnimatedColorChain, probe);

            Assert.Multiple(() =>
            {
                Assert.That(snaps[0].PlanCompilations, Is.EqualTo(1), "frame 1 compiles the plan once");
                Assert.That(snaps[0].ProgramCreations, Is.GreaterThanOrEqualTo(1), "frame 1 warms the program cache");
                long recompiles = snaps.Skip(1).Sum(s => s.PlanCompilations);
                long reprograms = snaps.Skip(1).Sum(s => s.ProgramCreations);
                Assert.That(recompiles, Is.EqualTo(0), "no recompile across 99 animated frames (SC-002)");
                Assert.That(reprograms, Is.EqualTo(0), "no program creation after frame 1 (SC-002)");
                AssertMatches(fresh, cachedMid!, "cached middle frame vs fresh compile");
            });
        });
    }

    // SC-002 (2): a bounds-animating case — a declarative SkiaFilter Blur whose sigma animates, ahead of a fused
    // Gamma. The per-frame filter factory and the bounds both re-resolve on a cache hit, so the plan compiles once
    // while the output (growing blur) tracks a fresh compile.
    [Test]
    public void MixedChain_AnimatedBlurSigma_CompilesOnceWithGrowingBounds()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            ProgramCache.Clear();
            const int frames = 8;
            int last = frames - 1;

            DriveResult drive = DriveAnimated(MakeAnimatedBlurChain, frames, [1, last]);
            PipelineDiagnosticsSnapshot[] snaps = drive.Snapshots;
            Bitmap?[] bmps = drive.Bitmaps;
            using Bitmap? early = bmps[1];
            using Bitmap? wide = bmps[last];
            using Bitmap freshWide = RenderFresh(MakeAnimatedBlurChain, last);

            Assert.Multiple(() =>
            {
                Assert.That(snaps[0].PlanCompilations, Is.EqualTo(1), "frame 1 compiles once");
                Assert.That(snaps.Skip(1).Sum(s => s.PlanCompilations), Is.EqualTo(0),
                    "bounds-animating frames re-resolve sizes without recompiling (C5)");
                AssertMatches(freshWide, wide!, "cached wide-sigma frame vs fresh compile");
                Assert.That(early!.Width, Is.EqualTo(wide!.Width), "structure-constant chain keeps a fixed raster size");
            });
        });
    }

    // SC-002 (3): a structural change (insert an effect) between frames ⇒ exactly one ADDITIONAL recompile; the
    // stable frames on either side hit.
    [Test]
    public void StructuralChange_InsertEffect_RecompilesExactlyOnce()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            ProgramCache.Clear();
            // Frames 0..2 = [Gamma]; frames 3..5 = [Gamma, Invert] (a structural insert at frame 3).
            var gamma = new Gamma { Amount = { CurrentValue = 120f } };
            var group = new FilterEffectGroup();
            group.Children.Add(gamma);
            var chain = new Chain(group, f =>
            {
                bool wantInvert = f >= 3;
                bool hasInvert = group.Children.Count > 1;
                if (wantInvert && !hasInvert)
                    group.Children.Add(new Invert { Amount = { CurrentValue = 1f } });
            });

            PipelineDiagnosticsSnapshot[] snaps = DriveAnimated(() => chain, frames: 6, capture: []).Snapshots;

            long total = snaps.Sum(s => s.PlanCompilations);
            Assert.Multiple(() =>
            {
                Assert.That(snaps[0].PlanCompilations, Is.EqualTo(1), "frame 0 compiles the initial structure");
                Assert.That(snaps[3].PlanCompilations, Is.EqualTo(1), "the insert at frame 3 recompiles");
                Assert.That(total, Is.EqualTo(2), "one initial compile + exactly one for the structural change");
                Assert.That(snaps[1].PlanCompilations + snaps[2].PlanCompilations
                    + snaps[4].PlanCompilations + snaps[5].PlanCompilations, Is.EqualTo(0),
                    "stable frames on both sides hit the cache");
            });
        });
    }

    // SC-002 (4): a cached plan rendered at extreme parameter values equals a fresh compile at those values — the
    // rebind writes the frame's uniforms with no recompile, whatever the magnitude.
    [Test]
    public void ParameterExtreme_CachedPlanEqualsFreshCompile()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            ProgramCache.Clear();
            // Frame 0 warms the cache at a moderate gamma; frame 1 drives an extreme gamma on the same cached node.
            DriveResult drive = DriveAnimated(MakeExtremeGammaChain, frames: 2, [1]);
            PipelineDiagnosticsSnapshot[] snaps = drive.Snapshots;
            Bitmap?[] bmps = drive.Bitmaps;
            using Bitmap? cachedExtreme = bmps[1];
            using Bitmap freshExtreme = RenderFresh(MakeExtremeGammaChain, 1);

            Assert.Multiple(() =>
            {
                Assert.That(snaps[1].PlanCompilations, Is.EqualTo(0), "the extreme-value frame hits the cache");
                AssertMatches(freshExtreme, cachedExtreme!, "cached extreme-parameter frame vs fresh compile");
            });
        });
    }

    // SC-002 (5) / T034 stale-parameter regression: an animated parameter on a declarative SkiaFilter effect
    // (DropShadow position) inside a structurally constant chain that also contains a fused node (Gamma) must render
    // each frame identically to a fresh compile — proving the cache hit swaps the cached passes' captured factories
    // for the frame's — while PlanCompilations stays at 1. A stale-capture bug would freeze the shadow after frame 0.
    [Test]
    public void AnimatedSkiaFilterEffect_StaysLiveWhilePlanCompilesOnce()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            ProgramCache.Clear();
            const int frames = 3;

            DriveResult drive = DriveAnimated(MakeAnimatedDropShadowChain, frames, [0, 1, 2]);
            PipelineDiagnosticsSnapshot[] snaps = drive.Snapshots;
            Bitmap?[] bmps = drive.Bitmaps;

            Assert.Multiple(() =>
            {
                Assert.That(snaps[0].PlanCompilations, Is.EqualTo(1), "frame 0 compiles once");
                Assert.That(snaps.Skip(1).Sum(s => s.PlanCompilations), Is.EqualTo(0),
                    "the animated effect keeps hitting the cache (stable structural token)");

                for (int f = 0; f < frames; f++)
                {
                    using Bitmap fresh = RenderFresh(MakeAnimatedDropShadowChain, f);
                    AssertMatches(fresh, bmps[f]!, $"animated frame {f} vs fresh compile");
                }

                // Proof the animation is live on cache hits, not vacuous: the shadow's moving position
                // re-resolves the output op's union bounds every frame, so they grow across the run. A frozen
                // (stale-context) cache would replay frame 0's bounds unchanged.
                Assert.That(drive.OutputBounds[2].Right, Is.GreaterThan(drive.OutputBounds[0].Right),
                    "the moving drop-shadow expands the output bounds across frames");
            });

            foreach (Bitmap? b in bmps)
                b?.Dispose();
        });
    }

    // ---- SC-002 drivers -----------------------------------------------------------------------------------

    private static Chain MakeAnimatedColorChain()
    {
        var gamma = new Gamma();
        var group = new FilterEffectGroup();
        group.Children.Add(gamma);
        group.Children.Add(new HueRotate { Angle = { CurrentValue = 90f } });
        group.Children.Add(new Saturate { Amount = { CurrentValue = 1.4f } });
        group.Children.Add(new Brightness { Amount = { CurrentValue = 1.2f } });
        group.Children.Add(new Invert { Amount = { CurrentValue = 1f } });
        return new Chain(group, f => gamma.Amount.CurrentValue = 50f + 2f * f);
    }

    private static Chain MakeAnimatedBlurChain()
    {
        var blur = new Blur();
        var group = new FilterEffectGroup();
        group.Children.Add(blur);
        group.Children.Add(new Gamma { Amount = { CurrentValue = 140f } });
        return new Chain(group, f =>
        {
            float sigma = f switch
            {
                0 => 0,
                1 => 2,
                2 => 0,
                _ => 1 + f,
            };
            blur.Sigma.CurrentValue = new Size(sigma, sigma);
        });
    }

    private static Chain MakeExtremeGammaChain()
    {
        var gamma = new Gamma();
        var group = new FilterEffectGroup();
        group.Children.Add(gamma);
        group.Children.Add(new Invert { Amount = { CurrentValue = 1f } });
        // Frame 0 = moderate; frame 1 = the clamp extreme (Gamma clamps Amount/100 to [0.01, 3]).
        return new Chain(group, f => gamma.Amount.CurrentValue = f == 0 ? 120f : 300f);
    }

    private static Chain MakeAnimatedDropShadowChain()
    {
        var shadow = new DropShadow
        {
            Sigma = { CurrentValue = new Size(3, 3) },
            Color = { CurrentValue = Colors.Black },
        };
        var group = new FilterEffectGroup();
        group.Children.Add(new Gamma { Amount = { CurrentValue = 130f } });
        group.Children.Add(shadow);
        return new Chain(group, f => shadow.Position.CurrentValue = new Point(6 + 10 * f, 6 + 10 * f));
    }

    // Drives a persistent node across frames, resetting counters per frame and capturing a bitmap for the requested
    // frames. The node (hence its PlanCache) and the pool persist for the whole run.
    private sealed record DriveResult(PipelineDiagnosticsSnapshot[] Snapshots, Bitmap?[] Bitmaps, Rect[] OutputBounds);

    private static DriveResult DriveAnimated(Func<Chain> makeChain, int frames, IReadOnlyCollection<int> capture)
    {
        Chain chain = makeChain();
        var resource = (FilterEffect.Resource)chain.Root.ToResource(CompositionContext.Default);
        using var node = new PlanFilterEffectRenderNode(resource);
        var diagnostics = new PipelineDiagnostics();
        using var pool = new RenderTargetPool();
        var snaps = new PipelineDiagnosticsSnapshot[frames];
        var bmps = new Bitmap?[frames];
        var bounds = new Rect[frames];

        for (int f = 0; f < frames; f++)
        {
            pool.Trim(f);
            chain.SetFrame(f);
            bool updateOnly = false;
            resource.Update(chain.Root, CompositionContext.Default, ref updateOnly);
            node.Update(resource);

            diagnostics.Reset();
            var context = new RenderNodeContext([AnimInput()], RenderIntent.Delivery) { Diagnostics = diagnostics, Pool = pool };
            RenderNodeOperation[] ops = node.Process(context);
            snaps[f] = diagnostics.Snapshot();
            bounds[f] = ops.Aggregate<RenderNodeOperation, Rect>(default, (u, op) => u.Union(op.Bounds));

            // Rasterize every frame (the output op is consumed and flushed as the real pull path does), keeping the
            // bitmap only for requested frames.
            Bitmap frame = Rasterize(ops);
            if (capture.Contains(f))
                bmps[f] = frame;
            else
                frame.Dispose();
        }

        return new DriveResult(snaps, bmps, bounds);
    }

    // Renders one frame's values through a brand-new node (cold PlanCache ⇒ a genuine fresh compile), the control
    // every cache-hit render is compared against.
    private static Bitmap RenderFresh(Func<Chain> makeChain, int frame)
    {
        Chain chain = makeChain();
        chain.SetFrame(frame);
        var resource = (FilterEffect.Resource)chain.Root.ToResource(CompositionContext.Default);
        using var node = new PlanFilterEffectRenderNode(resource);
        var context = new RenderNodeContext([AnimInput()], RenderIntent.Delivery);
        RenderNodeOperation[] ops = node.Process(context);
        return Rasterize(ops);
    }

    private static RenderNodeOperation AnimInput()
    {
        return RenderNodeOperation.CreateLambda(
            s_animBounds,
            canvas => canvas.DrawRectangle(s_animBounds.Deflate(24), Brushes.Resource.White, null),
            hitTest: s_animBounds.Contains);
    }

    private static Bitmap Rasterize(RenderNodeOperation[] ops)
    {
        var size = PixelRect.FromRect(s_animBounds);
        using RenderTarget target = RenderTarget.Create(size.Width, size.Height)
            ?? throw new InvalidOperationException("RenderTarget.Create returned null.");
        using (var canvas = new ImmediateCanvas(target, RenderIntent.Delivery, 1f, logicalSize: s_animBounds.Size))
        {
            canvas.Clear();
            using (canvas.PushTransform(Matrix.CreateTranslation(-s_animBounds.X, -s_animBounds.Y)))
            {
                foreach (RenderNodeOperation op in ops)
                    op.Render(canvas);
            }
        }

        RenderNodeOperation.DisposeAll(ops);
        return target.Snapshot();
    }

    private static void AssertMatches(Bitmap expected, Bitmap actual, string because)
    {
        double ssim = ImageMetrics.Ssim(expected, actual);
        double mae = ImageMetrics.MeanAbsoluteError(expected, actual);
        TestContext.WriteLine($"{because}: SSIM={ssim:F4} MAE={mae:F4}");
        Assert.That(ssim, Is.GreaterThanOrEqualTo(GoldenThresholds.ExactSsimMin), $"SSIM ({because})");
        Assert.That(mae, Is.LessThanOrEqualTo(GoldenThresholds.ExactMaeMax), $"MAE ({because})");
    }
}
