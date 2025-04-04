using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.Threading;
using SkiaSharp;

namespace Beutl.Graphics.Rendering;

public class RenderTarget : IDisposable
{
    private readonly SKSurfaceCounter _surface;
    private readonly Dispatcher? _dispatcher = Dispatcher.Current;

    private RenderTarget(SKSurfaceCounter surface, int width, int height)
    {
        _surface = surface;
        Width = width;
        Height = height;
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

    public static RenderTarget? Create(int width, int height)
    {
        try
        {
            SKSurface? surface;
            if (Dispatcher.Current == null)
            {
                surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul));
            }
            else
            {
                RenderThread.Dispatcher.VerifyAccess();
                GRContext? grContext = SharedGRContext.GetOrCreate();

                if (grContext != null)
                {
                    surface = SKSurface.Create(
                        grContext,
                        false,
                        new SKImageInfo(width, height, SKColorType.Bgra8888 /*, SKAlphaType.Unpremul*/));
                }
                else
                {
                    surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Bgra8888,
                        SKAlphaType.Unpremul));
                }
            }

            return surface == null ? null : new RenderTarget(new SKSurfaceCounter(surface), width, height);
        }
        catch
        {
            return null;
        }
    }

    public static RenderTarget CreateNull(int width, int height)
    {
        var surface = SKSurface.CreateNull(width, height);
        return new RenderTarget(new SKSurfaceCounter(surface), width, height);
    }

    public static RenderTarget GetRenderTarget(ImmediateCanvas canvas)
    {
        canvas.VerifyAccess();
        return canvas._renderTarget.ShallowCopy();
    }

    public unsafe Bitmap<Bgra8888> Snapshot()
    {
        VerifyAccess();
        var result = new Bitmap<Bgra8888>(Width, Height);

        _surface.Value!.ReadPixels(new SKImageInfo(Width, Height, SKColorType.Bgra8888), result.Data,
            result.Width * sizeof(Bgra8888), 0, 0);

        return result;
    }

    public RenderTarget ShallowCopy()
    {
        _surface.AddRef();
        return new RenderTarget(_surface, Width, Height);
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
        IsDisposed = true;
        GC.SuppressFinalize(this);
    }

    private sealed class SKSurfaceCounter(SKSurface value)
    {
        private readonly Dispatcher? _dispatcher = Dispatcher.Current;
        private volatile int _refs = 1;

        public SKSurface? Value { get; private set; } = value;

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
