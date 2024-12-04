using Beutl.Threading;
using OpenTK.Graphics.OpenGL;

namespace Beutl.Graphics3D;

public sealed class ColorBuffer : IDisposable
{
    private readonly Dispatcher _dispatcher = Dispatcher.Current;

    public ColorBuffer(int width, int height, InternalFormat internalFormat, PixelFormat format, PixelType type)
    {
        if (_dispatcher is null) throw new InvalidOperationException("Dispatcher is not initialized.");
        
        Handle = GL.GenTexture();
        (Width, Height, InternalFormat, Format, Type) = (width, height, internalFormat, format, type);
        GL.BindTexture(TextureTarget.Texture2d, Handle);
        GL.TexImage2D(TextureTarget.Texture2d, 0, internalFormat, width, height, 0, format, type, IntPtr.Zero);
        GL.TexParameteri(TextureTarget.Texture2d, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameteri(TextureTarget.Texture2d, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameteri(TextureTarget.Texture2d, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameteri(TextureTarget.Texture2d, TextureParameterName.TextureMagFilter, (int)TextureMinFilter.Linear);
        GL.BindTexture(TextureTarget.Texture2d, 0);

        GlErrorHelper.CheckGlError();
    }

    ~ColorBuffer()
    {
        Dispose();
    }

    public int Width { get; }

    public int Height { get; }

    public InternalFormat InternalFormat { get; }

    public PixelFormat Format { get; }

    public PixelType Type { get; }

    public int Handle { get; }

    public bool IsDisposed { get; private set; }

    public void Bind()
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(ColorBuffer));

        GL.BindTexture(TextureTarget.Texture2d, Handle);
        GlErrorHelper.CheckGlError();
    }

    public void Dispose()
    {
        if (IsDisposed) return;

        _dispatcher.Run(() => GL.DeleteTexture(Handle));

        GC.SuppressFinalize(this);

        IsDisposed = true;
    }
}
