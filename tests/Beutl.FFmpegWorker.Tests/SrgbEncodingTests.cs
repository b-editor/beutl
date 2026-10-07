using Beutl.Extensibility;
using Beutl.FFmpegIpc;
using Beutl.FFmpegWorker.Encoding;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using FFmpeg.AutoGen.Abstractions;
using FFmpegSharp;

namespace Beutl.FFmpegWorker.Tests;

[TestFixture, NonParallelizable]
public sealed class SrgbEncodingTests
{
    [OneTimeSetUp]
    public void Initialize()
    {
        Log.LoggerFactory = Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;
        try
        {
            FFmpegLoaderWorker.Initialize();
        }
        catch (Exception exception) when (exception is FFmpegLibrariesNotFoundException or DllNotFoundException)
        {
            Assert.Ignore("FFmpeg native libraries are unavailable.");
        }
    }

    [Test]
    public async Task Encode_SrgbF16CoverageSurvivesLosslessVideoRoundTrip()
    {
        string directory = Path.Combine(Path.GetTempPath(), "beutl-srgb-encode-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "coverage.mkv");
            var controller = new FFmpegEncodingController(path, new FFmpegEncodingSettings());
            controller.VideoSettings.SourceSize = new PixelSize(240, 80);
            controller.VideoSettings.DestinationSize = new PixelSize(240, 80);
            controller.VideoSettings.FrameRate = new Rational(30, 1);
            controller.VideoSettings.Codec = new CodecRecord("ffv1", "FFV1");
            controller.VideoSettings.Options.Clear();
            controller.VideoSettings.Format = (int)AVPixelFormat.AV_PIX_FMT_BGRA;
            controller.VideoSettings.KeyframeRate = 1;
            controller.AudioSettings.SampleRate = 44100;
            controller.AudioSettings.Channels = 2;
            using var frames = new CoverageFrameProvider();
            using var samples = new EmptySamples();
            await controller.Encode(frames, samples, CancellationToken.None);

            using var demuxer = MediaDemuxer.Open(path);
            MediaStream stream = Enumerable.Range(0, demuxer.Count).Select(index => demuxer[index])
                .First(candidate => candidate.CodecparRef.codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO);
            using var decoder = MediaDecoder.CreateDecoder(stream.CodecparRef);
            using var packet = new MediaPacket();
            using var decoded = new MediaFrame();
            foreach (MediaPacket input in demuxer.ReadPackets(packet))
            {
                if (input.StreamIndex != stream.Index)
                    continue;
                foreach (var _ in decoder.DecodePacket(input, decoded))
                {
                    AssertFrame(decoded);
                    return;
                }
            }

            foreach (var _ in decoder.DecodePacket(null, decoded))
            {
                AssertFrame(decoded);
                return;
            }

            Assert.Fail("The encoded video did not contain a decodable frame.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static unsafe void AssertFrame(MediaFrame frame)
    {
        Assert.That((AVPixelFormat)frame.Format, Is.EqualTo(AVPixelFormat.AV_PIX_FMT_BGRA));
        byte* row = (byte*)frame.Data[0] + 40 * frame.Linesize[0];
        Assert.Multiple(() =>
        {
            Assert.That(row[60 * 4 + 2], Is.EqualTo(51));
            Assert.That(row[59 * 4 + 2], Is.Zero);
            Assert.That(row[61 * 4 + 2], Is.EqualTo(255));
        });
    }

    private sealed class CoverageFrameProvider : IFrameProvider
    {
        public long FrameCount => 1;
        public Rational FrameRate => new(30, 1);

        public ValueTask<Bitmap> RenderFrame(long frame)
        {
            using RenderTarget target = RenderTarget.Create(240, 80)!;
            using (var canvas = new ImmediateCanvas(target, RenderIntent.Delivery))
            {
                canvas.Clear(Colors.Black);
                canvas.DrawRectangle(new Rect(60.8f, 0, 120, 80), Brushes.Resource.White, null);
            }

            return ValueTask.FromResult(target.Snapshot());
        }

        public void Dispose() { }
    }

    private sealed class EmptySamples : ISampleProvider
    {
        public long SampleCount => 0;
        public long SampleRate => 44100;
        public ValueTask<Pcm<Stereo32BitFloat>> Sample(long offset, long length)
            => throw new InvalidOperationException("An empty audio provider must not be sampled.");
        public void Dispose() { }
    }
}
