using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Covers <see cref="RenderTargetPool"/> behavior (feature 004, T012): hit/miss counters, idle-frame and
/// byte-cap eviction, cleared-on-acquire byte determinism, the leak invariant, generation-tag safety under a
/// live stale shallow copy, and the unchanged (this-step) allocation-failure semantics. Pooled buffers are
/// GPU surfaces, so every case runs on the Vulkan render thread and self-skips when no device is available.
/// </summary>
[NonParallelizable]
[TestFixture]
public class RenderTargetPoolTests
{
    private const int W = 40;
    private const int H = 30;

    [Test]
    public void Acquire_FreshPool_CountsMissThenReuseHits()
    {
        RunOnRenderThread(() =>
        {
            var diag = new PipelineDiagnostics();
            using var pool = new RenderTargetPool();

            RenderTarget first = pool.Acquire(W, H, diag) ?? throw new InvalidOperationException("null");
            Assert.Multiple(() =>
            {
                Assert.That(diag.TargetAllocations, Is.EqualTo(1), "miss allocates");
                Assert.That(diag.PoolMisses, Is.EqualTo(1));
                Assert.That(diag.PoolAcquires, Is.EqualTo(1));
            });

            first.Dispose(); // last release returns the buffer to the pool
            Assert.That(pool.IdleCount, Is.EqualTo(1), "released buffer is reclaimable");

            using RenderTarget second = pool.Acquire(W, H, diag) ?? throw new InvalidOperationException("null");
            Assert.Multiple(() =>
            {
                Assert.That(diag.TargetAllocations, Is.EqualTo(1), "reuse does not allocate");
                Assert.That(diag.PoolMisses, Is.EqualTo(1));
                Assert.That(diag.PoolAcquires, Is.EqualTo(2), "every acquire counts");
                Assert.That(pool.IdleCount, Is.EqualTo(0), "reused buffer left the pool");
            });
        });
    }

    [Test]
    public void Acquire_DifferentSize_Misses()
    {
        RunOnRenderThread(() =>
        {
            var diag = new PipelineDiagnostics();
            using var pool = new RenderTargetPool();

            pool.Acquire(W, H, diag)!.Dispose();
            using RenderTarget other = pool.Acquire(W + 8, H, diag) ?? throw new InvalidOperationException("null");

            Assert.That(diag.TargetAllocations, Is.EqualTo(2), "a different bucket cannot reuse");
        });
    }

    [Test]
    public void Acquire_ReusedBuffer_IsClearedForByteDeterminism()
    {
        RunOnRenderThread(() =>
        {
            using var pool = new RenderTargetPool();

            RenderTarget dirty = pool.Acquire(W, H) ?? throw new InvalidOperationException("null");
            dirty.Value.Canvas.Clear(SKColors.Red);
            using (Bitmap before = dirty.Snapshot())
            {
                Assert.That(IsAllZero(before.GetPixelSpan()), Is.False, "sanity: the buffer was dirtied");
            }

            dirty.Dispose();

            using RenderTarget reused = pool.Acquire(W, H) ?? throw new InvalidOperationException("null");
            using Bitmap after = reused.Snapshot();
            Assert.That(IsAllZero(after.GetPixelSpan()), Is.True,
                "a reused buffer must be cleared so it is byte-indistinguishable from a fresh one");
        });
    }

    [Test]
    public void Trim_EvictsBuffersIdleBeyondThreshold()
    {
        RunOnRenderThread(() =>
        {
            using var pool = new RenderTargetPool();
            pool.Trim(0);
            pool.Acquire(W, H)!.Dispose();
            Assert.That(pool.IdleCount, Is.EqualTo(1));

            pool.Trim(RenderTargetPool.IdleFrameThreshold - 1);
            Assert.That(pool.IdleCount, Is.EqualTo(1), "still within the idle window");

            pool.Trim(RenderTargetPool.IdleFrameThreshold);
            Assert.That(pool.IdleCount, Is.EqualTo(0), "evicted once idle for the threshold");
        });
    }

