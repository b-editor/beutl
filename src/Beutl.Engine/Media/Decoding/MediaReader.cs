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

    public static MediaReader Open(string file)
    {
        return Open(file, new MediaOptions());
    }

    public static MediaReader Open(string file, MediaOptions options)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(options);
        if (!File.Exists(file))
        {
            throw new FileNotFoundException(null, file);
        }

        return DecoderRegistry.OpenMediaFile(file, options) ?? throw new Exception();
    }

    public abstract bool ReadVideo(int frame, [NotNullWhen(true)] out Ref<Bitmap>? image);

    /// <summary>
    /// Decodes audio samples starting at <paramref name="start"/>.
    /// </summary>
    /// <param name="start">First sample (frame) index to read, in source sample-rate units.</param>
    /// <param name="length">Number of samples (frames) requested. Passing 0 is valid and yields an empty buffer.</param>
    /// <param name="sound">
    /// On success, a <see cref="Pcm{T}"/> of <see cref="Music.Samples.Stereo32BitFloat"/>. The output is
    /// always stereo regardless of the source channel count (see <see cref="AudioStreamInfo.NumChannels"/>).
    /// <para>
    /// End-of-stream is signalled by <see cref="IPcm.NumSamples"/> being less than
    /// <paramref name="length"/> (a short read), including <c>NumSamples == 0</c> when
    /// <paramref name="start"/> is at or past the end of the stream. A backend that cannot report a
    /// precise decoded count (e.g. the AVFoundation native reader) may instead return a full
    /// <paramref name="length"/> buffer whose trailing uncovered region is zero-filled (silence).
    /// </para>
    /// <para>
    /// Callers must therefore always size copies with
    /// <c>Math.Min(pcm.NumSamples, destinationLength)</c>, treat a zero <see cref="IPcm.NumSamples"/>
    /// (for a non-zero <paramref name="length"/>) as end-of-stream, and never assume
    /// <c>pcm.NumSamples == length</c>.
    /// </para>
    /// </param>
    /// <returns>
    /// <see langword="true"/> whenever a buffer is produced — including an empty buffer for
    /// <paramref name="length"/> == 0 and for a <paramref name="start"/> at/after end-of-stream.
    /// <see langword="false"/> is returned only when no buffer can be produced at all: the reader is
    /// disposed, has no audio stream, or hit an unrecoverable decode/transport error. End-of-stream is
    /// not reported via <see langword="false"/>; use <see cref="IPcm.NumSamples"/> for that.
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
