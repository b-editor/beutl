using System.Diagnostics.CodeAnalysis;
using Beutl.Audio;
using Beutl.Media.Proxy;
using Beutl.Media.Source;

namespace Beutl.Media.Decoding;

public abstract class MediaReader : IDisposable
{
    ~MediaReader()
    {
        Dispose(disposing: false);
    }

    public bool IsDisposed { get; private set; }

    public abstract VideoStreamInfo VideoInfo { get; }

    public abstract AudioStreamInfo AudioInfo { get; }

    public abstract bool HasVideo { get; }

    public abstract bool HasAudio { get; }

    public virtual ProxyResolution? ProxyResolution => null;

    public static MediaReader Open(string file)
    {
        return Open(file, new MediaOptions());
    }

    public static MediaReader Open(string file, MediaOptions options)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(options);

        MediaReader? reader = DecoderRegistry.OpenMediaFile(file, options);
        if (reader is not null)
        {
            return reader;
        }

        // OpenMediaFile returns null both when the file is missing and when no registered decoder can
        // open it; distinguish them so an unsupported-but-present file is not reported as missing.
        if (File.Exists(file))
        {
            throw new UnsupportedMediaException($"No registered decoder could open '{file}'.", file);
        }

        throw new FileNotFoundException(null, file);
    }

    public abstract bool ReadVideo(int frame, [NotNullWhen(true)] out Ref<Bitmap>? image);

    /// <summary>
    /// Decodes audio samples starting at <paramref name="start"/>.
    /// </summary>
    /// <param name="start">First sample (frame) index to read, in source sample-rate units.</param>
    /// <param name="length">Number of samples (frames) requested. Passing 0 is valid and yields an empty buffer.</param>
    /// <param name="sound">
    /// On success, an <see cref="AudioBuffer"/> with <c>AudioInfo.ChannelLayout.ChannelCount</c> channels
    /// in <see cref="AudioStreamInfo.ChannelLayout"/> order. A short read
    /// (<see cref="AudioBuffer.SampleCount"/> below <paramref name="length"/>, down to 0 at or past the end) signals
    /// end-of-stream, though a backend without a precise count may zero-fill the uncovered tail instead. Callers size
    /// copies with <c>Math.Min(audio.SampleCount, destinationLength)</c>.
    /// </param>
    /// <returns>
    /// <see langword="false"/> only when no buffer can be produced at all: the reader is disposed, has no audio
    /// stream, or hit an unrecoverable decode or transport error. End-of-stream still returns <see langword="true"/>.
    /// </returns>
    public abstract bool ReadAudio(int start, int length, [NotNullWhen(true)] out AudioBuffer? sound);

    public void Dispose()
    {
        if (!IsDisposed)
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);

            IsDisposed = true;
        }
    }

    protected virtual void Dispose(bool disposing)
    {
    }
}
