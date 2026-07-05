using Beutl.Graphics.Backend;
using Beutl.Threading;
using SkiaSharp;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// Reuses effect-intermediate <see cref="RenderTarget"/> buffers across frames (feature 004, US3 /
/// research D4). Exact-size <c>(width, height, format)</c> buckets keep a pooled buffer indistinguishable
/// from a fresh one so shader resolution uniforms and the 003 density bookkeeping never change; a hit is
/// a re-wrap of a cleared existing buffer, a miss allocates. Steady-state, structurally-and-size-stable
/// scenes reuse identical sizes, so allocations drop to zero after the first frame (SC-003).
/// </summary>
/// <remarks>
/// <para><b>Lease state machine (ownership inside <see cref="RenderTarget"/>).</b> The pool does not hand
/// out disposable ownership; it hands out a <em>lease</em>. Each <see cref="Acquire"/> wraps a
/// <see cref="PooledSurface"/> in a fresh <see cref="RenderTarget"/> whose ref-counted shallow copies
/// share one counter. The pool-aware deallocator installed by <see cref="RenderTarget.WrapPooled"/>
/// replaces <c>SKSurfaceCounter</c>'s dispose-on-zero: the buffer returns to its bucket at the underlying
/// target's <em>last</em> ref-count release — never when an individual shallow copy disposes while others
/// live. States per <see cref="PooledSurface"/>:</para>
/// <list type="bullet">
/// <item><description><b>Idle</b> — in a bucket, available. <see cref="Acquire"/> pops, clears, re-wraps → Leased.</description></item>
/// <item><description><b>Leased</b> — one or more live shallow-copy handles. Last release → <see cref="Return"/> → Idle (generation bumped).</description></item>
/// <item><description><b>Evicted</b> — idle ≥ <see cref="IdleFrameThreshold"/> frames, or trimmed by the byte soft-cap; the backing surface + texture are disposed and the buffer leaves the pool.</description></item>
/// </list>
/// <para>A <b>generation tag</b> on each <see cref="PooledSurface"/> is bumped on every <see cref="Return"/>.
/// A lease captures the generation at acquire time (see <see cref="RenderTarget"/>), so a stale shallow copy
/// of an already-returned (and possibly reissued) lease is rejected before it can read or write the reissued
/// surface. Under correct ref-counting a return only happens with no live handles, making the tag a
/// defense-in-depth guard; it is exercised directly by the tests via <see cref="ForceReturnForTest"/>.</para>
/// <para><b>Failure semantics AT THIS ROLLOUT STEP are unchanged.</b> A pool acquire that must allocate and
/// fails returns <see langword="null"/> — byte-for-byte the same surface as <see cref="RenderTarget.Create"/>
/// returning <see langword="null"/> today — so the existing call-site handling (the activator's flush
/// drop/throw, the custom context's empty-target path) behaves identically. The contracts/execution-plan.md
/// §C7 normalization (uniform preview-drop / delivery-throw) lands with the new executor in a later step,
/// not here.</para>
/// <para><b>Ownership choice: per-renderer, render-thread-affine.</b> Research D4 places the pool on the
/// shared graphics context. This step instead threads one pool per renderer (mirroring how
/// <see cref="PipelineDiagnostics"/> was threaded in step 1), because the counter tests and golden harness
/// each construct their own renderer/processor and must not see pool state leak across tests; a per-renderer
/// pool that lives on the render thread gives that isolation for free. Cross-renderer sharing (moving the
/// pool onto the shared context) can come with the executor, when a single owner across renderers is
/// actually useful. All access is render-thread-affine, like <see cref="RenderTarget.Create"/>.</para>
/// </remarks>
public sealed class RenderTargetPool : IDisposable
{
    /// <summary>A pooled buffer is evicted once it has been idle for at least this many frames.</summary>
    public const int IdleFrameThreshold = 8;

    /// <summary>Default total-bytes soft cap for idle buffers before LRU eviction kicks in (256 MiB).</summary>
    public const long DefaultMaxIdleBytes = 256L * 1024 * 1024;

    private readonly Dispatcher? _dispatcher = Dispatcher.Current;
    private readonly Dictionary<BucketKey, List<PooledSurface>> _buckets = [];
    private readonly long _maxIdleBytes;
    private Func<int, int, (SKSurface Surface, ITexture2D? Texture)?> _backingFactory = RenderTarget.CreateBackingSurface;
    private long _idleBytes;
    private long _currentFrame;
    private bool _isDisposed;

    public RenderTargetPool(long maxIdleBytes = DefaultMaxIdleBytes)
    {
        _maxIdleBytes = maxIdleBytes > 0 ? maxIdleBytes : DefaultMaxIdleBytes;
    }

    /// <summary>Number of idle (available) buffers currently held. Test/diagnostic surface.</summary>
    public int IdleCount => _buckets.Values.Sum(l => l.Count);

    /// <summary>Total bytes of idle buffers currently held. Test/diagnostic surface.</summary>
    public long IdleBytes => _idleBytes;

