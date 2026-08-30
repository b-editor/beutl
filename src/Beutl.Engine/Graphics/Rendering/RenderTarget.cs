using Beutl.Graphics.Backend;
using Beutl.Media;
using Beutl.Threading;
using SkiaSharp;

namespace Beutl.Graphics.Rendering;

internal readonly struct RenderTargetSamplingIntent
{
    private readonly RenderTargetSamplingIntentKind _kind;
    private readonly GRRecordingContext? _consumerContext;

    private RenderTargetSamplingIntent(
        RenderTargetSamplingIntentKind kind,
        GRRecordingContext? consumerContext = null)
    {
        _kind = kind;
        _consumerContext = consumerContext;
    }

    public static RenderTargetSamplingIntent CpuReadback => default;

    public static RenderTargetSamplingIntent BackendInterop { get; }
        = new(RenderTargetSamplingIntentKind.BackendInterop);

    public static RenderTargetSamplingIntent SameContextTextureSampling(GRRecordingContext? consumerContext)
        => new(RenderTargetSamplingIntentKind.SameContextTextureSampling, consumerContext);

    internal bool RequiresBackendInterop => _kind == RenderTargetSamplingIntentKind.BackendInterop;

    internal bool CanSubmitWithoutCompletion(GRRecordingContext? producerContext)
    {
        if (_kind != RenderTargetSamplingIntentKind.SameContextTextureSampling)
            return false;

        return producerContext is null
            ? _consumerContext is null
            : _consumerContext is not null && producerContext.Handle == _consumerContext.Handle;
    }
}

internal enum RenderTargetSamplingIntentKind : byte
{
    CpuReadback,
    BackendInterop,
    SameContextTextureSampling,
}

public class RenderTarget : IDisposable
{
    private readonly SKSurfaceCounter<SKSurface> _surface;
    private readonly SKSurfaceCounter<ITexture2D>? _texture;
    private readonly Dispatcher? _dispatcher = Dispatcher.Current;
    private bool _hasTransparentContents;

    private RenderTarget(SKSurfaceCounter<SKSurface> surface, int width, int height,
        SKSurfaceCounter<ITexture2D>? texture = null)
    {
        _surface = surface;
        Width = width;
        Height = height;
        _texture = texture;
    }

    /// <summary>
    /// For subclasses (custom allocations / test doubles). Wraps a raw <paramref name="surface"/>
    /// with no shared texture. The surface is released by <see cref="Dispose()"/> unless a
    /// subclass overrides it.
    /// </summary>
    protected RenderTarget(SKSurface surface, int width, int height)
        : this(new SKSurfaceCounter<SKSurface>(surface), width, height)
    {
    }

    ~RenderTarget()
    {
        Dispose(disposing: false);
    }

    internal SKSurface Value
    {
        get
        {
            SKSurface surface = RawValue;
            ITexture2D? texture = _texture?.Value;
            if (texture is ITransparentClearableTexture { HasTransparentContents: true })
            {
                // Value exposes the mutable Skia surface directly. Submit a pending transparent
                // initialization before an unwrapped Canvas operation can overtake that clear.
                texture.PrepareForSkiaRendering();
            }
            _hasTransparentContents = false;
            return surface;
        }
    }

    internal SKSurface RawValue =>
        !IsDisposed ? _surface.Value! : throw new ObjectDisposedException(nameof(RenderTarget));

    public int Width { get; }

    public int Height { get; }

    public bool IsDisposed { get; protected set; }

    /// <summary>
    /// Whether another live holder shares this instance's backing-surface reference count, so
    /// <see cref="Dispose()"/> drops only this reference instead of releasing the surface.
    /// </summary>
    internal bool SharesSurfaceOwnership => _surface.RefCount > 1;

    internal ITexture2D? Texture => _texture?.Value;

