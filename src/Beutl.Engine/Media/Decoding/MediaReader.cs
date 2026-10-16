using System.Diagnostics.CodeAnalysis;
using Beutl.Media.Music;
using Beutl.Media.Proxy;
using Beutl.Media.Source;

namespace Beutl.Media.Decoding;

/// <summary>
/// Decodes the video and audio streams of one media file. <see cref="IDecoderInfo.Open"/> creates it.
/// </summary>
/// <remarks>
/// Every decoder follows these rules, which <c>tests/MediaReaderConformance.cs</c> checks:
/// <list type="bullet">
/// <item><description>Frames and samples count from the container start: frame N covers N /
/// <see cref="VideoStreamInfo.FrameRate"/> seconds. A read does not depend on earlier reads.</description></item>
/// <item><description>A reader has only the requested streams that the file has.</description></item>
/// <item><description>A failed read logs and returns <see langword="false"/>; it never throws.</description></item>
/// <item><description>At the end, audio reads come back short (empty past it) and video reads return
/// <see langword="false"/>.</description></item>
/// <item><description>A returned buffer is valid until the next read; the caller disposes it.</description></item>
/// <item><description>Calls may come from any thread, one at a time.</description></item>
/// </list>
/// </remarks>
public abstract class MediaReader : IDisposable
{
    ~MediaReader()
    {
        Dispose(disposing: false);
    }

    public bool IsDisposed { get; private set; }

    /// <summary>The video stream's info; valid only when <see cref="HasVideo"/> is <see langword="true"/>.</summary>
    public abstract VideoStreamInfo VideoInfo { get; }

    /// <summary>The audio stream's info; valid only when <see cref="HasAudio"/> is <see langword="true"/>.</summary>
    public abstract AudioStreamInfo AudioInfo { get; }

    /// <summary>Whether the reader has a video stream.</summary>
    public abstract bool HasVideo { get; }

    /// <summary>Whether the reader has an audio stream.</summary>
    public abstract bool HasAudio { get; }

    public virtual ProxyResolution? ProxyResolution => null;

    /// <summary>Opens <paramref name="file"/> for video and audio with the first registered decoder that can.</summary>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="UnsupportedMediaException">No registered decoder opened the file.</exception>
    public static MediaReader Open(string file)
    {
        return Open(file, new MediaOptions());
    }

    /// <summary>Opens <paramref name="file"/> with the first registered decoder that can.</summary>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="UnsupportedMediaException">No registered decoder opened the file.</exception>
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

    /// <summary>Decodes frame <paramref name="frame"/>.</summary>
    /// <returns><see langword="false"/> past the last frame, without a video stream, or when decoding fails.</returns>
    public abstract bool ReadVideo(int frame, [NotNullWhen(true)] out Ref<Bitmap>? image);

    /// <summary>Decodes <paramref name="length"/> samples (0 is allowed) from <paramref name="start"/>.</summary>
    /// <param name="sound">Stereo <see cref="Music.Samples.Stereo32BitFloat"/> samples; fewer at the end.</param>
    /// <returns>
    /// <see langword="false"/> without an audio stream or when decoding fails. The end of the stream returns
    /// <see langword="true"/>.
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
