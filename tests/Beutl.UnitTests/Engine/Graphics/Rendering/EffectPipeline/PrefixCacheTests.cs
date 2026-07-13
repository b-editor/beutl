using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.Serialization;
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
    private static (FilterEffect Root, Gamma Gamma, Blur Blur) MakeBlurGamma(float sigma = 5f)
    {
        var blur = new Blur { Sigma = { CurrentValue = new Size(sigma, sigma) } };
        var gamma = new Gamma();
        var group = new FilterEffectGroup();
        group.Children.Add(blur);
        group.Children.Add(gamma);
        return (group, gamma, blur);
    }

    // A group [static LutEffect (fused color, child 0), animated Blur (SkiaFilterPass, child 1)]: after warmup the
    // LUT pass is the reusable prefix. The LUT's output depends on its nested CubeSource, exercising nested-object
    // version propagation; the Blur tail animates so only the LUT stabilizes.
    private static (FilterEffect Root, Blur Blur, LutEffect Lut) MakeLutBlur(CubeSource cube, float sigma)
    {
        var lut = new LutEffect();
        lut.Source.CurrentValue = cube;
        var blur = new Blur { Sigma = { CurrentValue = new Size(sigma, sigma) } };
        var group = new FilterEffectGroup();
        group.Children.Add(lut);
        group.Children.Add(blur);
        return (group, blur, lut);
    }

    // A group [static Blur (SkiaFilterPass, child 0), a color filter that throws at execution (child 1)]: the Blur
    // forms the capturable prefix while the throwing tail is a distinct later fused pass. The tail's resource bumps
    // its Version on every update, keeping child 1 perpetually unstable so only the Blur stabilizes into the prefix.
    private static FilterEffect MakeBlurThrowing(float sigma = 5f)
    {
        var blur = new Blur { Sigma = { CurrentValue = new Size(sigma, sigma) } };
        var group = new FilterEffectGroup();
        group.Children.Add(blur);
        group.Children.Add(new ThrowingColorEffect());
        return group;
    }

    // Distinct per frame so the Blur child's resource Version bumps every frame (keeping the tail unstable).
    private static float BlurSigma(int frame) => 3f + 0.2f * frame;

    // A 2×2×2 identity .cube LUT (output = input), the swap counterpart to SceneFixtures' channel-inverting LUT.
    private static CubeSource MakeIdentityLutSource()
    {
        const string cubeText =
            """
            TITLE "prefix identity"
            LUT_3D_SIZE 2
            DOMAIN_MIN 0 0 0
            DOMAIN_MAX 1 1 1
            0 0 0
            1 0 0
            0 1 0
            1 1 0
            0 0 1
            1 0 1
            0 1 1
            1 1 1
            """;
        var source = new CubeSource();
        source.ReadFrom(UriHelper.CreateBase64DataUri("text/plain", System.Text.Encoding.ASCII.GetBytes(cubeText)));
        return source;
    }

    private static RenderNodeOperation MakeInput() => MakeInput(s_bounds);

    private static RenderNodeOperation MakeInput(Rect bounds)
        => RenderNodeOperation.CreateLambda(
            bounds,
            canvas => canvas.DrawRectangle(bounds.Deflate(24), Brushes.Resource.White, null),
            hitTest: bounds.Contains);

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
            using var node = new PlanFilterEffectRenderNode(resource);
            var diagnostics = new PipelineDiagnostics();
            using var pool = new RenderTargetPool();

            var snaps = new PipelineDiagnosticsSnapshot[frames];
            for (int f = 0; f < frames; f++)
            {
                snaps[f] = StepAndParity(node, resource, root, gamma, diagnostics, pool, f, 1f, 5f);
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

    [Test]
    public void AuxiliaryFullBoundsPull_DoesNotInvalidateFrameRoiPrefix()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var (root, gamma, _) = MakeBlurGamma();
            using var resource = (FilterEffect.Resource)root.ToResource(CompositionContext.Default);
            using var node = new PlanFilterEffectRenderNode(resource);
            using var pool = new RenderTargetPool();
            var diagnostics = new PipelineDiagnostics();
            var frameRoi = new Rect(8, 6, 72, 54);

            PipelineDiagnosticsSnapshot ProcessFrame(int frame)
            {
                gamma.Amount.CurrentValue = 50f + frame;
                bool updateOnly = false;
                resource.Update(root, CompositionContext.Default, ref updateOnly);
                node.Update(resource);
                diagnostics.Reset();
                var context = new RenderNodeContext([MakeInput()])
                {
                    Diagnostics = diagnostics,
                    Pool = pool,
                    RequestedBounds = frameRoi,
                };
                RenderNodeOperation.DisposeAll(node.Process(context));
                return diagnostics.Snapshot();
            }

            for (int frame = 0; frame < 6; frame++)
                ProcessFrame(frame);
            Assert.That(ProcessFrame(6).PrefixCacheHits, Is.EqualTo(1), "sanity: the frame ROI prefix is warm");

            var auxiliary = new RenderNodeContext([MakeInput()])
            {
                Pool = pool,
                RequestedBounds = Rect.Invalid,
                IsAuxiliaryPull = true,
            };
            RenderNodeOperation.DisposeAll(node.Process(auxiliary));

            Assert.That(ProcessFrame(7).PrefixCacheHits, Is.EqualTo(1),
                "a full-bounds auxiliary pull must not evict the retained frame-ROI prefix");
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
            using var node = new PlanFilterEffectRenderNode(resource);
            var diagnostics = new PipelineDiagnostics();
            using var pool = new RenderTargetPool();

            var snaps = new PipelineDiagnosticsSnapshot[frames];
            for (int f = 0; f < frames; f++)
            {
                if (f == mutateAt)
                    blur.Sigma.CurrentValue = new Size(7, 7);

                // The final resume frame asserts pixel parity against a fresh render of the POST-mutation chain
                // (sigma 7): a prefix that re-retained the stale pre-mutation blur (sigma 5) would replay it here and
                // the parity gate would fail, so the counters alone cannot hide a stale-buffer regression.
                snaps[f] = f == frames - 1
                    ? StepAndParity(node, resource, root, gamma, diagnostics, pool, f, 1f, 7f)
                    : Step(node, resource, root, gamma, diagnostics, pool, f, 1f);
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
            using var node = new PlanFilterEffectRenderNode(resource);
            var diagnostics = new PipelineDiagnostics();
            using var pool = new RenderTargetPool();

            var snaps = new PipelineDiagnosticsSnapshot[frames];
            for (int f = 0; f < frames; f++)
            {
                float outputScale = f >= scaleAt ? 2f : 1f;
                // The final resume frame runs at the new scale; parity against a fresh render at that same scale
                // catches a prefix that replayed a buffer captured at the old density.
                snaps[f] = f == frames - 1
                    ? StepAndParity(node, resource, root, gamma, diagnostics, pool, f, outputScale, 5f)
                    : Step(node, resource, root, gamma, diagnostics, pool, f, outputScale);
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
            using var node = new PlanFilterEffectRenderNode(resource);
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
                // The final resume frame asserts parity so a re-engaged prefix still matches a fresh full render.
                snaps[f] = f == frames - 1
                    ? StepAndParity(node, resource, root, gamma, diagnostics, pool, f, 1f, 5f)
                    : Step(node, resource, root, gamma, diagnostics, pool, f, 1f);
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
            var node = new PlanFilterEffectRenderNode(resource);
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

    // A capture frame whose LATER pass throws (a real Skia/shader fault, not the preview allocation-drop path) must
    // still release the ref the capture pass shallow-copied into the sink. Group [static Blur (child 0), a throwing
    // color filter (child 1, kept perpetually unstable)]: frames 0..2 warm the Blur's stability, frame 3 captures the
    // Blur prefix and THEN the color pass throws — the path that used to strand one full-frame pooled lease per
    // re-capture. After the throw the pool must report zero live leases.
    //
    // BEFORE the fix this failed: ExecuteAndCapture let the exception escape after the sink had taken a ShallowCopy of
    // the Blur pass's pooled buffer, StoreCaptured never ran, and that ref was never released (LiveLeaseCount == 1).
    [Test]
    public void CaptureFrame_LaterPassThrows_ReleasesCapturedLease()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            ProgramCache.Clear();
            FilterEffect root = MakeBlurThrowing();
            var resource = (FilterEffect.Resource)root.ToResource(CompositionContext.Default);
            using var node = new PlanFilterEffectRenderNode(resource);
            var diagnostics = new PipelineDiagnostics();
            using var pool = new RenderTargetPool();

            // Frames 0..2 warm the Blur child's stability (each frame the throwing tail faults, which is expected);
            // frame 3 captures the Blur prefix and then the tail throws AFTER the capture.
            for (int f = 0; f <= 3; f++)
            {
                pool.Trim(f);
                bool updateOnly = false;
                resource.Update(root, CompositionContext.Default, ref updateOnly);
                node.Update(resource);

                diagnostics.Reset();
                var context = new RenderNodeContext([MakeInput()]) { Diagnostics = diagnostics, Pool = pool };
                Assert.Throws<InvalidOperationException>(() => node.Process(context),
                    $"frame {f}: the throwing tail pass was expected to fault");
            }

            Assert.That(pool.LiveLeaseCount, Is.EqualTo(0),
                "a capture frame whose later pass throws must release the captured prefix's pooled lease");
        });
    }

    // StoreCaptured runs after the executor has returned both the final operations and a shallow-copied prefix lease.
    // If cache adoption throws, neither ownership set has reached the caller and both must be swept before rethrowing.
    [Test]
    public void CaptureFrame_StoreCapturedThrows_ReleasesResultAndCapturedLease()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            ProgramCache.Clear();
            var (root, gamma, _) = MakeBlurGamma();
            var resource = (FilterEffect.Resource)root.ToResource(CompositionContext.Default);
            using var node = new PlanFilterEffectRenderNode(resource);
            var diagnostics = new PipelineDiagnostics();
            using var pool = new RenderTargetPool();

            for (int f = 0; f < 3; f++)
                Step(node, resource, root, gamma, diagnostics, pool, f, 1f);

            var injected = new InvalidOperationException("prefix adoption failed");
            PlanFilterEffectRenderNode.SetBeforeStoreCapturedForTest(() => throw injected);
            try
            {
                InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(
                    () => Step(node, resource, root, gamma, diagnostics, pool, 3, 1f));
                Assert.That(actual, Is.SameAs(injected));
                Assert.That(pool.LiveLeaseCount, Is.Zero,
                    "a failed capture adoption must release both the final result and the sink's shallow copy");
            }
            finally
            {
                PlanFilterEffectRenderNode.SetBeforeStoreCapturedForTest(null);
            }
        });
    }

    // Disabling the effect after the prefix has engaged must release the retained cross-frame lease, not pin it until
    // node dispose: the disabled branch bypasses execution entirely, so nothing else would ever return that buffer to
    // the pool while the effect stays off.
    //
    // BEFORE the fix this failed: the disabled/null early return skipped straight to context.Input without releasing
    // the retained prefix, so the lease stayed held (LiveLeaseCount == 1) outside every pool budget.
    [Test]
    public void DisabledEffect_ReleasesRetainedPrefixLease()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            ProgramCache.Clear();
            var (root, gamma, _) = MakeBlurGamma();
            var resource = (FilterEffect.Resource)root.ToResource(CompositionContext.Default);
            using var node = new PlanFilterEffectRenderNode(resource);
            using var pool = new RenderTargetPool();
            var diagnostics = new PipelineDiagnostics();

            // Warm and engage (frames 0..5): after the resumed frame 5, only the retained prefix buffer is leased.
            for (int f = 0; f <= 5; f++)
                Step(node, resource, root, gamma, diagnostics, pool, f, 1f);
            Assert.That(pool.LiveLeaseCount, Is.EqualTo(1), "the engaged prefix retains exactly one live lease");

            // Disable the effect and process once: the disabled branch must release the retained prefix.
            root.IsEnabled = false;
            Step(node, resource, root, gamma, diagnostics, pool, 6, 1f);
            Assert.That(pool.LiveLeaseCount, Is.EqualTo(0),
                "disabling the effect must return the retained prefix buffer to the pool");
        });
    }

    [Test]
    public void DisabledEffect_PrefixReleaseFailure_DisposesBypassedInputs()
    {
        var effect = new Blur { IsEnabled = false };
        using var resource = (FilterEffect.Resource)effect.ToResource(CompositionContext.Default);
        using var node = new PlanFilterEffectRenderNode(resource);
        bool inputDisposed = false;
        RenderNodeOperation input = RenderNodeOperation.CreateLambda(
            new Rect(0, 0, 10, 10),
            static _ => { },
            onDispose: () => inputDisposed = true);
        var injected = new InvalidOperationException("prefix release failed");

        PlanFilterEffectRenderNode.SetBeforeDisabledPrefixReleaseForTest(() => throw injected);
        try
        {
            InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(
                () => node.Process(new RenderNodeContext([input])));
            Assert.Multiple(() =>
            {
                Assert.That(actual, Is.SameAs(injected));
                Assert.That(inputDisposed, Is.True,
                    "the disabled early-return path must sweep its input when prefix cleanup fails");
            });
        }
        finally
        {
            PlanFilterEffectRenderNode.SetBeforeDisabledPrefixReleaseForTest(null);
        }
    }

    // Once the OUTER RenderNodeCache engages over the effect node's subtree, Process stops running, so the prefix
    // cache's retained cross-frame lease — invisible to the pool's idle-eviction and byte-cap — must be released as
    // the cache engages, not pinned until node dispose. Drive the animate-then-hold sequence: animate the Gamma tail
    // until the prefix engages and retains a lease, then engage the outer cache over an ancestor container and assert
    // the live-lease count drops back to zero.
    //
    // BEFORE the fix this failed: outer-cache engagement never released the retained prefix, so the lease stayed held
    // (LiveLeaseCount == 1) outside every pool budget until the node was disposed.
    [Test]
    public void OuterNodeCacheEngages_ReleasesRetainedPrefixLease()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            ProgramCache.Clear();
            var (root, gamma, _) = MakeBlurGamma();
            var resource = (FilterEffect.Resource)root.ToResource(CompositionContext.Default);
            var effectNode = new PlanFilterEffectRenderNode(resource);

            // The effect node's input subtree (one child node emitting the source op). Kept stable so the prefix
            // cache's input-subtree gate is satisfied during warmup, and cacheable so the outer cache can later
            // engage over the whole subtree.
            var inputNode = new StubInputNode();
            effectNode.AddChild(inputNode);

            // An ancestor container: a drawable's render subtree caches at its top node, not at the effect node
            // itself, so engaging here exercises the descendant-release recursion (the real production shape).
            var container = new ContainerRenderNode();
            container.AddChild(effectNode);

            var diagnostics = new PipelineDiagnostics();
            using var pool = new RenderTargetPool();

            // Phase A — animate the tail until the prefix cache engages and retains a pooled lease. The outer node
            // cache cannot engage here: the animated tail keeps the node changed every frame.
            long hits = 0;
            for (int f = 0; f <= 5; f++)
            {
                inputNode.Cache.ReportRenderCount(RenderNodeCache.Count);
                hits += Step(effectNode, resource, root, gamma, diagnostics, pool, f, 1f).PrefixCacheHits;
            }

            Assert.That(hits, Is.GreaterThan(0), "the prefix cache never engaged during warmup");
            Assert.That(pool.LiveLeaseCount, Is.EqualTo(1), "the engaged prefix retains exactly one pooled lease");

            // Phase B — everything holds stable; the whole subtree becomes cacheable and the outer node cache engages
            // over the container. From here Process never runs on the effect node again.
            container.Cache.ReportRenderCount(RenderNodeCache.Count);
            effectNode.Cache.ReportRenderCount(RenderNodeCache.Count);
            inputNode.Cache.ReportRenderCount(RenderNodeCache.Count);
            RenderNodeCacheHelper.MakeCache(container, RenderCacheOptions.Default, 1f);

            Assert.That(container.Cache.IsCached, Is.True, "the outer node cache did not engage");
            Assert.That(pool.LiveLeaseCount, Is.EqualTo(0),
                "outer-cache engagement must release the effect node's retained prefix lease");

            container.Dispose();
        });
    }

    // ---- Tail-driven ROI invalidation ---------------------------------------------------------------------
    //
    // A group [static Blur, Clipping whose Left animates]: the structural key and the input signature are stable
    // and the Blur child never changes, but the animated clip moves the Blur pass's backward-resolved ROI every
    // frame. A resume from the buffer captured for the previous (narrower) ROI would feed the tail a stale,
    // differently-cropped blur — newly exposed regions would render empty. The prefix cache must release and
    // re-capture instead of resuming, and the output must stay parity with a fresh full render.
    [Test]
    public void TailRoiChange_ReleasesRetainedPrefix_NoStaleResume()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            ProgramCache.Clear();
            const int frames = 6;
            var (root, clip) = MakeBlurClip(5f, ClipLeft(0));
            var resource = (FilterEffect.Resource)root.ToResource(CompositionContext.Default);
            using var node = new PlanFilterEffectRenderNode(resource);
            var diagnostics = new PipelineDiagnostics();
            using var pool = new RenderTargetPool();

            var snaps = new PipelineDiagnosticsSnapshot[frames];
            for (int f = 0; f < frames; f++)
            {
                pool.Trim(f);
                clip.Left.CurrentValue = ClipLeft(f);
                bool updateOnly = false;
                resource.Update(root, CompositionContext.Default, ref updateOnly);
                node.Update(resource);

                diagnostics.Reset();
                var context = new RenderNodeContext([MakeInput()]) { Diagnostics = diagnostics, Pool = pool };
                RenderNodeOperation[] ops = node.Process(context);
                snaps[f] = diagnostics.Snapshot();

                if (f == frames - 1)
                {
                    // The last frame's kept region extends far past the region captured at frame 3, so a stale
                    // resume cannot pass this parity gate even if the counters were gamed.
                    using Bitmap actual = Rasterize(ops);
                    using Bitmap fresh = RenderFreshBlurClip(5f, ClipLeft(f));
                    AssertMatches(fresh, actual, $"clip frame {f}");
                }
                else
                {
                    RenderNodeOperation.DisposeAll(ops);
                }
            }

            Assert.Multiple(() =>
            {
                for (int f = 4; f < frames; f++)
                {
                    Assert.That(snaps[f].PrefixCacheHits, Is.EqualTo(0),
                        $"frame {f}: a tail-ROI-shifted prefix must not resume from the stale buffer");
                    Assert.That(snaps[f].GpuPasses, Is.EqualTo(2),
                        $"frame {f}: both passes re-execute after the ROI shift releases the prefix");
                }
            });
        });
    }

    // A group [static Blur (SkiaFilterPass, child 0), Clipping (GeometryPass, child 1) whose Left animates].
    private static (FilterEffect Root, Clipping Clip) MakeBlurClip(float sigma, float left)
    {
        var blur = new Blur { Sigma = { CurrentValue = new Size(sigma, sigma) } };
        var clip = new Clipping();
        clip.Left.CurrentValue = left;
        var group = new FilterEffectGroup();
        group.Children.Add(blur);
        group.Children.Add(clip);
        return (group, clip);
    }

    // Decreasing per frame, so each frame's kept region EXPANDS past the previously captured ROI (a shrinking clip
    // would hide the bug: the stale buffer would still cover the smaller region).
    private static float ClipLeft(int frame) => 48f - 8f * frame;

    private static Bitmap RenderFreshBlurClip(float sigma, float left)
    {
        var (root, _) = MakeBlurClip(sigma, left);
        var resource = (FilterEffect.Resource)root.ToResource(CompositionContext.Default);
        using var node = new PlanFilterEffectRenderNode(resource);
        var context = new RenderNodeContext([MakeInput()]);
        return Rasterize(node.Process(context));
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
            using var node = new PlanFilterEffectRenderNode(resource);
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

    // ---- Nested-object invalidation ----------------------------------------------------------------------
    //
    // A group [static LutEffect, animated Blur]: the LUT is a fused color pass (child 0, the reusable prefix) whose
    // output depends on a NESTED object — its CubeSource. Swapping that CubeSource mid-run must invalidate the cached
    // prefix, which relies entirely on the LutEffect resource's Version bumping when a nested EngineObject swaps
    // (EngineObject.CompareAndUpdateObject). Both cubes are 3D size-2, so the SKSL snippet and the StructuralKey are
    // identical across the swap — the only signal that can invalidate the prefix is the per-child resource Version,
    // so this pins that propagation. The Blur tail animates (parameter-only), keeping child 1 perpetually unstable so
    // the LUT alone forms the leading stable prefix.
    [Test]
    public void StaticNestedObjectSwap_InvalidatesAndReWarms()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            ProgramCache.Clear();
            const int frames = 11;
            const int swapAt = 6;
            var (root, blur, lut) = MakeLutBlur(SceneFixtures.CreateInvertLutSource(), BlurSigma(0));
            var resource = (FilterEffect.Resource)root.ToResource(CompositionContext.Default);
            using var node = new PlanFilterEffectRenderNode(resource);
            var diagnostics = new PipelineDiagnostics();
            using var pool = new RenderTargetPool();

            CubeSource swapped = MakeIdentityLutSource();
            var snaps = new PipelineDiagnosticsSnapshot[frames];
            for (int f = 0; f < frames; f++)
            {
                if (f == swapAt)
                    lut.Source.CurrentValue = swapped;

                // The final resume frame asserts pixel parity against a fresh render of the POST-swap chain: a prefix
                // that replayed the stale pre-swap LUT (invert) instead of the swapped one (identity) would diverge
                // sharply here, so a broken nested-version propagation cannot pass on counters alone.
                snaps[f] = f == frames - 1
                    ? StepLutAndParity(node, resource, root, blur, diagnostics, pool, f, swapped)
                    : StepLut(node, resource, root, blur, diagnostics, pool, f);
            }

            Assert.Multiple(() =>
            {
                Assert.That(snaps[5].PrefixCacheHits, Is.EqualTo(1), "frame 5 reuses the LUT prefix before the swap");
                Assert.That(snaps[swapAt].GpuPasses, Is.EqualTo(2), "the CubeSource swap re-executes the LUT pass");
                Assert.That(snaps[swapAt].PrefixCacheHits, Is.EqualTo(0), "the nested-object swap invalidates the cached prefix");
                Assert.That(snaps[10].GpuPasses, Is.EqualTo(1), "after re-warming the LUT prefix is reused again");
                Assert.That(snaps[10].PrefixCacheHits, Is.EqualTo(1), "the re-warmed prefix engages after three stable frames");
            });
        });
    }

    // ---- Input-bounds signature (exact) ------------------------------------------------------------------
    //
    // A change in the bounds of the ops feeding the plan changes the cached prefix's pixels, so it must invalidate.
    // The signature is compared exactly (not via a 32-bit hash), so this also guards the exact-comparison path.
    [Test]
    public void InputBoundsChange_InvalidatesAndReWarms()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            ProgramCache.Clear();
            const int frames = 11;
            const int changeAt = 6;
            var (root, gamma, _) = MakeBlurGamma();
            var resource = (FilterEffect.Resource)root.ToResource(CompositionContext.Default);
            using var node = new PlanFilterEffectRenderNode(resource);
            var diagnostics = new PipelineDiagnostics();
            using var pool = new RenderTargetPool();

            var widened = new Rect(0, 0, 160, 96);
            var snaps = new PipelineDiagnosticsSnapshot[frames];
            for (int f = 0; f < frames; f++)
            {
                // Frame `changeAt` onward feeds a wider input rect, so the signature changes once and then holds.
                Rect inputBounds = f >= changeAt ? widened : s_bounds;
                snaps[f] = StepBounds(node, resource, root, gamma, diagnostics, pool, f, inputBounds);
            }

            Assert.Multiple(() =>
            {
                Assert.That(snaps[5].PrefixCacheHits, Is.EqualTo(1), "frame 5 reuses the prefix at the original input bounds");
                Assert.That(snaps[changeAt].GpuPasses, Is.EqualTo(2), "a changed input-bounds signature forces a full re-execution");
                Assert.That(snaps[changeAt].PrefixCacheHits, Is.EqualTo(0), "the changed input bounds invalidate the cached prefix");
                Assert.That(snaps[10].PrefixCacheHits, Is.EqualTo(1), "the prefix re-engages after re-warming at the new bounds");
            });
        });
    }

    // The prefix signature includes the render-scale policy: a deferred child bakes at the describe-time
    // MaxWorkingScale (DisplacementMapTransform's map), so a prefix captured under another policy would resume
    // wrongly-scaled pixels even when workingScale itself is unchanged (a supply-pinned w under a zoom change).
    [Test]
    public void MaxWorkingScaleChange_InvalidatesTheCapturedPrefix()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            ProgramCache.Clear();
            var (root, gamma, _) = MakeBlurGamma();
            var resource = (FilterEffect.Resource)root.ToResource(CompositionContext.Default);
            using var node = new PlanFilterEffectRenderNode(resource);
            var diagnostics = new PipelineDiagnostics();
            using var pool = new RenderTargetPool();

            for (int f = 0; f < 6; f++)
                Step(node, resource, root, gamma, diagnostics, pool, f, 1f);
            PipelineDiagnosticsSnapshot warmed = Step(node, resource, root, gamma, diagnostics, pool, 6, 1f);
            Assert.That(warmed.PrefixCacheHits, Is.EqualTo(1), "sanity: the prefix is warmed and reused");

            PipelineDiagnosticsSnapshot changed = StepWithMaxScale(
                node, resource, root, gamma, diagnostics, pool, frame: 7, maxWorkingScale: 4f);
            Assert.That(changed.PrefixCacheHits, Is.EqualTo(0),
                "a MaxWorkingScale change must invalidate the captured prefix, not resume it");
        });
    }

    // A caller that disabled render caching (RenderCacheOptions.Disabled — the delivery render paths) must never
    // be served from a retained prefix, even on the pooled path: the processor seeds the flag into the context and
    // the prefix cache only engages when it is set.
    [Test]
    public void DisabledRenderCaching_NeverEngagesThePrefixCache()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            ProgramCache.Clear();
            var (root, gamma, _) = MakeBlurGamma();
            var resource = (FilterEffect.Resource)root.ToResource(CompositionContext.Default);
            using var node = new PlanFilterEffectRenderNode(resource);
            var diagnostics = new PipelineDiagnostics();
            using var pool = new RenderTargetPool();

            for (int f = 0; f < 8; f++)
            {
                pool.Trim(f);
                gamma.Amount.CurrentValue = 50f + 2f * f;
                bool updateOnly = false;
                resource.Update(root, CompositionContext.Default, ref updateOnly);
                node.Update(resource);

                diagnostics.Reset();
                var context = new RenderNodeContext([MakeInput()])
                {
                    IsRenderCacheEnabled = false,
                    Diagnostics = diagnostics,
                    Pool = pool,
                };
                RenderNodeOperation[] ops = node.Process(context);
                RenderNodeOperation.DisposeAll(ops);

                Assert.That(diagnostics.Snapshot().PrefixCacheHits, Is.Zero,
                    $"frame {f}: disabled render caching must never resume from a retained prefix");
            }
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

    private static PipelineDiagnosticsSnapshot StepWithMaxScale(
        FilterEffectRenderNode node, FilterEffect.Resource resource, FilterEffect root, Gamma gamma,
        PipelineDiagnostics diagnostics, RenderTargetPool pool, int frame, float maxWorkingScale)
    {
        pool.Trim(frame);
        gamma.Amount.CurrentValue = 50f + 2f * frame;
        bool updateOnly = false;
        resource.Update(root, CompositionContext.Default, ref updateOnly);
        node.Update(resource);

        diagnostics.Reset();
        var context = new RenderNodeContext([MakeInput()], outputScale: 1f, maxWorkingScale)
        {
            Diagnostics = diagnostics,
            Pool = pool,
        };
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

    // Steps one frame (Gamma animates with the frame) and additionally asserts the rendered output is pixel-parity
    // with a fresh full render of the Blur(sigma)/Gamma chain at the same output scale — so a resumed frame that
    // replayed a stale or wrongly-scaled prefix buffer is caught, not just the counters.
    private static PipelineDiagnosticsSnapshot StepAndParity(
        FilterEffectRenderNode node, FilterEffect.Resource resource, FilterEffect root, Gamma gamma,
        PipelineDiagnostics diagnostics, RenderTargetPool pool, int frame, float outputScale, float sigma)
    {
        pool.Trim(frame);
        float gammaAmount = 50f + 2f * frame;
        gamma.Amount.CurrentValue = gammaAmount;
        bool updateOnly = false;
        resource.Update(root, CompositionContext.Default, ref updateOnly);
        node.Update(resource);

        diagnostics.Reset();
        var context = new RenderNodeContext([MakeInput()], outputScale) { Diagnostics = diagnostics, Pool = pool };
        RenderNodeOperation[] ops = node.Process(context);
        PipelineDiagnosticsSnapshot snap = diagnostics.Snapshot();

        using Bitmap actual = Rasterize(ops);
        using Bitmap fresh = RenderFresh(sigma, gammaAmount, outputScale);
        AssertMatches(fresh, actual, $"frame {frame}");
        return snap;
    }

    // A brand-new node with no pool renders a genuine full execution (no prefix engagement) — the parity control.
    private static Bitmap RenderFresh(float sigma, float gammaAmount, float outputScale)
    {
        var (root, gamma, _) = MakeBlurGamma(sigma);
        gamma.Amount.CurrentValue = gammaAmount;
        var resource = (FilterEffect.Resource)root.ToResource(CompositionContext.Default);
        using var node = new PlanFilterEffectRenderNode(resource);
        var context = new RenderNodeContext([MakeInput()], outputScale);
        RenderNodeOperation[] ops = node.Process(context);
        return Rasterize(ops);
    }

    // Steps one frame of the LUT/Blur chain, animating the Blur tail.
    private static PipelineDiagnosticsSnapshot StepLut(
        FilterEffectRenderNode node, FilterEffect.Resource resource, FilterEffect root, Blur blur,
        PipelineDiagnostics diagnostics, RenderTargetPool pool, int frame)
    {
        pool.Trim(frame);
        blur.Sigma.CurrentValue = new Size(BlurSigma(frame), BlurSigma(frame));
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

    private static PipelineDiagnosticsSnapshot StepLutAndParity(
        FilterEffectRenderNode node, FilterEffect.Resource resource, FilterEffect root, Blur blur,
        PipelineDiagnostics diagnostics, RenderTargetPool pool, int frame, CubeSource cube)
    {
        pool.Trim(frame);
        blur.Sigma.CurrentValue = new Size(BlurSigma(frame), BlurSigma(frame));
        bool updateOnly = false;
        resource.Update(root, CompositionContext.Default, ref updateOnly);
        node.Update(resource);

        diagnostics.Reset();
        var context = new RenderNodeContext([MakeInput()]) { Diagnostics = diagnostics, Pool = pool };
        RenderNodeOperation[] ops = node.Process(context);
        PipelineDiagnosticsSnapshot snap = diagnostics.Snapshot();

        using Bitmap actual = Rasterize(ops);
        using Bitmap fresh = RenderFreshLut(cube, frame);
        AssertMatches(fresh, actual, $"lut frame {frame}");
        return snap;
    }

    private static Bitmap RenderFreshLut(CubeSource cube, int frame)
    {
        var (root, _, _) = MakeLutBlur(cube, BlurSigma(frame));
        var resource = (FilterEffect.Resource)root.ToResource(CompositionContext.Default);
        using var node = new PlanFilterEffectRenderNode(resource);
        var context = new RenderNodeContext([MakeInput()]);
        return Rasterize(node.Process(context));
    }

    // Steps one frame (Gamma animates) feeding a caller-supplied input rect, so a bounds-signature change can be
    // driven independently of the effect parameters.
    private static PipelineDiagnosticsSnapshot StepBounds(
        FilterEffectRenderNode node, FilterEffect.Resource resource, FilterEffect root, Gamma gamma,
        PipelineDiagnostics diagnostics, RenderTargetPool pool, int frame, Rect inputBounds)
    {
        pool.Trim(frame);
        gamma.Amount.CurrentValue = 50f + 2f * frame;
        bool updateOnly = false;
        resource.Update(root, CompositionContext.Default, ref updateOnly);
        node.Update(resource);

        diagnostics.Reset();
        var context = new RenderNodeContext([MakeInput(inputBounds)]) { Diagnostics = diagnostics, Pool = pool };
        RenderNodeOperation[] ops = node.Process(context);
        PipelineDiagnosticsSnapshot snap = diagnostics.Snapshot();
        RenderNodeOperation.DisposeAll(ops);
        return snap;
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

    // A leaf render node that emits the source op, so the effect node has a real, cacheable input subtree the outer
    // node cache can engage over (the source generator does not run in the test project, so this is hand-rolled).
    private sealed class StubInputNode : RenderNode
    {
        public override RenderNodeOperation[] Process(RenderNodeContext context) => [MakeInput()];
    }
}

// A color-filter effect whose filter factory throws at execution time, exercising the capture-frame exception path.
// Manual resource (the generator does not run in tests): its Update bumps Version on every call so the effect is
// perpetually "changed" and never joins the reusable stable prefix.
[SuppressResourceClassGeneration]
internal sealed partial class ThrowingColorEffect : FilterEffect
{
    public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
    {
        builder.ColorFilter(ColorFilterNodeDescriptor.Create(
            () => throw new InvalidOperationException("ThrowingColorEffect: simulated pass failure"),
            structuralToken: "ThrowingColor"));
    }

    public override Resource ToResource(CompositionContext context)
    {
        var resource = new Resource();
        bool updateOnly = false;
        resource.Update(this, context, ref updateOnly);
        return resource;
    }

    public new sealed class Resource : FilterEffect.Resource
    {
        public override void Update(EngineObject obj, CompositionContext context, ref bool updateOnly)
        {
            base.Update(obj, context, ref updateOnly);
            Version++;
        }
    }
}
