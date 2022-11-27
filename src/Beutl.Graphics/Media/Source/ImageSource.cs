using System.Diagnostics.CodeAnalysis;

using Beutl.Media.Pixel;

namespace Beutl.Media.Source;

public sealed class ImageSource : IImageSource
{
    private readonly MediaSourceManager.Ref<Bitmap<Bgra8888>> _bitmap;

    public ImageSource(MediaSourceManager.Ref<Bitmap<Bgra8888>> bitmap, string fileName)
    {
        _bitmap = bitmap;
        Name = fileName;
        FrameSize = new PixelSize(_bitmap.Value.Width, _bitmap.Value.Height);
    }

    public PixelSize FrameSize { get; }

    public bool IsDisposed { get; private set; }

    public string Name { get; }

    public void Dispose()
    {
        if (!IsDisposed)
        {
            _bitmap.Dispose();
            GC.SuppressFinalize(this);
            IsDisposed = true;
        }
    }

    public bool Read([NotNullWhen(true)] out IBitmap? bitmap)
    {
        if (IsDisposed)
        {
            bitmap = null;
            return false;
        }

        bitmap = _bitmap.Value;
        return true;
    }
}
