using System.Diagnostics.CodeAnalysis;

using Beutl.Media.Pixel;

namespace Beutl.Media.Source;

public sealed class ImageSource : IImageSource
{
    private readonly Ref<IBitmap> _bitmap;

    public ImageSource(Ref<IBitmap> bitmap, string name)
    {
        _bitmap = bitmap;
        Name = name;
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

    public ImageSource Clone()
    {
        if (IsDisposed)
            throw new ObjectDisposedException(nameof(VideoSource));

        return new ImageSource(_bitmap.Clone(), Name);
    }

    public bool Read([NotNullWhen(true)] out IBitmap? bitmap)
    {
        if (IsDisposed)
        {
            bitmap = null;
            return false;
        }

        bitmap = _bitmap.Value.Clone();
        return true;
    }

    IImageSource IImageSource.Clone() => Clone();
}
