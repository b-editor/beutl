using System.Diagnostics.CodeAnalysis;

namespace Beutl.Media.Source;

public sealed class BitmapSource : ImageSource
{
    private readonly Ref<IBitmap> _bitmap;

    public BitmapSource(Ref<IBitmap> bitmap, string name)
    {
        _bitmap = bitmap.Clone();
        Name = name;
        FrameSize = new PixelSize(_bitmap.Value.Width, _bitmap.Value.Height);
    }

    public override PixelSize FrameSize { get; }

    public override string Name { get; }

    public override bool IsGenerated => true;

    public override IImageSource Clone()
    {
        if (IsDisposed)
            throw new ObjectDisposedException(nameof(VideoSource));

        return new BitmapSource(_bitmap, Name);
    }

    public override bool Read([NotNullWhen(true)] out IBitmap? bitmap)
    {
        if (IsDisposed)
        {
            bitmap = null;
            return false;
        }

        bitmap = _bitmap.Value.Clone();
        return true;
    }

    public override bool TryGetRef([NotNullWhen(true)] out Ref<IBitmap>? bitmap)
    {
        if (IsDisposed)
        {
            bitmap = null;
            return false;
        }

        bitmap = _bitmap.Clone();
        return true;
    }

    protected override void OnDispose(bool disposing)
    {
        _bitmap.Dispose();
    }
}
