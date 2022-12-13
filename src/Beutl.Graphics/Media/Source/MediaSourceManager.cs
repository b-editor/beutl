using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

using Beutl.Media.Decoding;
using Beutl.Media.Pixel;

namespace Beutl.Media.Source;

public class MediaSourceManager
{
    private readonly Dictionary<string, Ref<MediaReader>> _mediaReaders = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Ref<IBitmap>> _bitmaps = new(StringComparer.Ordinal);

    public static readonly MediaSourceManager Shared = new();

    public bool OpenVideoSource(string name, [NotNullWhen(true)] out IVideoSource? value)
    {
        value = null;
        if (TryGetMediaReaderOrOpen(name, out Ref<MediaReader>? mediaReader))
        {
            value = new VideoSource(mediaReader, name);
        }

        return value != null;
    }

    public bool OpenSoundSource(string name, [NotNullWhen(true)] out ISoundSource? value)
    {
        value = null;
        if (TryGetMediaReaderOrOpen(name, out Ref<MediaReader>? mediaReader))
        {
            value = new SoundSource(mediaReader, name);
        }

        return value != null;
    }

    public bool OpenImageSource(string name, [NotNullWhen(true)] out IImageSource? value)
    {
        value = null;
        if (TryGetBitmapOrOpen(name, out Ref<IBitmap>? bitmap))
        {
            value = new ImageSource(bitmap, name);
        }

        return value != null;
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool TryGetMediaReader(string name, [NotNullWhen(true)] out Ref<MediaReader>? value)
    {
        value = null;
        if (_mediaReaders.TryGetValue(name, out Ref<MediaReader>? result))
        {
            value = result.Clone();
        }

        return value != null;
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool TryGetMediaReaderOrOpen(string fileName, [NotNullWhen(true)] out Ref<MediaReader>? value)
    {
        value = null;
        if (_mediaReaders.TryGetValue(fileName, out Ref<MediaReader>? result))
        {
            value = result.Clone();
        }
        else
        {
            try
            {
                var reader = MediaReader.Open(fileName);

                value = Ref<MediaReader>.Create(reader, () => _mediaReaders.Remove(fileName));
                _mediaReaders.Add(fileName, value);
            }
            catch
            {
            }
        }

        return value != null;
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool TryGetBitmap(string name, [NotNullWhen(true)] out Ref<IBitmap>? value)
    {
        value = null;
        if (_bitmaps.TryGetValue(name, out Ref<IBitmap>? result))
        {
            value = result.Clone();
        }

        return value != null;
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool TryGetBitmapOrOpen(string fileName, [NotNullWhen(true)] out Ref<IBitmap>? value)
    {
        value = null;
        if (_bitmaps.TryGetValue(fileName, out Ref<IBitmap>? result))
        {
            value = result.Clone();
        }
        else
        {
            try
            {
                var bitmap = Bitmap<Bgra8888>.FromFile(fileName);

                value = Ref<IBitmap>.Create(bitmap, () => _bitmaps.Remove(fileName));
                _bitmaps.Add(fileName, value);
            }
            catch
            {
            }
        }

        return value != null;
    }

    // 移譲する側は今後、valueを直接参照しない。
    [EditorBrowsable(EditorBrowsableState.Never)]
    public Ref<MediaReader>? TryTransferMediaReader(string name, MediaReader mediaReader)
    {
        if (mediaReader.IsDisposed
            || _mediaReaders.ContainsKey(name)
            || _mediaReaders.Values.Any(x => ReferenceEquals(x.Value, mediaReader)))
        {
            return null;
        }
        else
        {
            var @ref = Ref<MediaReader>.Create(mediaReader, () => _mediaReaders.Remove(name));
            _mediaReaders.Add(name, @ref);
            return @ref;
        }
    }

    // 移譲する側は今後、valueを直接参照しない。
    [EditorBrowsable(EditorBrowsableState.Never)]
    public Ref<IBitmap>? TryTransferBitmap(string name, IBitmap bitmap)
    {
        if (bitmap.IsDisposed
            || _bitmaps.ContainsKey(name)
            || _bitmaps.Values.Any(x => ReferenceEquals(x.Value, bitmap)))
        {
            return null;
        }
        else
        {
            var @ref = Ref<IBitmap>.Create(bitmap, () => _bitmaps.Remove(name));
            _bitmaps.Add(name, @ref);
            return @ref;
        }
    }

    public bool ContainsMediaReader(string name)
    {
        return _mediaReaders.ContainsKey(name);
    }

    public bool ContainsBitmap(string name)
    {
        return _bitmaps.ContainsKey(name);
    }
}
