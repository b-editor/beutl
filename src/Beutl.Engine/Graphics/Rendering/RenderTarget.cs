using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Vulkan;
using Beutl.Media;
using Beutl.Media.Pixel;
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
                surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul));
            }
            else
            {
                RenderThread.Dispatcher.VerifyAccess();
                IGraphicsContext? context = GraphicsContextFactory.GetOrCreateShared();

                if (context != null)
                {
                    sharedTexture = context.CreateTexture2D(width, height, TextureFormat.BGRA8Unorm);
                    surface = sharedTexture.CreateSkiaSurface();
                }
                else
                {
                    surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Bgra8888,
                        SKAlphaType.Unpremul));
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

    public unsafe Bitmap<Bgra8888> Snapshot()
    {
        VerifyAccess();
        PrepareForSampling();
        var result = new Bitmap<Bgra8888>(Width, Height);

        _surface.Value!.ReadPixels(new SKImageInfo(Width, Height, SKColorType.Bgra8888), result.Data,
            result.Width * sizeof(Bgra8888), 0, 0);

        return result;
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

    public void Dispose()
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
        _texture?.Value?.PrepareForRender();
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
