using System.Diagnostics.CodeAnalysis;
using Beutl.Media.Music;
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
    /// <param name="length">Number of samples (frames) requested.</param>
    /// <param name="sound">
    /// On success, a <see cref="Pcm{T}"/> of <see cref="Music.Samples.Stereo32BitFloat"/>. Its
    /// <see cref="IPcm.NumSamples"/> is the number of samples actually decoded and MAY be less than
    /// <paramref name="length"/> near end-of-stream. The output is always stereo regardless of the
    /// source channel count (see <see cref="AudioStreamInfo.NumChannels"/>).
    /// </param>
    /// <returns>
    /// <see langword="true"/> if at least one sample was decoded; <see langword="false"/> only when
    /// nothing could be read (for example <paramref name="start"/> is at or past end-of-stream, or the
    /// reader is disposed/unreadable).
    /// </returns>
    /// <remarks>
    /// Callers must treat the result as a possibly-short read and copy with
    /// <c>Math.Min(pcm.NumSamples, destinationLength)</c>; the uncovered tail is silence. A backend MAY
    /// instead return a full <paramref name="length"/> buffer whose trailing uncovered region is
    /// zero-filled (silence) — both shapes satisfy this contract.
    /// </remarks>
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
