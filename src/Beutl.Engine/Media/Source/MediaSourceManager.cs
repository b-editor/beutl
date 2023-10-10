using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

using Beutl.Media.Decoding;
using Beutl.Media.Pixel;

namespace Beutl.Media.Source;

[EditorBrowsable(EditorBrowsableState.Never)]
[Obsolete("Use the Open method of each MediaSource.")]
public class MediaSourceManager
{
    public static readonly MediaSourceManager Shared = new();

    [Obsolete("Use VideoSource.Open")]
    public bool OpenVideoSource(string name, [NotNullWhen(true)] out IVideoSource? value)
    {
        value = null;
        if (TryGetMediaReaderOrOpen(name, out Ref<MediaReader>? mediaReader))
        {
            value = new VideoSource(mediaReader, name);
        }

        return value != null;
    }

    [Obsolete("Use SoundSource.Open")]
    public bool OpenSoundSource(string name, [NotNullWhen(true)] out ISoundSource? value)
    {
        value = null;
        if (TryGetMediaReaderOrOpen(name, out Ref<MediaReader>? mediaReader))
        {
            value = new SoundSource(mediaReader, name);
        }

        return value != null;
    }

    [Obsolete("Use BitmapSource.Open")]
    public bool OpenImageSource(string name, [NotNullWhen(true)] out IImageSource? value)
    {
        value = null;
        if (TryGetBitmapOrOpen(name, out Ref<IBitmap>? bitmap))
        {
            value = new BitmapSource(bitmap, name);
        }

        return value != null;
    }

    [Obsolete("Do not use.")]
    public bool TryGetMediaReader(string name, [NotNullWhen(true)] out Ref<MediaReader>? value)
    {
        return TryGetMediaReaderOrOpen(name, out value);
    }

    [Obsolete("Do not use.")]
    public bool TryGetMediaReaderOrOpen(string fileName, [NotNullWhen(true)] out Ref<MediaReader>? value)
    {
        try
        {
            var reader = MediaReader.Open(fileName);
            value = Ref<MediaReader>.Create(reader);
            return true;
        }
        catch
        {
            value = null;
            return false;
        }
    }

    [Obsolete("Do not use.")]
    public bool TryGetBitmap(string name, [NotNullWhen(true)] out Ref<IBitmap>? value)
    {
        return TryGetBitmapOrOpen(name, out value);
    }

    [Obsolete("Do not use.")]
    public bool TryGetBitmapOrOpen(string fileName, [NotNullWhen(true)] out Ref<IBitmap>? value)
    {
        try
        {
            var bitmap = Bitmap<Bgra8888>.FromFile(fileName);
            value = Ref<IBitmap>.Create(bitmap);
            return true;
        }
        catch
        {
            value = null;
            return false;
        }

    }

    // 移譲する側は今後、valueを直接参照しない。
    [Obsolete("Do not use.")]
    public Ref<MediaReader>? TryTransferMediaReader(string name, MediaReader mediaReader)
    {
        return Ref<MediaReader>.Create(mediaReader);
    }

    // 移譲する側は今後、valueを直接参照しない。
    [Obsolete("Do not use.")]
    public Ref<IBitmap>? TryTransferBitmap(string name, IBitmap bitmap)
    {
        return Ref<IBitmap>.Create(bitmap);
    }

    [Obsolete("Do not use.")]
    public bool ContainsMediaReader(string name)
    {
        return false;
    }

    [Obsolete("Do not use.")]
    public bool ContainsBitmap(string name)
    {
        return false;
    }
}
