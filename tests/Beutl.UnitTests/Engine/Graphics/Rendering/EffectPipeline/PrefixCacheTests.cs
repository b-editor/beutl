using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;
using Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Gates the pass-prefix output cache (feature 004, contracts/execution-plan.md §C10): a heavy static prefix of an
/// effect chain (here a fixed-sigma Blur) is executed once and then reused across frames while only the animated
/// tail (a Gamma whose amount animates) re-executes — restoring the pre-004 per-child caching granularity the
/// group un-flattening lost. Each frame's output stays parity with a fresh full render. All assertions drive a
/// persistent <see cref="FilterEffectRenderNode"/> over a shared <see cref="RenderTargetPool"/>, mirroring how the
/// renderer reuses one node/pool across frames.
/// </summary>
[NonParallelizable]
[TestFixture]
public class PrefixCacheTests
{
    private static readonly Rect s_bounds = new(0, 0, 128, 96);

    // A group [static Blur, animated Gamma]: the Blur is a SkiaFilterPass (child 0), the Gamma a fused color pass
    // (child 1). Only the Gamma animates, so after warmup the Blur pass is the reusable prefix.
    private static (FilterEffect Root, Gamma Gamma, Blur Blur) MakeBlurGamma()
    {
        var blur = new Blur { Sigma = { CurrentValue = new Size(5, 5) } };
        var gamma = new Gamma();
        var group = new FilterEffectGroup();
        group.Children.Add(blur);
        group.Children.Add(gamma);
        return (group, gamma, blur);
    }

    private static RenderNodeOperation MakeInput()
        => RenderNodeOperation.CreateLambda(
            s_bounds,
            canvas => canvas.DrawRectangle(s_bounds.Deflate(24), Brushes.Resource.White, null),
            hitTest: s_bounds.Contains);

    // ---- The headline regression gate --------------------------------------------------------------------
    //
    // Six frames of [static Blur, animated Gamma]. Frames 0..2 warm the per-child stability tracker; frame 3 first
    // sees the Blur child stable for the engagement threshold (3 frames) and CAPTURES the Blur pass output during a
    // full execution; frames 4..5 RESUME from the Gamma pass with the cached Blur buffer as input, so the Blur pass
    // neither draws nor allocates. GpuPasses therefore drops from 2 (Blur + Gamma) to 1 (Gamma only) after warmup and
    // PrefixCacheHits increments, while every frame stays parity with a fresh full render.
    //
    // BEFORE the C10 fix this failed: with the whole group one cache unit, the animated Gamma marked the node changed
    // every frame, the outer RenderNodeCache never engaged, and the Blur pass re-executed every frame — GpuPasses
    // stayed 2 for all six frames and there was no prefix reuse.
    [Test]
    public void StaticPrefix_AnimatedTail_ReusesPrefixAfterWarmup()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            ProgramCache.Clear();
            const int frames = 6;
            var (root, gamma, _) = MakeBlurGamma();
            var resource = (FilterEffect.Resource)root.ToResource(CompositionContext.Default);
            using var node = new FilterEffectRenderNode(resource);
            var diagnostics = new PipelineDiagnostics();
            using var pool = new RenderTargetPool();

            var snaps = new PipelineDiagnosticsSnapshot[frames];
            for (int f = 0; f < frames; f++)
            {
                snaps[f] = StepAndAssertParity(node, resource, root, gamma, diagnostics, pool, f, f);
            }

