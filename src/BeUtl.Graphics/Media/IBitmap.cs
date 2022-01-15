using BeUtl.Media.Pixel;

namespace BeUtl.Media;

public interface IBitmap : IDisposable, ICloneable
{
    int Width { get; }

    int Height { get; }

    int ByteCount { get; }

    int PixelSize { get; }

    IntPtr Data { get; }

    bool IsDisposed { get; }

    Bitmap<T> Convert<T>() where T : unmanaged, IPixel<T>;
}