    /// <summary>
    /// Acquires a cleared RGBA16F buffer of exactly <paramref name="width"/> × <paramref name="height"/>:
    /// pops and clears a matching idle buffer (a hit), or allocates a fresh one (a miss). Every successful
    /// acquire counts <see cref="PipelineDiagnostics.PoolAcquires"/>; a miss additionally counts
    /// <see cref="PipelineDiagnostics.TargetAllocations"/> and <see cref="PipelineDiagnostics.PoolMisses"/>.
    /// Returns <see langword="null"/> on allocation failure, exactly as <see cref="RenderTarget.Create"/> does
    /// (no counter is touched), so existing call-site failure handling is preserved.
    /// </summary>
    public RenderTarget? Acquire(int width, int height, PipelineDiagnostics? diagnostics = null)
        => Acquire(width, height, TextureFormat.RGBA16Float, diagnostics);

    /// <summary>
    /// Acquire overload naming the buffer <paramref name="format"/> (RGBA16F today; the key admits
    /// <see cref="TextureFormat.Depth32Float"/> so depth/ping-pong targets can pool later without a reshape).
    /// </summary>
    public RenderTarget? Acquire(int width, int height, TextureFormat format, PipelineDiagnostics? diagnostics = null)
    {
        VerifyAccess();

        var key = new BucketKey(width, height, format);
        if (_buckets.TryGetValue(key, out List<PooledSurface>? list) && list.Count > 0)
        {
            PooledSurface pooled = list[^1];
            list.RemoveAt(list.Count - 1);
            pooled.IsPooled = false;
            _idleBytes -= pooled.ByteSize;
            ClearForReuse(pooled);
            if (diagnostics != null)
                diagnostics.PoolAcquires++;
            return RenderTarget.WrapPooled(this, pooled);
        }

        if (_backingFactory(width, height) is not { } backing)
            return null;

        var fresh = new PooledSurface(backing.Surface, backing.Texture, width, height, format);
        if (diagnostics != null)
        {
            diagnostics.TargetAllocations++;
            diagnostics.PoolMisses++;
            diagnostics.PoolAcquires++;
        }

        return RenderTarget.WrapPooled(this, fresh);
    }

    /// <summary>
    /// Nullable entry point for call sites that may or may not have a pool: with a <paramref name="pool"/>
    /// it delegates to <see cref="Acquire(int, int, PipelineDiagnostics?)"/>; without one it falls back to a
    /// direct <see cref="RenderTarget.Create"/> and counts <see cref="PipelineDiagnostics.TargetAllocations"/>
    /// itself, so the total <c>TargetAllocations</c> is identical whether or not pooling is enabled.
    /// </summary>
    public static RenderTarget? Acquire(
        RenderTargetPool? pool, int width, int height, PipelineDiagnostics? diagnostics)
    {
        if (pool != null)
            return pool.Acquire(width, height, diagnostics);

        RenderTarget? target = RenderTarget.Create(width, height);
        if (target != null && diagnostics != null)
            diagnostics.TargetAllocations++;
        return target;
    }

    /// <summary>
    /// Returns a buffer to its bucket at its lease's last release (called by the pool-aware deallocator in
    /// <see cref="RenderTarget"/>, never directly by consumers). Bumps the generation to invalidate any stale
    /// lease, stamps the current frame for idle eviction, and enforces the byte cap. If the pool is already
    /// disposed the buffer is disposed instead of resurrected.
    /// </summary>
    internal void Return(PooledSurface pooled)
    {
        // Idempotent against a double return (a buggy double-dispose, or the generation test seam that
        // deliberately returns a still-leased buffer): a buffer already idle is not re-bucketed.
        if (pooled.IsPooled)
            return;

        if (_isDisposed)
        {
            DisposeBacking(pooled);
            return;
        }

        pooled.Generation++;
        pooled.LastUsedFrame = _currentFrame;
        pooled.IsPooled = true;
        GetBucket(pooled.Key).Add(pooled);
        _idleBytes += pooled.ByteSize;
        EnforceByteCap();
    }

