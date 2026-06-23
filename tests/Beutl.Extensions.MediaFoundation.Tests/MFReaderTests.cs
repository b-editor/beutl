using Beutl.Embedding.MediaFoundation.Decoding;
using Beutl.Media.Decoding;

using Vortice.Win32;

using Windows.Win32.Media.MediaFoundation;

namespace Beutl.Extensions.MediaFoundation.Tests;

[TestFixture]
public class MFReaderTests
{
    [Test]
    public void Constructor_WhenNoAudioStreamAndVideoSucceeds_FallsBackToVideoOnly()
    {
        var decoder = new FakeVideoDecoder();

        // A video-only file opened in the default AudioVideo mode: the video stream
        // decodes fine, but there is no audio stream. The reader must fall back to
        // video-only instead of failing the whole open.
        var reader = new MFReader(
            "sample.mp4",
            new MediaOptions(MediaMode.AudioVideo),
            new MFDecodingExtension(),
            (_, _, _) => decoder,
            static (_, _) => throw new NotSupportedException("audio factory must not be called without an audio stream"),
            hasAudioStream: static _ => false);

        Assert.Multiple(() =>
        {
            Assert.That(reader.HasVideo, Is.True);
            Assert.That(reader.HasAudio, Is.False);
            Assert.That(decoder.DisposeCount, Is.EqualTo(0));
        });

        // The live reader still owns the video decoder; it is disposed only when the
        // reader is, not eagerly on the tolerated audio failure.
        reader.Dispose();
        Assert.That(decoder.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public void Constructor_WhenAudioStreamExistsAndAudioFactoryThrows_Propagates()
    {
        var decoder = new FakeVideoDecoder();

        // If the file advertises an audio stream, losing audio silently is worse than
        // failing this decoder. Propagate so MFDecoderInfo.Open returns null and the
        // registry can try the next decoder, such as FFmpeg.
        Assert.Throws<InvalidOperationException>(() => new MFReader(
            "sample.mp4",
            new MediaOptions(MediaMode.AudioVideo),
            new MFDecodingExtension(),
            (_, _, _) => decoder,
            static (_, _) => throw new InvalidOperationException("audio initialization failed"),
            hasAudioStream: static _ => true));

        Assert.That(decoder.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public void Constructor_AudioOnlyMode_WhenAudioFactoryThrows_Propagates()
    {
        // With no video stream to fall back to, an audio-open failure is genuine and
        // must propagate so other decoders (e.g. FFmpeg) can retry.
        Assert.Throws<InvalidOperationException>(() => new MFReader(
            "sample.mp3",
            new MediaOptions(MediaMode.Audio),
            new MFDecodingExtension(),
            static (_, _, _) => throw new NotSupportedException("video factory must not be called in audio-only mode"),
            static (_, _) => throw new InvalidOperationException("audio initialization failed")));
    }

    [Test]
    public void Constructor_WhenVideoMediaInfoThrows_DisposesVideoDecoder()
    {
        var decoder = new FakeVideoDecoder
        {
            GetMediaInfoException = new InvalidOperationException("media info failed"),
        };

        Assert.Throws<InvalidOperationException>(() => new MFReader(
            "sample.mp4",
            new MediaOptions(MediaMode.Video),
            new MFDecodingExtension(),
            (_, _, _) => decoder,
            static (_, _) => throw new NotSupportedException()));

        Assert.That(decoder.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public void Constructor_WhenVideoStreamSucceeds_DoesNotDisposeVideoDecoder()
    {
        var decoder = new FakeVideoDecoder();

        // MediaMode.Video skips the audio branch, so the throwing audio factory is never invoked.
        using var reader = new MFReader(
            "sample.mp4",
            new MediaOptions(MediaMode.Video),
            new MFDecodingExtension(),
            (_, _, _) => decoder,
            static (_, _) => throw new NotSupportedException());

        Assert.Multiple(() =>
        {
            Assert.That(reader.HasVideo, Is.True);
            Assert.That(reader.HasAudio, Is.False);
            Assert.That(decoder.DisposeCount, Is.EqualTo(0));
        });
    }

    private sealed class FakeVideoDecoder : IMediaFoundationVideoDecoder
    {
        public Exception? GetMediaInfoException { get; init; }

        public int DisposeCount { get; private set; }

        public MFMediaInfo GetMediaInfo()
        {
            if (GetMediaInfoException != null)
            {
                throw GetMediaInfoException;
            }

            return new()
            {
                VideoStreamIndex = 0,
                Fps = new MFRatio { Numerator = 30, Denominator = 1 },
                TotalFrameCount = 1,
                ImageFormat = new BitmapInfoHeader { Width = 16, Height = 16 },
                VideoFormatName = "YUY2",
            };
        }

        public int ReadFrame(int frame, nint buf)
            => throw new NotSupportedException();

        public void Dispose()
            => DisposeCount++;
    }
}
