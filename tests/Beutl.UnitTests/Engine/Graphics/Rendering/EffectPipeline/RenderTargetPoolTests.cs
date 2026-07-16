using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Vulkan;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Threading;
using Beutl.UnitTests.Engine.Graphics.Backend;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Covers <see cref="RenderTargetPool"/> behavior (feature 004, T012): hit/miss counters, idle-frame and
/// byte-cap eviction, consumer-owned initialization, the leak invariant, generation-tag safety under a
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
    public void AcquireTexture_SameRgba16Format_DoesNotShareSkiaSurfaceBucket()
    {
        RunOnRenderThread(() =>
        {
            var diagnostics = new PipelineDiagnostics();
            using var pool = new RenderTargetPool();

            pool.Acquire(W, H, diagnostics)!.Dispose();
            pool.AcquireTexture(W, H, TextureFormat.RGBA16Float, diagnostics)!.Dispose();

            Assert.Multiple(() =>
            {
                Assert.That(pool.BucketCountForTest, Is.EqualTo(2),
                    "a Skia surface and a raw texture need distinct buckets even when size/format match");
                Assert.That(diagnostics.PoolMisses, Is.EqualTo(2));
                Assert.That(diagnostics.TargetAllocations, Is.EqualTo(2));
                Assert.That(pool.IdleBytes, Is.EqualTo(2L * W * H * 8),
                    "surface and raw-texture idle bytes are each counted exactly once");
            });

            using RenderTarget surface = pool.Acquire(W, H, diagnostics)!;
            using PooledTextureLease texture = pool.AcquireTexture(
                W, H, TextureFormat.RGBA16Float, diagnostics)!;
            Assert.Multiple(() =>
            {
                Assert.That(diagnostics.PoolMisses, Is.EqualTo(2),
                    "each resource kind must hit its own warmed bucket");
                Assert.That(pool.IdleBytes, Is.Zero,
                    "checking out both idle resources balances the idle-byte accounting");
            });
        });
    }

    [Test]
    public void Acquire_ReusedBuffer_IsLeftForConsumerInitialization()
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
            Assert.That(IsAllZero(after.GetPixelSpan()), Is.False,
                "the pool must not clear a target that every drawing consumer initializes itself");
        });
    }

    [Test]
    public void DisposeBackings_SurfaceFailureStillDisposesTexture()
    {
        var injected = new InvalidOperationException("surface dispose failed");
        var surface = new DisposeSpy(injected);
        var texture = new DisposeSpy();

        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(() =>
            PooledSurface.DisposeBackings(surface, texture));

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(injected));
            Assert.That(surface.DisposeCount, Is.EqualTo(1));
            Assert.That(texture.DisposeCount, Is.EqualTo(1),
                "the native texture must be released even when surface teardown fails");
        });
    }

    [Test]
    public void Acquire_AfterContextIdentityChange_EvictsStaleBackingInsteadOfReissuingIt()
    {
        var firstContext = new object();
        var secondContext = new object();
        object currentContext = firstContext;
        var diagnostics = new PipelineDiagnostics();
        using var pool = new RenderTargetPool();
        pool.SetContextIdentityProviderForTest(() => currentContext);
        pool.SetBackingFactoryForTest((width, height) =>
        {
            SKSurface surface = SKSurface.Create(new SKImageInfo(width, height))
                ?? throw new InvalidOperationException("Could not create the test surface.");
            return (surface, null);
        });

        pool.Acquire(W, H, diagnostics)!.Dispose();
        Assert.That(pool.IdleCount, Is.EqualTo(1));

        int staleDisposeCount = 0;
        pool.SetDisposeBackingForTest(pooled =>
        {
            if (ReferenceEquals(pooled.ContextId, firstContext))
                staleDisposeCount++;
            pooled.DisposeBacking();
        });
        currentContext = secondContext;

        using RenderTarget replacement = pool.Acquire(W, H, diagnostics)!;

        Assert.Multiple(() =>
        {
            Assert.That(staleDisposeCount, Is.EqualTo(1));
            Assert.That(diagnostics.PoolMisses, Is.EqualTo(2),
                "a context change must allocate a backing owned by the recreated device");
            Assert.That(diagnostics.TargetAllocations, Is.EqualTo(2));
            Assert.That(pool.IdleCount, Is.Zero);
        });
    }

    [Test]
    public void Dispose_FromWorkerThread_MarshalsEntireIdleSweepToOwningDispatcher()
    {
        Dispatcher dispatcher = Dispatcher.Spawn();
        RenderTargetPool? pool = null;
        try
        {
            int dispatcherThreadId = dispatcher.Invoke(() => Environment.CurrentManagedThreadId);
            int disposeThreadId = -1;
            pool = dispatcher.Invoke(() =>
            {
                var created = new RenderTargetPool();
                created.SetBackingFactoryForTest((width, height) =>
                {
                    SKSurface surface = SKSurface.Create(new SKImageInfo(width, height))
                        ?? throw new InvalidOperationException("Could not create the test surface.");
                    return (surface, null);
                });
                created.SetDisposeBackingForTest(pooled =>
                {
                    disposeThreadId = Environment.CurrentManagedThreadId;
                    pooled.DisposeBacking();
                });
                created.Acquire(W, H)!.Dispose();
                return created;
            });

            Task.Run(pool.Dispose).GetAwaiter().GetResult();

            dispatcher.Invoke(() =>
            {
                Assert.Multiple(() =>
                {
                    Assert.That(disposeThreadId, Is.EqualTo(dispatcherThreadId));
                    Assert.That(pool.IdleCount, Is.Zero);
                    Assert.That(pool.IdleBytes, Is.Zero);
                    Assert.That(pool.BucketCountForTest, Is.Zero);
                });
            });
        }
        finally
        {
            pool?.Dispose();
            dispatcher.Shutdown();
        }
    }

    [Test]
    public void LeaseDispose_AfterOwningDispatcherStops_ReleasesBackingAndBalancesLeaseCount()
    {
        Dispatcher dispatcher = Dispatcher.Spawn();
        RenderTargetPool? pool = null;
        RenderTarget? lease = null;
        int backingDisposeCount = 0;
        try
        {
            (pool, lease) = dispatcher.Invoke(() =>
            {
                var created = new RenderTargetPool();
                created.SetBackingFactoryForTest((width, height) =>
                {
                    SKSurface surface = SKSurface.Create(new SKImageInfo(width, height))
                        ?? throw new InvalidOperationException("Could not create the test surface.");
                    return (surface, null);
                });
                created.SetDisposeBackingForTest(pooled =>
                {
                    Interlocked.Increment(ref backingDisposeCount);
                    pooled.DisposeBacking();
                });
                return (created, created.Acquire(W, H)
                    ?? throw new InvalidOperationException("Could not acquire the test lease."));
            });
            Assert.That(pool.LiveLeaseCount, Is.EqualTo(1));

            dispatcher.Shutdown();
            Assert.That(dispatcher.Thread.Join(TimeSpan.FromSeconds(10)), Is.True,
                "the test dispatcher must finish before the last lease is released");

            lease.Dispose();
            lease = null;

            Assert.Multiple(() =>
            {
                Assert.That(backingDisposeCount, Is.EqualTo(1),
                    "a rejected cleanup dispatch must dispose the backing inline instead of leaking it");
                Assert.That(pool.LiveLeaseCount, Is.Zero,
                    "the shutdown fallback must balance the lease accounting");
                Assert.That(pool.IdleCount, Is.Zero,
                    "a stopped dispatcher cannot accept a returned buffer for later reuse");
            });
        }
        finally
        {
            lease?.Dispose();
            if (!dispatcher.HasShutdownStarted)
                dispatcher.Shutdown();
        }
    }

    [Test]
    public void LeaseReturn_AcceptedBeforeShutdown_IsNotAbandonedAndBalancesAtomicCounters()
    {
        Dispatcher dispatcher = Dispatcher.Spawn();
        dispatcher.Thread.IsBackground = true;
        RenderTargetPool? pool = null;
        RenderTarget? lease = null;
        int backingDisposeCount = 0;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        try
        {
            (pool, lease) = dispatcher.Invoke(() =>
            {
                var created = new RenderTargetPool();
                created.SetBackingFactoryForTest((width, height) =>
                {
                    SKSurface surface = SKSurface.Create(new SKImageInfo(width, height))
                        ?? throw new InvalidOperationException("Could not create the test surface.");
                    return (surface, null);
                });
                created.SetDisposeBackingForTest(pooled =>
                {
                    Interlocked.Increment(ref backingDisposeCount);
                    pooled.DisposeBacking();
                });
                RenderTarget acquired = created.Acquire(W, H)
                    ?? throw new InvalidOperationException("Could not acquire the test lease.");
                created.Dispose();
                return (created, acquired);
            });

            dispatcher.Dispatch(() =>
            {
                entered.Set();
                release.Wait();
            }, DispatchPriority.High);
            Assert.That(entered.Wait(TimeSpan.FromSeconds(5)), Is.True);

            lease.Dispose();
            lease = null;
            Assert.That(pool.LiveLeaseCount, Is.EqualTo(1),
                "the accepted return remains live until either its action or abort fallback runs");

            dispatcher.Shutdown();

            Assert.Multiple(() =>
            {
                Assert.That(backingDisposeCount, Is.EqualTo(1),
                    "draining an accepted return must release its backing exactly once");
                Assert.That(pool.LiveLeaseCount, Is.Zero,
                    "the shutdown fallback must atomically balance the live lease count");
                Assert.That(pool.PeakLiveLeaseCount, Is.EqualTo(1));
            });
        }
        finally
        {
            lease?.Dispose();
            release.Set();
            if (!dispatcher.HasShutdownStarted)
                dispatcher.Shutdown();
            Assert.That(dispatcher.Thread.Join(TimeSpan.FromSeconds(5)), Is.True);
        }
    }

    [Test]
    public void Clear_DisposeFailureStillReleasesEveryIdleBackingAndResetsAccounting()
    {
        RunOnRenderThread(() =>
        {
            using var pool = new RenderTargetPool();
            for (int i = 0; i < 3; i++)
                pool.Acquire(W + i, H)!.Dispose();

            int disposeCount = 0;
            var injected = new InvalidOperationException("first backing dispose failed");
            pool.SetDisposeBackingForTest(pooled =>
            {
                disposeCount++;
                pooled.DisposeBacking();
                if (disposeCount == 1)
                    throw injected;
            });

            InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(pool.Clear);

            Assert.Multiple(() =>
            {
                Assert.That(actual, Is.SameAs(injected), "the first cleanup failure is preserved");
                Assert.That(disposeCount, Is.EqualTo(3), "a failure must not abort the remaining teardown");
                Assert.That(pool.IdleCount, Is.Zero);
                Assert.That(pool.IdleBytes, Is.Zero);
                Assert.That(pool.BucketCountForTest, Is.Zero);
            });
        });
    }

    [Test]
    public void Trim_DisposeFailureStillEvictsEveryIdleBackingWithoutAbortingFrame()
    {
        RunOnRenderThread(() =>
        {
            using var pool = new RenderTargetPool();
            pool.Trim(0);
            for (int i = 0; i < 3; i++)
                pool.Acquire(W + i, H)!.Dispose();

            int disposeCount = 0;
            pool.SetDisposeBackingForTest(pooled =>
            {
                disposeCount++;
                pooled.DisposeBacking();
                if (disposeCount == 1)
                    throw new InvalidOperationException("first maintenance dispose failed");
            });

            Assert.DoesNotThrow(() => pool.Trim(RenderTargetPool.IdleFrameThreshold));

            Assert.Multiple(() =>
            {
                Assert.That(disposeCount, Is.EqualTo(3), "a fault must not abort the remaining maintenance sweep");
                Assert.That(pool.IdleCount, Is.Zero);
                Assert.That(pool.IdleBytes, Is.Zero);
                Assert.That(pool.BucketCountForTest, Is.Zero);
            });
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

    // A multi-buffer idle-frame eviction sweep drains the GPU exactly ONCE (GpuDisposeBatch), not once per evicted
    // buffer: before the batch, each VulkanTexture2D.Dispose issued its own queue-submit + fence-wait.
    [Test]
    public void Trim_EvictingIdleBatch_DrainsGpuExactlyOnce()
    {
        RunOnRenderThread(() =>
        {
            using var pool = new RenderTargetPool();
            pool.Trim(0);

            var held = new List<RenderTarget>();
            for (int i = 0; i < 4; i++)
                held.Add(pool.Acquire(W + i * 8, H) ?? throw new InvalidOperationException("null"));
            foreach (RenderTarget t in held)
                t.Dispose();
            Assert.That(pool.IdleCount, Is.EqualTo(4), "all four buffers are idle and evictable");

            GpuDisposeBatch.ResetFlushCountForTest();
            pool.Trim(RenderTargetPool.IdleFrameThreshold);

            Assert.Multiple(() =>
            {
                Assert.That(pool.IdleCount, Is.EqualTo(0), "the whole batch was evicted");
                // Zero on a backend whose Vulkan context has no GRContext to drain (MoltenVK - Skia runs on Metal
                // there); exactly one where it does (SwiftShader CI). Never once per evicted buffer.
                Assert.That(GpuDisposeBatch.FlushCount, Is.LessThanOrEqualTo(1),
                    "the four-buffer eviction drains the GPU at most once, never once per buffer");
            });
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

            Assert.That(pool.IdleCount, Is.EqualTo(2),
                "the soft cap is deferred until frame-boundary Trim so return-path eviction can be batched");

            GpuDisposeBatch.ResetFlushCountForTest();
            pool.Trim(2);

            Assert.Multiple(() =>
            {
                Assert.That(pool.IdleCount, Is.EqualTo(1), "the LRU buffer was evicted to stay under the cap");
                Assert.That(pool.IdleBytes, Is.LessThanOrEqualTo(12000L));
                Assert.That(GpuDisposeBatch.FlushCount, Is.LessThanOrEqualTo(1),
                    "all byte-cap evictions at the frame boundary share at most one GPU drain");
            });

            // The survivor is the newer (H+4) bucket: reacquiring it hits, reacquiring the evicted one misses.
            var diag = new PipelineDiagnostics();
            using RenderTarget survivor = pool.Acquire(W, H + 4, diag)!;
            Assert.That(diag.PoolMisses, Is.EqualTo(0), "the newer buffer survived and reuses");
        });
    }

    // A bucket whose last buffer leaves (acquire or eviction) must leave the dictionary too: a scene whose buffer
    // size varies per frame otherwise accumulates empty lists without bound and degrades the per-frame LRU scan.
    [Test]
    public void EmptiedBuckets_AreRemovedFromTheDictionary()
    {
        RunOnRenderThread(() =>
        {
            using var pool = new RenderTargetPool();

            // Acquire path: draining a bucket removes it immediately.
            pool.Acquire(W, H)!.Dispose();
            Assert.That(pool.BucketCountForTest, Is.EqualTo(1));
            using (RenderTarget reused = pool.Acquire(W, H)!)
            {
                Assert.That(pool.BucketCountForTest, Is.Zero, "draining the bucket's last buffer removes the bucket");
            }

            // Eviction path: a Trim past the idle threshold empties the buckets and sweeps them out.
            pool.Trim(0);
            for (int i = 0; i < 4; i++)
                pool.Acquire(W + i, H)!.Dispose();
            Assert.That(pool.BucketCountForTest, Is.EqualTo(4));

            pool.Trim(RenderTargetPool.IdleFrameThreshold + 1);
            Assert.Multiple(() =>
            {
                Assert.That(pool.IdleCount, Is.Zero, "all buffers idled past the threshold and evicted");
                Assert.That(pool.BucketCountForTest, Is.Zero, "the emptied buckets were swept out of the dictionary");
            });
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

    private sealed class DisposeSpy(Exception? failure = null) : IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose()
        {
            DisposeCount++;
            if (failure != null)
                throw failure;
        }
    }

    private static void RunOnRenderThread(Action action)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(action);
    }
}
