using System.Runtime.ExceptionServices;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Vulkan;
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
/// <para><b>Failure semantics.</b> An acquire that cannot allocate returns <see langword="null"/>, like
/// <see cref="RenderTarget.Create"/>. The plan executor owns the uniform C7 response: finite-quality preview
/// drops the affected output and continues, while delivery/export throws <see cref="InvalidOperationException"/>.
/// Callers outside that executor remain responsible for handling a null acquire.</para>
/// <para><b>Ownership: per-renderer, render-thread-affine.</b> Each renderer owns one pool so leases, byte caps,
/// diagnostics, and teardown remain isolated between renderers and test harnesses. All access is
/// render-thread-affine, like <see cref="RenderTarget.Create"/>.</para>
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
    private Func<int, int, TextureFormat, ITexture2D?> _textureFactory = CreateBackingTexture;
    private Action<PooledSurface> _clearForReuse = ClearForReuse;
    private long _idleBytes;
    private long _currentFrame;
    private long _liveLeases;
    private long _peakLiveLeases;
    private bool _isDisposed;

    public RenderTargetPool(long maxIdleBytes = DefaultMaxIdleBytes)
    {
        _maxIdleBytes = maxIdleBytes > 0 ? maxIdleBytes : DefaultMaxIdleBytes;
    }

    /// <summary>Number of idle (available) buffers currently held. Test/diagnostic surface.</summary>
    public int IdleCount => _buckets.Values.Sum(l => l.Count);

    internal int BucketCountForTest => _buckets.Count;

    /// <summary>Total bytes of idle buffers currently held. Test/diagnostic surface.</summary>
    public long IdleBytes => _idleBytes;

    /// <summary>Number of leases currently issued and not yet returned. Test/diagnostic surface.</summary>
    public long LiveLeaseCount => _liveLeases;

    /// <summary>High-water mark of concurrently live leases since the last <see cref="ResetPeakLiveLeases"/> (the FR-007 measured peak).</summary>
    public long PeakLiveLeaseCount => _peakLiveLeases;

    /// <summary>Restarts the peak-live window at the current live count; the plan executor calls this once per plan execution.</summary>
    public void ResetPeakLiveLeases() => _peakLiveLeases = _liveLeases;

    /// <summary>
    /// Acquires a cleared RGBA16F buffer of exactly <paramref name="width"/> × <paramref name="height"/>:
    /// pops and clears a matching idle buffer (a hit), or allocates a fresh one (a miss). Every successful
    /// acquire counts <see cref="PipelineDiagnostics.PoolAcquires"/>; a miss additionally counts
    /// <see cref="PipelineDiagnostics.TargetAllocations"/> and <see cref="PipelineDiagnostics.PoolMisses"/>.
    /// Returns <see langword="null"/> on allocation failure, exactly as <see cref="RenderTarget.Create"/> does
    /// (no counter is touched). The plan executor applies the C7 preview-drop / delivery-throw contract.
    /// </summary>
    public RenderTarget? Acquire(int width, int height, PipelineDiagnostics? diagnostics = null)
    {
        VerifyAccess();

        var key = new BucketKey(width, height, TextureFormat.RGBA16Float, HasSurface: true);
        if (_buckets.TryGetValue(key, out List<PooledSurface>? list) && list.Count > 0)
        {
            PooledSurface pooled = list[^1];
            list.RemoveAt(list.Count - 1);
            if (list.Count == 0)
                _buckets.Remove(key);
            pooled.IsPooled = false;
            _idleBytes -= pooled.ByteSize;
            try
            {
                _clearForReuse(pooled);
            }
            catch
            {
                // The entry has already left its bucket and accounting. Its contents are now unknown, so destroy the
                // backing rather than orphaning it or returning an uncleared surface to a later lease.
                DisposeBacking(pooled);
                throw;
            }
            if (diagnostics != null)
                diagnostics.PoolAcquires++;
            OnLeaseIssued();
            return RenderTarget.WrapPooled(this, pooled);
        }

        if (_backingFactory(width, height) is not { } backing)
            return null;

        var fresh = new PooledSurface(
            backing.Surface, backing.Texture, width, height, TextureFormat.RGBA16Float);
        if (diagnostics != null)
        {
            diagnostics.TargetAllocations++;
            diagnostics.PoolMisses++;
            diagnostics.PoolAcquires++;
        }

        OnLeaseIssued();
        return RenderTarget.WrapPooled(this, fresh);
    }

    /// <summary>
    /// Acquires a pooled surface-less texture (a compute depth attachment) of exactly
    /// <paramref name="width"/> × <paramref name="height"/> × <paramref name="format"/>: pops a matching idle
    /// entry (a hit) or allocates a fresh <see cref="ITexture2D"/> (a miss), with the same counter semantics as
    /// <see cref="Acquire(int, int, PipelineDiagnostics?)"/> (FR-006/C8: every fresh GPU target
    /// creation counts <see cref="PipelineDiagnostics.TargetAllocations"/>). Disposing the returned lease returns
    /// the texture to its bucket. Returns <see langword="null"/> on allocation failure (no counter is touched).
    /// Texture contents are undefined on acquire — a depth attachment is cleared by its render pass.
    /// </summary>
    public PooledTextureLease? AcquireTexture(
        int width, int height, TextureFormat format, PipelineDiagnostics? diagnostics = null)
    {
        VerifyAccess();

        var key = new BucketKey(width, height, format, HasSurface: false);
        if (_buckets.TryGetValue(key, out List<PooledSurface>? list) && list.Count > 0)
        {
            PooledSurface pooled = list[^1];
            list.RemoveAt(list.Count - 1);
            if (list.Count == 0)
                _buckets.Remove(key);
            pooled.IsPooled = false;
            _idleBytes -= pooled.ByteSize;
            if (diagnostics != null)
                diagnostics.PoolAcquires++;
            OnLeaseIssued();
            return new PooledTextureLease(this, pooled);
        }

        ITexture2D? texture = _textureFactory(width, height, format);
        if (texture == null)
            return null;

        var fresh = new PooledSurface(surface: null, texture, width, height, format);
        if (diagnostics != null)
        {
            diagnostics.TargetAllocations++;
            diagnostics.PoolMisses++;
            diagnostics.PoolAcquires++;
        }

        OnLeaseIssued();
        return new PooledTextureLease(this, fresh);
    }

    private void OnLeaseIssued()
    {
        _liveLeases++;
        if (_liveLeases > _peakLiveLeases)
            _peakLiveLeases = _liveLeases;
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
    /// <see cref="RenderTarget"/> and by <see cref="PooledTextureLease.Dispose"/>, never directly by consumers).
    /// Bumps the generation to invalidate any stale lease, stamps the current frame for idle eviction, and
    /// queues it for reuse. The soft byte cap is enforced once at the next frame-boundary <see cref="Trim"/>, so
    /// a frame returning several large buffers evicts them under one <see cref="GpuDisposeBatch"/> drain instead
    /// of synchronously draining once per lease return. If the pool is already disposed the buffer is disposed.
    /// <paramref name="leaseGeneration"/> is the generation the returning lease captured at acquire time: a stale
    /// lease (its buffer force-returned and possibly reissued to a newer lease) is rejected so it can never
    /// re-bucket a buffer the current lease still owns.
    /// </summary>
    internal void Return(PooledSurface pooled, int leaseGeneration)
    {
        // Pool state is render-thread-affine, but a lease's last release can happen on any thread
        // (PooledTextureLease.Dispose runs on the caller thread); marshal like DisposeBacking does.
        if (_dispatcher != null && !_dispatcher.CheckAccess())
        {
            _dispatcher.Dispatch(() => Return(pooled, leaseGeneration));
            return;
        }

        // Idempotent against a double return (a buggy double-dispose, or the generation test seam that
        // deliberately returns a still-leased buffer): a buffer already idle is not re-bucketed, and a stale
        // lease generation means a newer lease owns the buffer now.
        if (pooled.IsPooled || pooled.Generation != leaseGeneration)
            return;

        _liveLeases--;
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

        // One GPU drain for the whole eviction sweep instead of one per evicted buffer (GpuDisposeBatch). The batch
        // drain is lazy, so a sweep that evicts nothing issues no flush.
        using (GpuDisposeBatch.Begin())
        {
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

        RemoveEmptyBuckets();
    }

    // Eviction empties bucket lists in place (EvictAt has no key to remove by); without this per-frame sweep a
    // scene whose buffer size varies continuously (animated bounds → a new key per frame) grows the dictionary
    // without bound and degrades the O(buckets) LRU scan. Dictionary.Remove during enumeration is supported.
    private void RemoveEmptyBuckets()
    {
        foreach ((BucketKey key, List<PooledSurface> list) in _buckets)
        {
            if (list.Count == 0)
                _buckets.Remove(key);
        }
    }

    /// <summary>Test seam: disposes every idle buffer and resets the pool to empty deterministically.</summary>
    public void Clear()
    {
        VerifyAccess();
        DisposeAllIdle();
    }

    private void DisposeAllIdle()
    {
        // Drain once for the whole teardown (GpuDisposeBatch) when it runs on the render thread; a cross-thread
        // Dispose dispatches the disposals and falls back to the per-texture drain.
        using (GpuDisposeBatch.Begin())
        {
            foreach (List<PooledSurface> list in _buckets.Values)
            {
                foreach (PooledSurface pooled in list)
                    DisposeBacking(pooled);
                list.Clear();
            }
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
        Return(pooled, pooled.Generation);
    }

    /// <summary>Test seam: overrides the backing-surface factory to simulate deterministic allocation failure.</summary>
    internal void SetBackingFactoryForTest(Func<int, int, (SKSurface Surface, ITexture2D? Texture)?> factory)
        => _backingFactory = factory;

    /// <summary>
    /// Test seam: lets the first <paramref name="successfulAcquires"/> fresh backing allocations succeed and fails
    /// (returns null) after that, so a test can force an allocation failure at a specific downstream acquire — a
    /// compute pass's output target or a ping-pong scratch — rather than only at the first (input) acquire.
    /// </summary>
    internal void SetBackingFactoryFailingAfterForTest(int successfulAcquires)
    {
        int remaining = successfulAcquires;
        Func<int, int, (SKSurface Surface, ITexture2D? Texture)?> real = RenderTarget.CreateBackingSurface;
        _backingFactory = (w, h) => remaining-- > 0 ? real(w, h) : null;
    }

    /// <summary>Test seam: overrides the surface-less texture factory to simulate deterministic allocation failure.</summary>
    internal void SetTextureFactoryForTest(Func<int, int, TextureFormat, ITexture2D?> factory)
        => _textureFactory = factory;

    /// <summary>Test seam: overrides the reuse clear to simulate a deterministic pooled-hit failure.</summary>
    internal void SetClearForReuseForTest(Action<PooledSurface> clearForReuse)
        => _clearForReuse = clearForReuse;

    // Must fail with null (never throw), matching CreateBackingSurface's failure shape.
    private static ITexture2D? CreateBackingTexture(int width, int height, TextureFormat format)
    {
        try
        {
            IGraphicsContext? context = GraphicsContextFactory.SharedContext;
            return context?.CreateTexture2D(width, height, format);
        }
        catch
        {
            return null;
        }
    }

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
        // SSIM 1.0), so wipe residual content on acquire. Surface-less entries (depth) are cleared by their
        // render pass instead.
        pooled.Surface?.Canvas.Clear(SKColors.Transparent);
    }

    private void VerifyAccess()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        _dispatcher?.VerifyAccess();
    }
}