    /// <summary>
    /// Frame-boundary maintenance: advances the pool's frame clock to <paramref name="frameIndex"/>, disposes
    /// buffers idle for at least <see cref="IdleFrameThreshold"/> frames, and enforces the byte soft-cap by
    /// LRU. Call once per frame (the renderer calls it at frame start). Idempotent and cheap when nothing is
    /// idle.
    /// </summary>
    public void Trim(long frameIndex)
    {
        VerifyAccess();
        _currentFrame = frameIndex;

        foreach (List<PooledSurface> list in _buckets.Values)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (frameIndex - list[i].LastUsedFrame >= IdleFrameThreshold)
                {
                    EvictAt(list, i);
                }
            }
        }

        EnforceByteCap();
    }

    /// <summary>Test seam: disposes every idle buffer and resets the pool to empty deterministically.</summary>
    public void Clear()
    {
        VerifyAccess();
        DisposeAllIdle();
    }

    private void DisposeAllIdle()
    {
        foreach (List<PooledSurface> list in _buckets.Values)
        {
            foreach (PooledSurface pooled in list)
                DisposeBacking(pooled);
            list.Clear();
        }

        _buckets.Clear();
        _idleBytes = 0;
    }

    /// <summary>
    /// Test-only: forces <paramref name="target"/>'s pooled buffer back into the pool and invalidates
    /// outstanding leases (bumps the generation) even while shallow copies remain live, so the generation
    /// guard can be verified against a reissued buffer without relying on ref-count timing.
    /// </summary>
    internal void ForceReturnForTest(RenderTarget target)
    {
        PooledSurface pooled = target.PooledSurfaceOrThrowForTest();
        Return(pooled);
    }

    /// <summary>Test seam: overrides the backing-surface factory to simulate deterministic allocation failure.</summary>
    internal void SetBackingFactoryForTest(Func<int, int, (SKSurface Surface, ITexture2D? Texture)?> factory)
        => _backingFactory = factory;

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        DisposeAllIdle();
    }

    private void EnforceByteCap()
    {
        while (_idleBytes > _maxIdleBytes)
        {
            (List<PooledSurface> list, int index) = FindLeastRecentlyUsed();
            if (list.Count == 0)
                break;

            EvictAt(list, index);
        }
    }

    private (List<PooledSurface> List, int Index) FindLeastRecentlyUsed()
    {
        List<PooledSurface> lruList = [];
        int lruIndex = -1;
        long oldest = long.MaxValue;
        foreach (List<PooledSurface> list in _buckets.Values)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].LastUsedFrame < oldest)
                {
                    oldest = list[i].LastUsedFrame;
                    lruList = list;
                    lruIndex = i;
                }
            }
        }

        return lruIndex >= 0 ? (lruList, lruIndex) : ([], 0);
    }

    private void EvictAt(List<PooledSurface> list, int index)
    {
        PooledSurface pooled = list[index];
        list.RemoveAt(index);
        _idleBytes -= pooled.ByteSize;
        DisposeBacking(pooled);
    }

    // GPU teardown is render-thread-affine; marshal it the same way SKSurfaceCounter marshals disposal, so
    // a Renderer.Dispose from another thread still frees pooled buffers on the render thread.
    private void DisposeBacking(PooledSurface pooled)
    {
        if (_dispatcher == null || _dispatcher.CheckAccess())
            pooled.DisposeBacking();
        else
            _dispatcher.Dispatch(pooled.DisposeBacking);
    }

    private List<PooledSurface> GetBucket(BucketKey key)
    {
        if (!_buckets.TryGetValue(key, out List<PooledSurface>? list))
        {
            list = [];
            _buckets[key] = list;
        }

        return list;
    }

    private static void ClearForReuse(PooledSurface pooled)
    {
        // A reused buffer must be byte-indistinguishable from a fresh one (frozen-reference suite asserts
        // SSIM 1.0), so wipe residual content on acquire.
        pooled.Surface.Canvas.Clear(SKColors.Transparent);
    }

    private void VerifyAccess()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        _dispatcher?.VerifyAccess();
    }
}

/// <summary>
/// One reusable GPU buffer owned by a <see cref="RenderTargetPool"/>: the persistent RGBA16F surface (and its
/// backing texture) plus the pooling bookkeeping (generation tag, last-used frame). The pool owns disposal;
/// leases (<see cref="RenderTarget"/> handles) only borrow it.
/// </summary>
internal sealed class PooledSurface(
    SKSurface surface, ITexture2D? texture, int width, int height, TextureFormat format)
{
    public SKSurface Surface { get; } = surface;

    public ITexture2D? Texture { get; } = texture;

    public int Width { get; } = width;

    public int Height { get; } = height;

    public TextureFormat Format { get; } = format;

    /// <summary>Bumped on every return to the pool; a lease captures this at acquire time to detect staleness.</summary>
    public int Generation { get; set; }

    /// <summary>The pool frame index at which this buffer last became idle; drives idle-frame eviction and LRU.</summary>
    public long LastUsedFrame { get; set; }

    /// <summary>True while sitting idle in a bucket; guards against double return of a re-leased buffer.</summary>
    public bool IsPooled { get; set; }

    public BucketKey Key => new(Width, Height, Format);

    public long ByteSize => (long)Width * Height * BytesPerPixel(Format);

    private bool _backingDisposed;

    public void DisposeBacking()
    {
        if (_backingDisposed)
            return;

        _backingDisposed = true;
        Surface.Dispose();
        Texture?.Dispose();
    }

    private static int BytesPerPixel(TextureFormat format) => format switch
    {
        TextureFormat.RGBA16Float => 8,
        TextureFormat.RGBA32Float => 16,
        TextureFormat.RGBA8Unorm or TextureFormat.BGRA8Unorm => 4,
        TextureFormat.Depth32Float or TextureFormat.R32Float or TextureFormat.Depth24Stencil8 => 4,
        TextureFormat.R16Float => 2,
        TextureFormat.R8Unorm => 1,
        _ => 8,
    };
}

/// <summary>Exact-size bucket identity for <see cref="RenderTargetPool"/> (research D4).</summary>
internal readonly record struct BucketKey(int Width, int Height, TextureFormat Format);
