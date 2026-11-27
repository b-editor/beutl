using Beutl.Audio;
using Beutl.Extensibility;
using Beutl.Extensions.FFmpeg.Decoding;
using Beutl.Extensions.FFmpeg.Encoding;
using Beutl.FFmpegIpc.Protocol;
using Beutl.FFmpegIpc.Protocol.Messages;
using Beutl.Media;
using Beutl.Media.Decoding;

namespace Beutl.FFmpegIpc.Tests;

[TestFixture]
public class EncodingStreamTests
{
    [TestCase(false, new[] { "soun", "vide" }, TestName = "Encode_WritesAudioAndVideoStreams")]
    [TestCase(true, new[] { "vide" }, TestName = "EncodeVideoOnly_SkipsAudioSetup")]
    public async Task Encode_WritesTheRequestedStreams(bool videoOnly, string[] expectedTracks)
    {
        DecodingWorkerTestHelpers.RequireDecodingWorker();
        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N") + ".mp4");
        var frameSize = new PixelSize(64, 48);
        var controller = new FFmpegEncodingControllerProxy(path, new FFmpegEncodingSettings());
        controller.VideoSettings.SourceSize.Value = frameSize;
        controller.VideoSettings.DestinationSize.Value = frameSize;
        controller.VideoSettings.Codec.Value = new CodecRecord("libx264", "H.264 / AVC");
        using var frames = new SolidFrames(frameSize);
        try
        {
            if (videoOnly)
            {
                // AAC rejects this rate if the worker initializes the audio encoder.
                controller.AudioSettings.SampleRate.Value = 1;
                await controller.EncodeVideoOnly(frames, CancellationToken.None);
            }
            else
            {
                using var samples = new Silence();
                await controller.Encode(frames, samples, CancellationToken.None);
            }

            Assert.That(Mp4TrackTable.ReadTypes(path), Is.EquivalentTo(expectedTracks));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestCase(".wav")]
    [TestCase(".mp3")]
    public async Task Encode_WritesTheAudioWithoutAskingForAFrame(string extension)
    {
        var connection = DecodingWorkerTestHelpers.RequireDecodingWorker();
        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N") + extension);
        var controller = new FFmpegEncodingControllerProxy(path, new FFmpegEncodingSettings());
        controller.AudioSettings.Format.Value = FFmpegAudioEncoderSettings.AudioFormat.Default;
        using var frames = new NoFrames();
        using var samples = new Silence();
        try
        {
            await controller.Encode(frames, samples, CancellationToken.None);

            var streams = FFmpegWorkerRequests.Send<OpenFileRequest, OpenFileResponse>(connection,
                MessageType.OpenFile, MessageType.OpenFileResult,
                new OpenFileRequest { FilePath = path, StreamsToLoad = (int)MediaMode.AudioVideo },
                FFmpegWorkerRequests.DefaultTimeout);
            await FFmpegWorkerRequests.CloseReader(connection, streams.ReaderId, FFmpegWorkerRequests.DefaultTimeout);
            Assert.Multiple(() =>
            {
                Assert.That(streams.HasVideo, Is.False);
                Assert.That(streams.HasAudio, Is.True);
                Assert.That(streams.AudioDurationNum, Is.Positive);
            });
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class SolidFrames(PixelSize size) : IFrameProvider
    {
        private long _next;

        public long FrameCount => 4;
        public Rational FrameRate => new(4, 1);

        public ValueTask<Bitmap?> ReadNextFrame()
        {
            if (_next++ >= FrameCount)
                return ValueTask.FromResult<Bitmap?>(null);

            var bitmap = new Bitmap(size.Width, size.Height);
            bitmap.GetPixelSpan().Fill(255);
            return ValueTask.FromResult<Bitmap?>(bitmap);
        }

        public void Dispose() { }
    }

    // What the export view model hands an encoder whose output has no video.
    private sealed class NoFrames : IFrameProvider
    {
        public long FrameCount => 0;
        public Rational FrameRate => new(0, 1);
        public ValueTask<Bitmap?> ReadNextFrame() => throw new AssertionException("An audio-only export must not ask for a frame.");
        public void Dispose() { }
    }

    private sealed class Silence : ISampleProvider
    {
        private long _next;

        public AudioChannelLayout ChannelLayout => AudioChannelLayout.Stereo;
        public long SampleCount => 44100;
        public long SampleRate => 44100;

        public ValueTask<AudioBuffer?> ReadNextSamples(int length)
        {
            int count = (int)Math.Min(length, SampleCount - _next);
            _next += count;
            return ValueTask.FromResult(count > 0 ? new AudioBuffer((int)SampleRate, 2, count) : null);
        }

        public void Dispose() { }
    }
}
