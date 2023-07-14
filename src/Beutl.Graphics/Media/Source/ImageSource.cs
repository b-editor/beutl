using System.Diagnostics.CodeAnalysis;

using Beutl.Media.Pixel;

namespace Beutl.Media.Source;

public abstract class ImageSource : IImageSource
{
    ~ImageSource()
    {
        if (!IsDisposed)
        {
            OnDispose(false);
            IsDisposed = true;
        }
    }

    public abstract PixelSize FrameSize { get; }

    public abstract bool IsGenerated { get; }

    public abstract string Name { get; }

    public bool IsDisposed { get; private set; }

    public abstract IImageSource Clone();

    public abstract bool Read([NotNullWhen(true)] out IBitmap? bitmap);

    public abstract bool TryGetRef([NotNullWhen(true)] out Ref<IBitmap>? bitmap);

    protected abstract void OnDispose(bool disposing);

    public void Dispose()
    {
        if (!IsDisposed)
        {
            OnDispose(true);
            GC.SuppressFinalize(this);
            IsDisposed = true;
        }
    }
}
