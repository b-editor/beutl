using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Logging;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Pins the legacy (pre-redesign) effect-pipeline cost model on the ColorChain / MixedChain fixtures
/// (feature 004, T008; contracts/observability.md O1/O2, research §0). Counters are asserted as exact
/// equalities derived from the code paths in <see cref="FilterEffectActivator"/> /
/// <see cref="CustomFilterEffectContext"/>; the derivation is documented next to each assertion. A render at
/// output scale 1.0 is structure-determined, so the counts are size-independent and rendered at the small
/// reference size for speed.
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
    // Mixed-plan model (W1 + W2): the group is one graph — [opaque Blur, fused Gamma+Invert, opaque DropShadow,
    // fused LUT]. Each opaque segment runs the retained activator over the current op; Blur and DropShadow are
    // Skia image filters, so the bridge returns a deferred-filter op without baking (0 cost each). Each fused pass
    // bakes its deferred input into one pooled target and draws once:
    //   Blur (opaque, deferred) ......... +0
    //   Gamma+Invert (fused) ............ +1 GpuPass, +1 TargetAllocation
    //   DropShadow (opaque, deferred) ... +0
    //   LUT (fused) ..................... +1 GpuPass, +1 TargetAllocation
    // Totals: GpuPasses = 2, TargetAllocations = 2, FullFrameMaterializations = 0, FlushSyncs = 0 — strictly below
    // the 6 / 6 / 3 / 6 legacy baseline (US1-AS2).
    [Test]
    public void MixedChain_BelowLegacyBaseline()
    {
        PipelineDiagnosticsSnapshot counters = RenderAndSnapshot(
            SceneFixtures.MixedChain(SceneFixtures.ReferenceSize));

        Assert.Multiple(() =>
        {
            Assert.That(counters.GpuPasses, Is.EqualTo(2), nameof(counters.GpuPasses));
            Assert.That(counters.TargetAllocations, Is.EqualTo(2), nameof(counters.TargetAllocations));
            Assert.That(counters.FullFrameMaterializations, Is.EqualTo(0), nameof(counters.FullFrameMaterializations));
            Assert.That(counters.FlushSyncs, Is.EqualTo(0), nameof(counters.FlushSyncs));

            // US1-AS2: strictly below the recorded legacy baseline (6 / 6 / 3 / 6).
            Assert.That(counters.GpuPasses, Is.LessThan(6));
            Assert.That(counters.TargetAllocations, Is.LessThan(6));
            Assert.That(counters.FullFrameMaterializations, Is.LessThan(3));
            Assert.That(counters.FlushSyncs, Is.LessThan(6));
        });
    }

    // The counters are additive and nullable: a render whose activator is handed no PipelineDiagnostics must
    // run every effect-path branch (Flush bake, CustomFilterEffectContext.CreateTarget, Open) with no throw
    // and produce a materialized target.
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

            using var feContext = new FilterEffectContext(bounds, outputScale: 1f, workingScale: 1f);
            var gamma = new Gamma();
            gamma.Amount.CurrentValue = 1.5f;
            var resource = (FilterEffect.Resource)(object)gamma.ToResource(Beutl.Composition.CompositionContext.Default);
            gamma.ApplyTo(feContext, resource);

            using var targets = new EffectTargets { new EffectTarget(op) };
            using var builder = new SKImageFilterBuilder();
            // diagnostics defaults to null — exercises the unobserved path.
            using var activator = new FilterEffectActivator(
                targets, builder, outputScale: 1f, workingScale: 1f);

            Assert.That(() => activator.Apply(feContext), Throws.Nothing);
            Assert.That(activator.Diagnostics, Is.Null);
            Assert.That(activator.CurrentTargets, Has.Count.EqualTo(1));
            Assert.That(activator.CurrentTargets[0].RenderTarget, Is.Not.Null,
                "the custom Gamma pass must have materialized a real buffer even without diagnostics");
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
                using var canvas = new ImmediateCanvas(target, 1f, logicalSize: size.ToSize(1));
                canvas.Clear(Colors.Black);

                Drawable.Resource resource = makeScene();
                using var node = new DrawableRenderNode(resource);
                using (var ctx = new GraphicsContext2D(node, size.ToSize(1), 1f))
                {
                    resource.GetOriginal().Render(ctx, resource);
                }

                var processor = new RenderNodeProcessor(
                    node, useRenderCache: false, outputScale: 1f, diagnostics: diagnostics, pool: pool);
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
        });
    }
}
