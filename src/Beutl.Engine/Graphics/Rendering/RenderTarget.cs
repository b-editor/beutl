using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Vulkan;
using Beutl.Media;
using Beutl.Threading;
using SkiaSharp;

namespace Beutl.Graphics.Rendering;

public class RenderTarget : IDisposable
{
    private readonly SKSurfaceCounter<SKSurface> _surface;
    private readonly SKSurfaceCounter<ITexture2D>? _texture;
    private readonly Dispatcher? _dispatcher = Dispatcher.Current;

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
        Dispose();
    }

    internal SKSurface Value =>
        !IsDisposed ? _surface.Value! : throw new ObjectDisposedException(nameof(RenderTarget));

    public int Width { get; }

    public int Height { get; }

    public bool IsDisposed { get; private set; }

    internal ITexture2D? Texture => _texture?.Value;

    public static RenderTarget? Create(int width, int height)
    {
        try
        {
            SKSurface? surface;
            ITexture2D? sharedTexture = null;
            if (Dispatcher.Current == null)
            {
                surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.RgbaF16, SKAlphaType.Premul, SKColorSpace.CreateSrgbLinear()));
            }
            else
            {
                RenderThread.Dispatcher.VerifyAccess();
                IGraphicsContext? context = GraphicsContextFactory.GetOrCreateShared();

                if (context != null)
                {
                    sharedTexture = context.CreateTexture2D(width, height, TextureFormat.RGBA16Float);
                    surface = sharedTexture.CreateSkiaSurface();
                }
                else
                {
                    surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.RgbaF16,
                        SKAlphaType.Premul, SKColorSpace.CreateSrgbLinear()));
                }
            }

            var textureRef = sharedTexture != null ? new SKSurfaceCounter<ITexture2D>(sharedTexture) : null;
            return surface == null
                ? null
                : new RenderTarget(new SKSurfaceCounter<SKSurface>(surface), width, height, textureRef);
        }
        catch
        {
            return null;
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
        PrepareForSampling();
        var result = CreateSnapshotBitmap();
        ReadPixelsInto(result);
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

        // ReadPixels does not convert formats or color spaces, so require the exact format that
        // Snapshot() allocates (RgbaF16 / Premul / LinearSrgb).
        if (destination.ColorType != BitmapColorType.RgbaF16
            || destination.AlphaType != BitmapAlphaType.Premul
            || !destination.ColorSpace.Equals(BitmapColorSpace.LinearSrgb))
        {
            throw new ArgumentException(
                "Destination bitmap must be RgbaF16/Premul/LinearSrgb to match the render target surface format.",
                nameof(destination));
        }

        PrepareForSampling();
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
        return new RenderTarget(_surface, Width, Height, _texture);
    }

    public void VerifyAccess()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        _dispatcher?.VerifyAccess();
    }

    public virtual void Dispose()
    {
        if (IsDisposed) return;

        _surface.Release();
        _texture?.Release();
        IsDisposed = true;
        GC.SuppressFinalize(this);
    }

    internal void BeginDraw()
    {
        VerifyAccess();

        _texture?.Value?.PrepareForRender();
    }

    internal void PrepareForSampling()
    {
        VerifyAccess();

        _surface.Value!.Flush(true, true);
        _texture?.Value?.PrepareForSampling();
    }

    private sealed class SKSurfaceCounter<T>(T value)
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
                            if (_dispatcher != null)
                            {
                                if (_dispatcher.CheckAccess())
                                {
                                    value.Dispose();
                                }
                                else
                                {
                                    _dispatcher.Dispatch(value.Dispose);
                                }
                            }
                            else
                            {
                                value.Dispose();
                            }
                        }
                    }

                    break;
                }

                old = current;
            }
        }
    }
}
