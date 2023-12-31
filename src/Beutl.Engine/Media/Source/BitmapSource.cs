using System.Diagnostics.CodeAnalysis;

using Beutl.Media.Pixel;

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

    public static BitmapSource Open(string fileName)
    {
        var bitmap = Bitmap<Bgra8888>.FromFile(fileName);
        return new BitmapSource(Ref<IBitmap>.Create(bitmap), fileName);
    }

    public static bool TryOpen(string fileName, out BitmapSource? result)
    {
        try
        {
            result = Open(fileName);
            return true;
        }
        catch
        {
            result = null;
            return false;
        }
    }

    public override IImageSource Clone()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

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