/// <summary>
/// One reusable GPU buffer owned by a <see cref="RenderTargetPool"/> plus the pooling bookkeeping (generation
/// tag, last-used frame). Two entry kinds share the bucket machinery: a Skia-drawable surface (RGBA16F, with its
/// backing texture) leased as a <see cref="RenderTarget"/>, and a surface-less raw texture (a depth attachment,
/// <see cref="Surface"/> <see langword="null"/>) leased as a <see cref="PooledTextureLease"/>. The pool owns
/// disposal; leases only borrow it.
/// </summary>
internal sealed class PooledSurface(
    SKSurface? surface, ITexture2D? texture, int width, int height, TextureFormat format)
{
    public SKSurface? Surface { get; } = surface;

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

    public BucketKey Key => new(Width, Height, Format, Surface != null);

    public long ByteSize => (long)Width * Height * BytesPerPixel(Format);

    private bool _backingDisposed;

    public void DisposeBacking()
    {
        if (_backingDisposed)
            return;

        _backingDisposed = true;
        DisposeBackings(Surface, Texture);
    }

    internal static void DisposeBackings(IDisposable? surface, IDisposable? texture)
    {
        Exception? failure = null;
        try
        {
            surface?.Dispose();
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        try
        {
            texture?.Dispose();
        }
        catch (Exception ex)
        {
            failure ??= ex;
        }

        if (failure != null)
            ExceptionDispatchInfo.Capture(failure).Throw();
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
internal readonly record struct BucketKey(int Width, int Height, TextureFormat Format, bool HasSurface);

/// <summary>
/// A lease over a pooled surface-less texture (a compute depth attachment) issued by
/// <see cref="RenderTargetPool.AcquireTexture"/>. <see cref="Dispose"/> returns the texture to its bucket;
/// the captured generation rejects access through a lease whose buffer was already returned, mirroring the
/// <see cref="RenderTarget"/> lease guard.
/// </summary>
public sealed class PooledTextureLease : IDisposable
{
    private readonly RenderTargetPool _pool;
    private readonly PooledSurface _pooled;
    private readonly int _generation;
    private bool _disposed;

    internal PooledTextureLease(RenderTargetPool pool, PooledSurface pooled)
    {
        _pool = pool;
        _pooled = pooled;
        _generation = pooled.Generation;
    }

    /// <summary>The leased texture; valid until <see cref="Dispose"/>.</summary>
    public ITexture2D Texture
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_pooled.Generation != _generation)
            {
                throw new ObjectDisposedException(
                    nameof(PooledTextureLease),
                    "This pooled texture lease has expired: the texture was returned to the pool and may have been reissued.");
            }

            return _pooled.Texture!;
        }
    }

    /// <summary>Returns the texture to the pool. Idempotent; safe from any thread (the pool marshals).</summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _pool.Return(_pooled, _generation);
    }
}
