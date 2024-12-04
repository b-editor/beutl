using Beutl.Threading;
using OpenTK.Graphics.OpenGL;

namespace Beutl.Graphics3D;

public sealed class DepthBuffer : IDisposable
{
    private readonly Dispatcher _dispatcher = Dispatcher.Current;

    public DepthBuffer(int width, int height)
    {
        if (_dispatcher is null) throw new InvalidOperationException("Dispatcher is not initialized.");

        Handle = GL.GenRenderbuffer();
        (Width, Height) = (width, height);
        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, Handle);
        GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.DepthComponent, Width, Height);
        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0);

        GlErrorHelper.CheckGlError();
    }

    ~DepthBuffer()
    {
        Dispose();
    }

    public int Width { get; }

    public int Height { get; }

    public int Handle { get; }

    public bool IsDisposed { get; private set; }

    public void Bind()
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(ColorBuffer));

        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, Handle);
        GlErrorHelper.CheckGlError();
    }

    public void Dispose()
    {
        if (IsDisposed) return;

        _dispatcher.Run(() => GL.DeleteRenderbuffer(Handle));

        GC.SuppressFinalize(this);

        IsDisposed = true;
    }
}
