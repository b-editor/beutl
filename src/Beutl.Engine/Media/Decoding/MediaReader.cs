using System.Diagnostics.CodeAnalysis;
using Beutl.Media.Music;
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
    /// On success, a <see cref="Pcm{T}"/> of <see cref="Music.Samples.Stereo32BitFloat"/>, stereo whatever the source
    /// channel count. A short read (<see cref="IPcm.NumSamples"/> below <paramref name="length"/>, down to 0 at or past
    /// the end) signals end-of-stream, though a backend without a precise count may zero-fill the uncovered tail
    /// instead. Callers size copies with <c>Math.Min(pcm.NumSamples, destinationLength)</c>.
    /// </param>
    /// <returns>
    /// <see langword="false"/> only when no buffer can be produced at all: the reader is disposed, has no audio
    /// stream, or hit an unrecoverable decode or transport error. End-of-stream still returns <see langword="true"/>.
    /// </returns>
    public abstract bool ReadAudio(int start, int length, [NotNullWhen(true)] out Ref<IPcm>? sound);

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
