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

    // ColorChain = Gamma, HueRotate, Saturate, Brightness, Invert.
    // Gamma and Invert are custom-SKSL items (FEItem_Custom); HueRotate/Saturate/Brightness are Skia
    // SKColorFilter items that accumulate into the builder without materializing.
    // Walk of FilterEffectActivator.Apply over [Gamma(custom), Hue, Sat, Bright, Invert(custom)]:
    //   Gamma  -> Flush(force) bakes the input node op:            +1 FFM, +1 GpuPass, +1 Alloc, +1 Sync
    //             Gamma's ApplyToNewTarget (CreateTarget + Open):          +1 GpuPass, +1 Alloc, +1 Sync
    //   Hue/Sat/Bright -> accumulate into the SKImageFilterBuilder (no materialization)
    //   Invert -> Flush(force) bakes the accumulated color-filter chain: +1 FFM, +1 GpuPass, +1 Alloc, +1 Sync
    //             Invert's ApplyToNewTarget (CreateTarget + Open):         +1 GpuPass, +1 Alloc, +1 Sync
    // Totals: FullFrameMaterializations = 2 (one bake per custom item), GpuPasses = 4 (2 bakes + 2 SKSL
    // passes), TargetAllocations = 4 (2 bake targets + 2 SKSL targets), FlushSyncs = 4 (one uncoordinated
    // sync per drawn effect buffer). This is research §0's "≥ 1 materialization + ≥ 1 pass per custom item".
    [Test]
    public void ColorChain_PinsLegacyCostModel()
    {
        PipelineDiagnosticsSnapshot counters = RenderAndSnapshot(
            SceneFixtures.ColorChain(SceneFixtures.ReferenceSize));

        Assert.Multiple(() =>
        {
            Assert.That(counters.FullFrameMaterializations, Is.EqualTo(2), nameof(counters.FullFrameMaterializations));
            Assert.That(counters.GpuPasses, Is.EqualTo(4), nameof(counters.GpuPasses));
            Assert.That(counters.TargetAllocations, Is.EqualTo(4), nameof(counters.TargetAllocations));
            Assert.That(counters.FlushSyncs, Is.EqualTo(4), nameof(counters.FlushSyncs));
        });
    }

    // MixedChain = Blur, Gamma, Invert, DropShadow, LutEffect.
    // Blur and DropShadow are Skia image-filter items; Gamma, Invert and LutEffect (3D cube) are custom-SKSL.
    // Walk over [Blur, Gamma(custom), Invert(custom), DropShadow, Lut(custom)]:
    //   Blur   -> accumulate into the builder
    //   Gamma  -> Flush bakes the accumulated Blur chain:          +1 FFM, +1 GpuPass, +1 Alloc, +1 Sync
    //             Gamma ApplyToNewTarget:                                  +1 GpuPass, +1 Alloc, +1 Sync
    //   Invert -> Flush(force) bakes Gamma's output even with no pending Skia chain (adjacent custom items
    //             each pay a bake, research §0):                   +1 FFM, +1 GpuPass, +1 Alloc, +1 Sync
    //             Invert ApplyToNewTarget:                                 +1 GpuPass, +1 Alloc, +1 Sync
    //   DropShadow -> accumulate into the builder
    //   Lut    -> Flush bakes the accumulated DropShadow chain:    +1 FFM, +1 GpuPass, +1 Alloc, +1 Sync
    //             Lut ApplyToNewTarget:                                    +1 GpuPass, +1 Alloc, +1 Sync
    // Totals: FullFrameMaterializations = 3, GpuPasses = 6, TargetAllocations = 6, FlushSyncs = 6.
    [Test]
    public void MixedChain_PinsLegacyCostModel()
    {
        PipelineDiagnosticsSnapshot counters = RenderAndSnapshot(
            SceneFixtures.MixedChain(SceneFixtures.ReferenceSize));

        Assert.Multiple(() =>
        {
            Assert.That(counters.FullFrameMaterializations, Is.EqualTo(3), nameof(counters.FullFrameMaterializations));
            Assert.That(counters.GpuPasses, Is.EqualTo(6), nameof(counters.GpuPasses));
            Assert.That(counters.TargetAllocations, Is.EqualTo(6), nameof(counters.TargetAllocations));
            Assert.That(counters.FlushSyncs, Is.EqualTo(6), nameof(counters.FlushSyncs));
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

    // T014: a structurally-constant scene rendered K times through one shared pool acquires its intermediates
    // from the same four sites every frame (matching ColorChain's legacy allocation count of 4), but only
    // frame 1 allocates — frames 2..K add ZERO fresh allocations / pool misses (all hits), the steady-state
    // gate for US3 (SC-003).
    //
    // Frame 1 itself allocates fewer than 4 buffers (2 here): the pool reuses within a frame too — a bake
    // target released mid-chain is reused by the next same-size acquire — so misses <= acquire sites. The
    // gate that matters is the cross-frame delta, which is exactly zero.
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
            // Four acquire sites per frame == ColorChain's legacy allocation count (pinned at 4 by
            // ColorChain_PinsLegacyCostModel). Frame 1 allocates each miss exactly once.
            Assert.That(perFrame[0].PoolAcquires, Is.EqualTo(4), "frame 1 acquire sites == legacy allocation count");
            Assert.That(perFrame[0].TargetAllocations, Is.EqualTo(perFrame[0].PoolMisses), "each miss allocates once");
            Assert.That(perFrame[0].PoolMisses, Is.GreaterThan(0).And.LessThanOrEqualTo(4), "frame 1 warms the pool");

            for (int f = 1; f < frames; f++)
            {
                Assert.That(perFrame[f].TargetAllocations, Is.EqualTo(0), $"frame {f + 1} adds no fresh allocations");
                Assert.That(perFrame[f].PoolMisses, Is.EqualTo(0), $"frame {f + 1} has no pool misses");
                Assert.That(perFrame[f].PoolAcquires, Is.EqualTo(4),
                    $"frame {f + 1} acquires the same four buffers, now all hits");
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