    [Test]
    public void EnforceByteCap_EvictsLeastRecentlyUsed()
    {
        RunOnRenderThread(() =>
        {
            // 40x30 RGBA16F = 40*30*8 = 9600 bytes; a 12000-byte cap holds exactly one.
            using var pool = new RenderTargetPool(maxIdleBytes: 12000);

            pool.Trim(0);
            pool.Acquire(W, H)!.Dispose();       // older, stamped frame 0
            pool.Trim(1);
            pool.Acquire(W, H + 4)!.Dispose();   // newer, stamped frame 1, pushes over the cap

            Assert.Multiple(() =>
            {
                Assert.That(pool.IdleCount, Is.EqualTo(1), "the LRU buffer was evicted to stay under the cap");
                Assert.That(pool.IdleBytes, Is.LessThanOrEqualTo(12000L));
            });

            // The survivor is the newer (H+4) bucket: reacquiring it hits, reacquiring the evicted one misses.
            var diag = new PipelineDiagnostics();
            using RenderTarget survivor = pool.Acquire(W, H + 4, diag)!;
            Assert.That(diag.PoolMisses, Is.EqualTo(0), "the newer buffer survived and reuses");
        });
    }

    [Test]
    public void LeakInvariant_EveryAcquiredBufferIsReclaimable()
    {
        RunOnRenderThread(() =>
        {
            var diag = new PipelineDiagnostics();
            using var pool = new RenderTargetPool();

            var held = new List<RenderTarget>();
            for (int i = 0; i < 3; i++)
                held.Add(pool.Acquire(W, H, diag) ?? throw new InvalidOperationException("null"));

            Assert.That(pool.IdleCount, Is.EqualTo(0), "all three are leased");
            foreach (RenderTarget t in held)
                t.Dispose();

            Assert.Multiple(() =>
            {
                Assert.That(pool.IdleCount, Is.EqualTo(3), "everything acquired in a frame is reclaimed");
                Assert.That(diag.TargetAllocations, Is.EqualTo(3));
                Assert.That(diag.PoolAcquires, Is.EqualTo(3));
            });
        });
    }

    [Test]
    public void GenerationTag_StaleShallowCopyCannotObserveReissuedBuffer()
    {
        RunOnRenderThread(() =>
        {
            using var pool = new RenderTargetPool();

            RenderTarget original = pool.Acquire(W, H) ?? throw new InvalidOperationException("null");
            RenderTarget stale = original.ShallowCopy();

            // Force the buffer back into the pool while the copies are still live (bumps the generation),
            // then reissue it — the reissued handle is valid, the stale copy is not.
            pool.ForceReturnForTest(original);
            using RenderTarget reissued = pool.Acquire(W, H) ?? throw new InvalidOperationException("null");

            Assert.Multiple(() =>
            {
                Assert.Throws<ObjectDisposedException>(() => _ = stale.Value,
                    "a stale lease must not read the reissued surface");
                Assert.DoesNotThrow(() => _ = reissued.Value, "the reissued lease is valid");
            });

            // Cleanup is double-return-safe (IsPooled guard); order is irrelevant.
            reissued.Dispose();
            original.Dispose();
            stale.Dispose();
        });
    }

    [Test]
    public void AllocationFailure_ReturnsNullWithoutCounting_LikeCreate()
    {
        RunOnRenderThread(() =>
        {
            var diag = new PipelineDiagnostics();
            using var pool = new RenderTargetPool();
            pool.SetBackingFactoryForTest((_, _) => null);

            RenderTarget? result = pool.Acquire(W, H, diag);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.Null, "acquire failure surfaces exactly like RenderTarget.Create null");
                Assert.That(diag.TargetAllocations, Is.EqualTo(0));
                Assert.That(diag.PoolAcquires, Is.EqualTo(0));
                Assert.That(diag.PoolMisses, Is.EqualTo(0));
            });
        });
    }

    [Test]
    public void StaticAcquire_WithoutPool_CreatesDirectlyAndCounts()
    {
        RunOnRenderThread(() =>
        {
            var diag = new PipelineDiagnostics();
            using RenderTarget? target = RenderTargetPool.Acquire(pool: null, W, H, diag);

            Assert.Multiple(() =>
            {
                Assert.That(target, Is.Not.Null, "no pool falls back to direct creation");
                Assert.That(diag.TargetAllocations, Is.EqualTo(1), "the direct path still counts an allocation");
                Assert.That(diag.PoolAcquires, Is.EqualTo(0), "no pool means no pool acquire");
            });
        });
    }

    private static bool IsAllZero(ReadOnlySpan<byte> span) => span.IndexOfAnyExcept((byte)0) < 0;

    private static void RunOnRenderThread(Action action)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(action);
    }
}
