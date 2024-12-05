using Beutl.Threading;
using OpenTK.Graphics.OpenGL;

namespace Beutl.Graphics3D;

public sealed class PixelBuffer : IDisposable
{
    private readonly Dispatcher _dispatcher = Dispatcher.Current;

    public PixelBuffer(int width, int height, int channel, PixelFormat format, PixelType type)
    {
        if (_dispatcher is null) throw new InvalidOperationException("Dispatcher is not initialized.");
        (Width, Height, Channel, Format, Type) = (width, height, channel, format, type);
        BufferSize = width * height * channel * GetPixelSize(type);

        Handle = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.PixelPackBuffer, Handle);
        GL.BufferData(BufferTarget.PixelPackBuffer, BufferSize, IntPtr.Zero, BufferUsage.StreamRead);
        GL.BindBuffer(BufferTarget.PixelPackBuffer, 0);

        GlErrorHelper.CheckGlError();
    }

    ~PixelBuffer()
    {
        Dispose();
    }

    public int Width { get; }

    public int Height { get; }

    public int Channel { get; }

    public int BufferSize { get; }

    public int Handle { get; }

    public PixelFormat Format { get; }

    public PixelType Type { get; }

    public bool IsDisposed { get; private set; }

    public unsafe void ReadPixelsFromBuffer(int handle, IntPtr data)
    {
        GL.BindBuffer(BufferTarget.PixelPackBuffer, Handle);
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, handle);
        GL.ReadPixels(0, 0, Width, Height, Format, Type, 0);

        var pboPtr = (byte*)GL.MapBufferRange(BufferTarget.PixelPackBuffer, 0, BufferSize, MapBufferAccessMask.MapReadBit);

        if (pboPtr is not null)
        {
            System.Buffer.MemoryCopy(pboPtr, (void*)data, BufferSize, BufferSize);
            GL.UnmapBuffer(BufferTarget.PixelPackBuffer);
        }

        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);
        GL.BindBuffer(BufferTarget.PixelPackBuffer, 0);
    }

    public void Dispose()
    {
        if (IsDisposed) return;

        _dispatcher.Run(() =>
        {
            GL.BindBuffer(BufferTarget.ArrayBuffer, Handle);
            GL.DeleteBuffer(Handle);
        });

        GC.SuppressFinalize(this);

        IsDisposed = true;
    }

    private static int GetPixelSize(PixelType type)
    {
        return type switch
        {
            PixelType.Float => sizeof(float),
            PixelType.UnsignedByte => sizeof(byte),
            PixelType.Byte => sizeof(sbyte),
            PixelType.Short => sizeof(short),
            PixelType.UnsignedShort => sizeof(ushort),
            PixelType.Int => sizeof(int),
            PixelType.UnsignedInt => sizeof(uint),
            PixelType.HalfFloat => sizeof(float) / 2,
            _ => sizeof(int),
        };
    }
}