    /// <summary>
    /// Whether <see cref="Create"/> would attach a new target to a graphics context rather than raster it on
    /// the CPU.
    /// </summary>
    /// <remarks>
    /// Only a caller on a dispatcher reaches a graphics context; anywhere else <see cref="Create"/> rasters,
    /// and no device's attachment limit bounds what it allocates there. A caller budgeting an allocation
    /// reads this rather than deciding for itself which path runs, so the budget and the allocation cannot
    /// answer differently.
    /// </remarks>
    internal static bool CreateAttachesToGraphicsContext => Dispatcher.Current is not null;

    /// <summary>
    /// The context <see cref="Create"/> would attach a new target to out of <paramref name="sharedContext"/>,
    /// or <see langword="null"/> when it would raster that target on the CPU instead.
    /// </summary>
    /// <param name="sharedContext">The shared context that applies where one is reached at all.</param>
    internal static IGraphicsContext? ResolveCreationContext(IGraphicsContext? sharedContext)
        => CreateAttachesToGraphicsContext ? sharedContext : null;

    public static RenderTarget? Create(int width, int height)
    {
        try
        {
            ITexture2D? sharedTexture = null;

            // Asking for the shared context is itself dispatcher-bound, so which path runs has to be settled
            // before GetOrCreateShared is reached rather than by what it answers.
            IGraphicsContext? context = null;
            if (CreateAttachesToGraphicsContext)
            {
                RenderThread.Dispatcher.VerifyAccess();
                context = GraphicsContextFactory.GetOrCreateShared();
            }

            SKSurface? surface = context != null
                ? CreateSharedSurface(context, width, height, out sharedTexture)
                : SKSurface.Create(new SKImageInfo(
                    width, height, SKColorType.RgbaF16, SKAlphaType.Premul, SKColorSpace.CreateSrgbLinear()));

            if (surface == null)
                return null;

            // Skia refcounts the surface itself and only borrows the image behind it, so the
            // backend texture is the one resource that can outlive its last managed reference.
            var textureRef = sharedTexture != null
                ? new SKSurfaceCounter<ITexture2D>(
                    sharedTexture,
                    deferRelease: true,
                    approximateBytes: (long)width * height * 8)
                : null;

            var result = new RenderTarget(
                new SKSurfaceCounter<SKSurface>(surface),
                width,
                height,
                textureRef);
            try
            {
                if (!result.HasTransparentContents)
                    result.ClearToTransparent();
            }
            catch
            {
                // The clear submits to the device, so it is the step that fails when the device is
                // already lost - the one time a caller retries every frame. Degrading to null without
                // releasing what the target owns strands a surface and an image per attempt until a
                // finalizer runs, which is the worst moment to be leaking device memory.
                result.Dispose();
                throw;
            }

            return result;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Creates the backend texture and its Skia surface, releasing both when initialization fails.
    /// </summary>
    /// <remarks>
    /// The backend texture has no finalizer, so escaping this method before the texture reaches a
    /// <see cref="RenderTarget"/> strands its image, view and device memory for the life of the process.
    /// </remarks>
    /// <returns>
    /// The surface wrapping a new backend texture, or <see langword="null"/> when the backend declined to
    /// wrap it, in which case the texture has already been released.
    /// </returns>
    internal static SKSurface? CreateSharedSurface(
        IGraphicsContext context,
        int width,
        int height,
        out ITexture2D? texture)
    {
        ITexture2D createdTexture = context.CreateTexture2D(width, height, TextureFormat.RGBA16Float);
        texture = createdTexture;
        SKSurface? surface = null;
        try
        {
            surface = createdTexture.CreateSkiaSurface();
            if (surface is null)
            {
                // The backend texture is the one resource here that outlives its last managed reference, so
                // a wrap the driver declined has to release it rather than leave it to a finalizer.
                createdTexture.Dispose();
                texture = null;
                return null;
            }

            // Surface wrapping marks Skia access. Record initialization afterwards so an
            // untouched snapshot still observes and submits the backend clear.
            if (createdTexture is ITransparentClearableTexture clearableTexture)
                clearableTexture.ClearToTransparent();

            return surface;
        }
        catch
        {
            // The surface only borrows the backend image, so it has to go first — the same order
            // Release uses.
            try
            {
                surface?.Dispose();
            }
            finally
            {
                createdTexture.Dispose();
            }

            throw;
        }
    }

    public static RenderTarget CreateNull(int width, int height)
    {
        var surface = SKSurface.CreateNull(width, height);
        return new RenderTarget(new SKSurfaceCounter<SKSurface>(surface), width, height);
    }

    public static RenderTarget GetRenderTarget(ImmediateCanvas canvas)
    {
        canvas.VerifyAccess();
        return canvas._renderTarget.ShallowCopy();
    }

    public Bitmap Snapshot()
    {
        VerifyAccess();
        PrepareForSampling(RenderTargetSamplingIntent.CpuReadback);
        var result = CreateSnapshotBitmap();
        return ReadInto(result);
    }

    /// <summary>
    /// Reads the current surface directly into a one-byte-per-pixel alpha bitmap.
    /// </summary>
    /// <remarks>
    /// The GPU backend converts the render target's RgbaF16 pixels to Alpha8 during readback, so
    /// callers that inspect only coverage avoid transferring and converting the color channels.
    /// This is a synchronous CPU readback and waits for submitted rendering to complete.
    /// </remarks>
    public Bitmap SnapshotAlpha()
    {
        VerifyAccess();
        PrepareForSampling(RenderTargetSamplingIntent.CpuReadback);
        var result = new Bitmap(
            Width,
            Height,
            BitmapColorType.Alpha8,
            BitmapAlphaType.Premul,
            BitmapColorSpace.LinearSrgb);
        return ReadInto(result);
    }

    /// <summary>
    /// Fills a bitmap this method owns, releasing it if the readback fails.
    /// </summary>
    /// <remarks>
    /// A failed readback is a device-loss symptom, and a caller that snapshots per frame retries it per
    /// frame. Propagating without releasing leaves one full-frame native bitmap behind per attempt for a
    /// finalizer to find, which is the worst moment to be holding them. <see cref="SnapshotInto(Bitmap)"/>
    /// does not go through here: its destination belongs to the caller, who keeps it either way.
    /// </remarks>
    private Bitmap ReadInto(Bitmap result)
    {
        try
        {
            ReadPixelsInto(result);
        }
        catch
        {
            result.Dispose();
            throw;
        }

        return result;
    }

    /// <summary>
    /// Allocates a bitmap in the exact format <see cref="Snapshot()"/> produces
    /// (RgbaF16/Premul/LinearSrgb at the render target size). The single source of truth for that
    /// format — callers pre-allocating a destination for <see cref="SnapshotInto(Bitmap)"/> should use
    /// this instead of hardcoding it, so the destination cannot drift out of sync with the surface.
    /// </summary>
    public Bitmap CreateSnapshotBitmap() =>
        new(Width, Height, BitmapColorType.RgbaF16, BitmapAlphaType.Premul, BitmapColorSpace.LinearSrgb);

    /// <summary>
    /// Reads the current surface into an existing <paramref name="destination"/> bitmap so
    /// repeat-snapshot callers (e.g. onion-skin compositing) can reuse one scratch bitmap and avoid
    /// Large Object Heap churn. The destination must match the render target size and be in the same
    /// RgbaF16/Premul/LinearSrgb format produced by <see cref="Snapshot()"/>.
    /// </summary>
    public void SnapshotInto(Bitmap destination)
    {
        VerifyAccess();
        ArgumentNullException.ThrowIfNull(destination);
        if (destination.Width != Width || destination.Height != Height)
        {
            throw new ArgumentException(
                $"Destination bitmap size ({destination.Width}x{destination.Height}) must match the render target size ({Width}x{Height}).",
                nameof(destination));
        }

        // Keep the reusable full-color snapshot contract exact even though Skia can convert during
        // ReadPixels; alpha-only callers use SnapshotAlpha instead.
        if (destination.ColorType != BitmapColorType.RgbaF16
            || destination.AlphaType != BitmapAlphaType.Premul
            || !destination.ColorSpace.Equals(BitmapColorSpace.LinearSrgb))
        {
            throw new ArgumentException(
                "Destination bitmap must be RgbaF16/Premul/LinearSrgb to match the render target surface format.",
                nameof(destination));
        }

        PrepareForSampling(RenderTargetSamplingIntent.CpuReadback);
        ReadPixelsInto(destination);
    }

    private void ReadPixelsInto(Bitmap destination)
    {
        SKImageInfo readInfo = destination.SKBitmap.Info;
        if (!_surface.Value!.ReadPixels(readInfo, destination.Data, destination.RowBytes, 0, 0))
        {
            // Readback failed; the destination still holds stale pixels. Throw rather than
            // silently compositing them.
            throw new InvalidOperationException(
                "Failed to read the render target surface into the destination bitmap.");
        }
    }

    public RenderTarget ShallowCopy()
    {
        _surface.AddRef();
        _texture?.AddRef();
        return new RenderTarget(_surface, Width, Height, _texture)
        {
            _hasTransparentContents = _hasTransparentContents,
        };
    }

    public void VerifyAccess()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        _dispatcher?.VerifyAccess();
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases the backing surface and texture. Subclasses (custom allocations / test doubles)
    /// override this to customize disposal semantics; an override must call
    /// <see langword="base"/>.<see cref="Dispose(bool)"/>, or the object stays finalizable and the
    /// finalizer re-enters the override. When <paramref name="disposing"/> is <see langword="false"/>
    /// (finalizer-driven), overrides must not throw and must not touch finalized managed state.
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (IsDisposed) return;

        IsDisposed = true;

        // Skia's GPU context is thread-affine, so the surface and its shared texture have to be
        // released on the dispatcher that allocated them — releasing from another thread corrupts
        // the context and faults the render thread later.
        SKSurfaceCounter<SKSurface> surface = _surface;
        SKSurfaceCounter<ITexture2D>? texture = _texture;

        if (!disposing)
        {
            GpuResourceRelease.DispatchFinalizer(_dispatcher, () => Release(surface, texture));
            return;
        }

        GpuResourceRelease.Run(_dispatcher, () => Release(surface, texture));
    }

    private static void Release(SKSurfaceCounter<SKSurface> surface, SKSurfaceCounter<ITexture2D>? texture)
    {
        try
        {
            surface.Release();
        }
        finally
        {
            texture?.Release();
        }
    }

    internal void BeginDraw()
    {
        VerifyAccess();

        _hasTransparentContents = false;
        _texture?.Value?.PrepareForSkiaRendering();
    }

    internal void PrepareBackendForSkiaSampling()
    {
        VerifyAccess();
        _texture?.Value?.PrepareForSkiaSampling(requireCompletion: false);
    }

    internal bool HasTransparentContents
        => _texture?.Value is ITransparentClearableTexture clearableTexture
            ? clearableTexture.HasTransparentContents
            : _hasTransparentContents;

    internal void ClearToTransparent()
    {
        VerifyAccess();
        BeginDraw();
        _surface.Value!.Canvas.Clear(SKColors.Transparent);

        // A canvas clear is a deferred Skia draw, and a custom effect that writes this image through
        // the Vulkan backend does so outside Skia's task graph. PrepareForSampling only covers a
        // target used as a source, so a write destination would keep the clear pending until after
        // the native writer had already filled it. Submitting it here, without the sampling
        // bookkeeping, keeps the clear ahead of any such writer.
        _surface.Value.Flush(true, false);
        _hasTransparentContents = true;

        // HasTransparentContents prefers the backend's record when there is one, and the clear above went
        // through Skia, which the backend cannot observe. Telling it here is what keeps a reused pooled
        // target from being cleared a second time by the next caller that wants a blank one.
        if (_texture?.Value is ITransparentClearableTexture clearableTexture)
            clearableTexture.MarkContentsTransparent();
    }

    internal void PrepareForSampling(RenderTargetSamplingIntent intent)
    {
        VerifyAccess();

        bool waitForCompletion = !intent.CanSubmitWithoutCompletion(_surface.Value!.Context);
        ITexture2D? texture = _texture?.Value;

        if (intent.RequiresBackendInterop
            && texture is { RequiresSkiaFlushForBackendInterop: false })
        {
            // A backend-produced target can remain in the same recording batch. There is no Skia
            // work to submit between consecutive native passes.
            texture.PrepareForSampling();

            if (texture is ITransparentClearableTexture { HasTransparentContents: true })
            {
                // A clear-only target can be exposed directly, with no following native pass to
                // submit its initialization. Preserve the completion boundary for that exposure,
                // then restore Vulkan ownership without consuming a second recording batch.
                texture.PrepareForSkiaSampling(requireCompletion: true);
                texture.PrepareForSampling();
                ImmediateCanvas.RecordFlush(ImmediateCanvasFlushKind.PrepareForSampling);
            }
            return;
        }

        if (!intent.RequiresBackendInterop)
        {
            // Submit backend writes before Skia records a dependent read. CPU readback waits here;
            // same-context GPU sampling relies on queue order and does not stall the CPU.
            texture?.PrepareForSkiaSampling(waitForCompletion);
        }

        // A context-wide flush is a superset of this surface's, so reclaiming deferred targets here
        // replaces the surface flush instead of adding a second submit - but only when it flushed this
        // surface's own context. A target from a caller-supplied factory can live on another one.
        if (GpuResourceReclaimQueue.FlushAndDrain(_surface.Value!.Context))
        {
            waitForCompletion = true;
        }
        else
        {
            _surface.Value.Flush(true, waitForCompletion);
        }

        ImmediateCanvas.RecordFlush(waitForCompletion
            ? ImmediateCanvasFlushKind.PrepareForSampling
            : ImmediateCanvasFlushKind.PrepareForSamplingSubmit);
        if (intent.RequiresBackendInterop)
        {
            // The caller is about to touch the texture through the backend, which does not route
            // through BeginDraw, so the transparency tracking cannot survive it.
            _hasTransparentContents = false;
            texture?.PrepareForSampling();
        }
    }

    private sealed class SKSurfaceCounter<T>(T value, bool deferRelease = false, long approximateBytes = 0)
        where T : class, IDisposable
    {
        private readonly Dispatcher? _dispatcher = Dispatcher.Current;
        private volatile int _refs = 1;

        public T? Value { get; private set; } = value;

        public int RefCount => _refs;

        public void AddRef()
        {
            int old = _refs;
            while (true)
            {
                ObjectDisposedException.ThrowIf(old == 0, this);
                int current = Interlocked.CompareExchange(ref _refs, old + 1, old);
                if (current == old)
                {
                    break;
                }

                old = current;
            }
        }

        public void Release()
        {
            int old = _refs;
            while (true)
            {
                int current = Interlocked.CompareExchange(ref _refs, old - 1, old);

                if (current == old)
                {
                    if (old == 1)
                    {
                        var value = Value;
                        Value = null;
                        if (value != null)
                        {
                            // Finished, not Started: between the two the owner thread is still
                            // running an operation, so disposing here could free a surface in use.
                            // Past Finished, dispatching would queue onto a loop that no longer
                            // drains and the native resource would outlive the process instead.
                            if (_dispatcher is { HasShutdownFinished: false } dispatcher
                                && !dispatcher.CheckAccess())
                            {
                                dispatcher.Dispatch(() => ReleaseValue(value));
                            }
                            else
                            {
                                ReleaseValue(value);
                            }
                        }
                    }

                    break;
                }

                old = current;
            }
        }

        private void ReleaseValue(T value)
        {
            if (deferRelease && GpuResourceReclaimQueue.TryDefer(value, approximateBytes))
            {
                return;
            }

            value.Dispose();
        }
    }
}