            Assert.Multiple(() =>
            {
                for (int f = 0; f <= 3; f++)
                {
                    Assert.That(snaps[f].GpuPasses, Is.EqualTo(2), $"frame {f}: warmup/capture executes Blur + Gamma");
                    Assert.That(snaps[f].PrefixCacheHits, Is.EqualTo(0), $"frame {f}: no reuse before capture");
                }

                for (int f = 4; f < frames; f++)
                {
                    Assert.That(snaps[f].GpuPasses, Is.EqualTo(1), $"frame {f}: only the Gamma tail executes");
                    Assert.That(snaps[f].PrefixCacheHits, Is.EqualTo(1), $"frame {f}: the Blur prefix is reused");
                    Assert.That(snaps[f].TargetAllocations, Is.EqualTo(0), $"frame {f}: the skipped Blur allocates nothing");
                }
            });
        });
    }

    // ---- Invalidation exactness --------------------------------------------------------------------------
    //
    // Mutating the static child's parameter mid-run must invalidate the cached prefix, re-execute fully, and re-warm.
    [Test]
    public void StaticChildMutation_InvalidatesAndReWarms()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            ProgramCache.Clear();
            const int frames = 11;
            const int mutateAt = 6;
            var (root, gamma, blur) = MakeBlurGamma();
            var resource = (FilterEffect.Resource)root.ToResource(CompositionContext.Default);
            using var node = new FilterEffectRenderNode(resource);
            var diagnostics = new PipelineDiagnostics();
            using var pool = new RenderTargetPool();

            var snaps = new PipelineDiagnosticsSnapshot[frames];
            for (int f = 0; f < frames; f++)
            {
                if (f == mutateAt)
                    blur.Sigma.CurrentValue = new Size(7, 7);

                snaps[f] = Step(node, resource, root, gamma, diagnostics, pool, f, 1f);
            }

            Assert.Multiple(() =>
            {
                Assert.That(snaps[5].PrefixCacheHits, Is.EqualTo(1), "frame 5 reuses the prefix before the mutation");
                Assert.That(snaps[mutateAt].GpuPasses, Is.EqualTo(2), "the mutation frame re-executes the Blur pass");
                Assert.That(snaps[mutateAt].PrefixCacheHits, Is.EqualTo(0), "the mutation invalidates the cached prefix");
                // Re-warm: frames 6..8 rebuild stability, frame 9 re-captures, frame 10 resumes again.
                Assert.That(snaps[10].GpuPasses, Is.EqualTo(1), "after re-warming the prefix is reused again");
                Assert.That(snaps[10].PrefixCacheHits, Is.EqualTo(1), "the re-warmed prefix engages after three stable frames");
            });
        });
    }

    // Changing the resolved working scale (via output scale) changes the cached prefix's density, so it must
    // invalidate and re-warm rather than replay a differently-scaled buffer.
    [Test]
    public void WorkingScaleChange_InvalidatesAndReWarms()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            ProgramCache.Clear();
            const int frames = 11;
            const int scaleAt = 6;
            var (root, gamma, _) = MakeBlurGamma();
            var resource = (FilterEffect.Resource)root.ToResource(CompositionContext.Default);
            using var node = new FilterEffectRenderNode(resource);
            var diagnostics = new PipelineDiagnostics();
            using var pool = new RenderTargetPool();

            var snaps = new PipelineDiagnosticsSnapshot[frames];
            for (int f = 0; f < frames; f++)
            {
                float outputScale = f >= scaleAt ? 2f : 1f;
                snaps[f] = Step(node, resource, root, gamma, diagnostics, pool, f, outputScale);
            }

            Assert.Multiple(() =>
            {
                Assert.That(snaps[5].PrefixCacheHits, Is.EqualTo(1), "frame 5 reuses the prefix at the original scale");
                Assert.That(snaps[scaleAt].GpuPasses, Is.EqualTo(2), "the scale change re-executes the whole plan");
                Assert.That(snaps[scaleAt].PrefixCacheHits, Is.EqualTo(0), "the scale change invalidates the cached prefix");
                Assert.That(snaps[10].GpuPasses, Is.EqualTo(1), "the prefix re-engages at the new scale after re-warming");
                Assert.That(snaps[10].PrefixCacheHits, Is.EqualTo(1), "the re-warmed prefix engages at the new scale");
            });
        });
    }

    // Input-subtree instability (a drawable below the effect changed) must block reuse: the same predicate the node
    // cache uses (CanCacheRecursiveChildrenOnly) gates engagement, driven here by a stub child node whose stability
    // count is toggled.
    [Test]
    public void InputSubtreeInstability_InvalidatesAndReWarms()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            ProgramCache.Clear();
            const int frames = 11;
            const int unstableAt = 6;
            var (root, gamma, _) = MakeBlurGamma();
            var resource = (FilterEffect.Resource)root.ToResource(CompositionContext.Default);
            using var node = new FilterEffectRenderNode(resource);
            // A stub input-subtree child; its render-count is the CanCacheRecursiveChildrenOnly signal.
            var child = new ContainerRenderNode();
            node.AddChild(child);
            var diagnostics = new PipelineDiagnostics();
            using var pool = new RenderTargetPool();

            var snaps = new PipelineDiagnosticsSnapshot[frames];
            for (int f = 0; f < frames; f++)
            {
                // Stable subtree = count at/above the cache threshold; frame `unstableAt` simulates a change below.
                child.Cache.ReportRenderCount(f == unstableAt ? 0 : 3);
                snaps[f] = Step(node, resource, root, gamma, diagnostics, pool, f, 1f);
            }

            Assert.Multiple(() =>
            {
                Assert.That(snaps[5].PrefixCacheHits, Is.EqualTo(1), "frame 5 reuses the prefix while the subtree is stable");
                Assert.That(snaps[unstableAt].GpuPasses, Is.EqualTo(2), "a changed subtree forces a full re-execution");
                Assert.That(snaps[unstableAt].PrefixCacheHits, Is.EqualTo(0), "a changed subtree invalidates the cached prefix");
                Assert.That(snaps[10].PrefixCacheHits, Is.EqualTo(1), "the prefix re-engages once the subtree is stable again");
            });
        });
    }

    // ---- Pool hygiene ------------------------------------------------------------------------------------
    //
    // The retained prefix buffer holds exactly one live pool lease while engaged; invalidation returns it to the pool
    // and node dispose leaves no live leases (no leak). Lease counts are read between frames, once each frame's ops
    // are disposed.
    [Test]
    public void RetainedPrefix_ReleasesLeaseOnInvalidateAndDispose()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            ProgramCache.Clear();
            var (root, gamma, blur) = MakeBlurGamma();
            var resource = (FilterEffect.Resource)root.ToResource(CompositionContext.Default);
            var node = new FilterEffectRenderNode(resource);
            var diagnostics = new PipelineDiagnostics();
            using var pool = new RenderTargetPool();

            // Warm and engage (frames 0..5): after the resumed frame 5, only the retained prefix buffer is leased.
            for (int f = 0; f <= 5; f++)
                Step(node, resource, root, gamma, diagnostics, pool, f, 1f);

            Assert.That(pool.LiveLeaseCount, Is.EqualTo(1), "the engaged prefix retains exactly one live lease");

            // Invalidate by mutating the static child; the retained buffer returns to the pool.
            blur.Sigma.CurrentValue = new Size(7, 7);
            Step(node, resource, root, gamma, diagnostics, pool, 6, 1f);
            Assert.That(pool.LiveLeaseCount, Is.EqualTo(0), "invalidation returns the retained buffer to the pool");

            // Re-engage, then dispose the node with a live retained lease: it must be released (no leak).
            for (int f = 7; f <= 10; f++)
                Step(node, resource, root, gamma, diagnostics, pool, f, 1f);
            Assert.That(pool.LiveLeaseCount, Is.EqualTo(1), "the prefix re-engages and re-retains one lease");

            node.Dispose();
            Assert.That(pool.LiveLeaseCount, Is.EqualTo(0), "node dispose releases the retained prefix lease");
        });
    }

    // ---- Outer-cache interplay ---------------------------------------------------------------------------
    //
    // A fully static group (nothing animated) must never engage the prefix cache — the outer whole-node cache owns
    // that case (C10 keeps a tail pass out of the prefix). Here the tail Gamma is held constant, so both children
    // stabilize together; the prefix cache declines (it never caches the whole plan) and no reuse is counted.
    [Test]
    public void FullyStaticGroup_DoesNotEngagePrefixCache()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            ProgramCache.Clear();
            const int frames = 8;
            var (root, gamma, _) = MakeBlurGamma();
            gamma.Amount.CurrentValue = 130f;
            var resource = (FilterEffect.Resource)root.ToResource(CompositionContext.Default);
            using var node = new FilterEffectRenderNode(resource);
            var diagnostics = new PipelineDiagnostics();
            using var pool = new RenderTargetPool();

            long totalHits = 0;
            for (int f = 0; f < frames; f++)
            {
                // No animation: gamma stays constant, so both children stabilize and the whole plan would be stable.
                PipelineDiagnosticsSnapshot snap = StepStatic(node, resource, root, diagnostics, pool, f);
                totalHits += snap.PrefixCacheHits;
            }

            Assert.That(totalHits, Is.EqualTo(0),
                "a fully static group never engages the prefix cache; the outer node cache owns it");
        });
    }

    // ---- Drivers -----------------------------------------------------------------------------------------

    private static PipelineDiagnosticsSnapshot Step(
        FilterEffectRenderNode node, FilterEffect.Resource resource, FilterEffect root, Gamma gamma,
        PipelineDiagnostics diagnostics, RenderTargetPool pool, int frame, float outputScale)
    {
        pool.Trim(frame);
        gamma.Amount.CurrentValue = 50f + 2f * frame;
        bool updateOnly = false;
        resource.Update(root, CompositionContext.Default, ref updateOnly);
        node.Update(resource);

        diagnostics.Reset();
        var context = new RenderNodeContext([MakeInput()], outputScale) { Diagnostics = diagnostics, Pool = pool };
        RenderNodeOperation[] ops = node.Process(context);
        PipelineDiagnosticsSnapshot snap = diagnostics.Snapshot();
        RenderNodeOperation.DisposeAll(ops);
        return snap;
    }

    private static PipelineDiagnosticsSnapshot StepStatic(
        FilterEffectRenderNode node, FilterEffect.Resource resource, FilterEffect root,
        PipelineDiagnostics diagnostics, RenderTargetPool pool, int frame)
    {
        pool.Trim(frame);
        bool updateOnly = false;
        resource.Update(root, CompositionContext.Default, ref updateOnly);
        node.Update(resource);

        diagnostics.Reset();
        var context = new RenderNodeContext([MakeInput()]) { Diagnostics = diagnostics, Pool = pool };
        RenderNodeOperation[] ops = node.Process(context);
        PipelineDiagnosticsSnapshot snap = diagnostics.Snapshot();
        RenderNodeOperation.DisposeAll(ops);
        return snap;
    }

    private static PipelineDiagnosticsSnapshot StepAndAssertParity(
        FilterEffectRenderNode node, FilterEffect.Resource resource, FilterEffect root, Gamma gamma,
        PipelineDiagnostics diagnostics, RenderTargetPool pool, int frame, int parityFrame)
    {
        pool.Trim(frame);
        gamma.Amount.CurrentValue = 50f + 2f * frame;
        bool updateOnly = false;
        resource.Update(root, CompositionContext.Default, ref updateOnly);
        node.Update(resource);

        diagnostics.Reset();
        var context = new RenderNodeContext([MakeInput()]) { Diagnostics = diagnostics, Pool = pool };
        RenderNodeOperation[] ops = node.Process(context);
        PipelineDiagnosticsSnapshot snap = diagnostics.Snapshot();

        using Bitmap actual = Rasterize(ops);
        using Bitmap fresh = RenderFresh(parityFrame);
        AssertMatches(fresh, actual, $"frame {frame}");
        return snap;
    }

    // A brand-new node with no pool renders a genuine full execution (no prefix engagement) — the parity control.
    private static Bitmap RenderFresh(int frame)
    {
        var (root, gamma, _) = MakeBlurGamma();
        gamma.Amount.CurrentValue = 50f + 2f * frame;
        var resource = (FilterEffect.Resource)root.ToResource(CompositionContext.Default);
        using var node = new FilterEffectRenderNode(resource);
        var context = new RenderNodeContext([MakeInput()]);
        RenderNodeOperation[] ops = node.Process(context);
        return Rasterize(ops);
    }

    private static Bitmap Rasterize(RenderNodeOperation[] ops)
    {
        Rect bounds = ops.Aggregate<RenderNodeOperation, Rect>(default, (u, op) => u.Union(op.Bounds));
        PixelRect rect = PixelRect.FromRect(bounds);
        int w = Math.Max(1, rect.Width);
        int h = Math.Max(1, rect.Height);
        using RenderTarget target = RenderTarget.Create(w, h)
            ?? throw new InvalidOperationException("RenderTarget.Create returned null.");
        using (var canvas = new ImmediateCanvas(target, 1f, logicalSize: bounds.Size))
        {
            canvas.Clear();
            using (canvas.PushTransform(Matrix.CreateTranslation(-bounds.X, -bounds.Y)))
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
