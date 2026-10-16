using System.Diagnostics.CodeAnalysis;
using Beutl.Media.Music;
using Beutl.Media.Proxy;
using Beutl.Media.Source;

namespace Beutl.Media.Decoding;

/// <summary>
/// Decodes the video stream and the audio stream of one media file. A decoder creates it in
/// <see cref="IDecoderInfo.Open"/>.
/// </summary>
/// <remarks>
/// Every decoder must follow these rules, so that a caller does not need to know which decoder opened a file. The
/// tests in <c>tests/MediaReaderConformance.cs</c> check the decoders against them.
/// <list type="bullet">
/// <item><description>
/// <b>Positions.</b> Video frames and audio samples both count from the start of the container, so frame 0 and
/// sample 0 are the same instant even when one stream starts later than the other. Frame <c>N</c> is the frame
/// whose display time covers <c>N</c> divided by <see cref="VideoStreamInfo.FrameRate"/> seconds, and sample
/// <c>N</c> is at <c>N</c> divided by <see cref="AudioStreamInfo.SampleRate"/> seconds. What an earlier read
/// returned does not change what a read returns.
/// </description></item>
/// <item><description>
/// <b>Streams.</b> A reader has a stream only when it was requested and the file has it; <see cref="HasVideo"/>
/// and <see cref="HasAudio"/> say which. <see cref="VideoInfo"/> and <see cref="AudioInfo"/> are valid only for a
/// stream the reader has, and may throw for one it does not.
/// </description></item>
/// <item><description>
/// <b>Failures.</b> A read that cannot decode, for example because the file is damaged or the process that decodes
/// it is gone, logs the failure and returns <see langword="false"/>. It does not throw.
/// </description></item>
/// <item><description>
/// <b>End of stream.</b> <see cref="ReadAudio"/> returns fewer samples than requested at the end of the stream,
/// and none past it. <see cref="ReadVideo"/> returns <see langword="false"/> for a frame past the last one.
/// </description></item>
/// <item><description>
/// <b>Returned buffers.</b> A bitmap or sample buffer that a read returns may share memory with the reader, so it
/// is valid only until the next read on the same reader. The caller disposes it, and copies it to keep it longer.
/// </description></item>
/// <item><description>
/// <b>Threads.</b> A reader can be called from any thread, one call at a time. A caller that shares a reader across
/// threads makes its calls one after another, <see cref="Dispose()"/> included.
/// </description></item>
/// </list>
/// </remarks>
public abstract class MediaReader : IDisposable
{
    ~MediaReader()
    {
        Dispose(disposing: false);
    }

    public bool IsDisposed { get; private set; }

    /// <summary>
    /// The video stream's size, nominal frame rate, frame count and duration. Valid only when
    /// <see cref="HasVideo"/> is <see langword="true"/>.
    /// </summary>
    public abstract VideoStreamInfo VideoInfo { get; }

    /// <summary>
    /// The audio stream's sample rate, channel count and duration. Valid only when <see cref="HasAudio"/> is
    /// <see langword="true"/>.
    /// </summary>
    public abstract AudioStreamInfo AudioInfo { get; }

    /// <summary>
    /// Whether the reader has a video stream: video was requested when it was opened, and the file has it.
    /// </summary>
    public abstract bool HasVideo { get; }

    /// <summary>
    /// Whether the reader has an audio stream: audio was requested when it was opened, and the file has it.
    /// </summary>
    public abstract bool HasAudio { get; }

    public virtual ProxyResolution? ProxyResolution => null;

    /// <summary>
    /// Opens <paramref name="file"/> for its video and audio streams with the first registered decoder that opens it.
    /// </summary>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="UnsupportedMediaException">No registered decoder opened the file.</exception>
    public static MediaReader Open(string file)
    {
        return Open(file, new MediaOptions());
    }

    /// <summary>
    /// Opens <paramref name="file"/> with the first registered decoder that opens it, as
    /// <see cref="DecoderRegistry.OpenMediaFile"/> does.
    /// </summary>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="UnsupportedMediaException">
    /// No registered decoder opened the file, for example because it has none of the requested streams.
    /// </exception>
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

    /// <summary>
    /// Decodes video frame <paramref name="frame"/>: the frame whose display time covers <paramref name="frame"/>
    /// divided by <see cref="VideoStreamInfo.FrameRate"/> seconds from the start of the container.
    /// </summary>
    /// <param name="frame">The frame index at the stream's nominal frame rate.</param>
    /// <param name="image">
    /// On success, the frame. It may share memory with the reader, so it is valid only until the next read on this
    /// reader. The caller disposes it.
    /// </param>
    /// <returns>
    /// <see langword="false"/> when no frame covers that time, as past the last frame, when the reader has no video
    /// stream or is disposed, or when decoding failed. A failure is logged, not thrown.
    /// </returns>
    public abstract bool ReadVideo(int frame, [NotNullWhen(true)] out Ref<Bitmap>? image);

    /// <summary>
    /// Decodes audio samples starting at <paramref name="start"/>, counted from the start of the container.
    /// </summary>
    /// <param name="start">First sample (frame) index to read, in source sample-rate units.</param>
    /// <param name="length">Number of samples (frames) requested. Passing 0 is valid and yields an empty buffer.</param>
    /// <param name="sound">
    /// On success, a <see cref="Pcm{T}"/> of <see cref="Music.Samples.Stereo32BitFloat"/>, stereo whatever the source
    /// channel count. It may share memory with the reader, so it is valid only until the next read on this reader.
    /// The caller disposes it. A short read (<see cref="IPcm.NumSamples"/> below <paramref name="length"/>, down to 0
    /// past the end) signals end-of-stream, so callers size copies with
    /// <c>Math.Min(pcm.NumSamples, destinationLength)</c>.
    /// </param>
    /// <returns>
    /// <see langword="false"/> only when no buffer can be produced at all: the reader is disposed, has no audio
    /// stream, or hit a decode or transport error, which is logged, not thrown. End-of-stream still returns
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
