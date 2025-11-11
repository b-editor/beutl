using System.Diagnostics.CodeAnalysis;
using Beutl.Media.Pixel;
using Beutl.Serialization;

namespace Beutl.Media.Source;

public sealed class BitmapSource : ImageSource
{
    private Ref<IBitmap>? _bitmap;
    private Uri? _uri;

    public BitmapSource()
    {
    }

    public override PixelSize FrameSize
    {
        get
        {
            if (_bitmap == null) throw new InvalidOperationException("Bitmap is not loaded.");
            return new PixelSize(_bitmap.Value.Width, _bitmap.Value.Height);
        }
    }

    public override Uri Uri => _uri ?? throw new InvalidOperationException("URI is not set.");

    public override bool IsGenerated => _uri != null && _bitmap != null;

    public static BitmapSource Open(string fileName)
    {
        var bitmap = Bitmap<Bgra8888>.FromFile(fileName);
        var source = new BitmapSource
        {
            _bitmap = Ref<IBitmap>.Create(bitmap),
            _uri = new Uri(new Uri("file://"), fileName)
        };
        return source;
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

        return new BitmapSource { _bitmap = _bitmap?.Clone(), _uri = _uri };
    }

    public override void ReadFrom(Uri uri)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        using var stream = UriHelper.ResolveStream(uri);
        var bitmap = Bitmap<Bgra8888>.FromStream(stream);
        _bitmap = Ref<IBitmap>.Create(bitmap);
        _uri = uri;
    }

    public override bool Read([NotNullWhen(true)] out IBitmap? bitmap)
    {
        if (IsDisposed || _bitmap == null)
        {
            bitmap = null;
            return false;
        }

        bitmap = _bitmap.Value.Clone();
        return true;
    }

    public override bool TryGetRef([NotNullWhen(true)] out Ref<IBitmap>? bitmap)
    {
        if (IsDisposed || _bitmap == null)
        {
            bitmap = null;
            return false;
        }

        bitmap = _bitmap.Clone();
        return true;
    }

    protected override void OnDispose(bool disposing)
    {
        _bitmap?.Dispose();
    }

    public override bool Equals(object? obj)
    {
        return obj is BitmapSource source
               && !IsDisposed && !source.IsDisposed
               && ReferenceEquals(_bitmap?.Value, source._bitmap?.Value);
    }

    public override int GetHashCode()
    {
        // ReSharper disable once NonReadonlyMemberInGetHashCode
        return HashCode.Combine(!IsDisposed ? _bitmap?.Value : null);
    }
}
