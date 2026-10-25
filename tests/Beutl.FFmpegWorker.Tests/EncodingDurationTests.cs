using Beutl.FFmpegWorker.Decoding;
using Beutl.FFmpegWorker.Encoding;
using Beutl.Media;
using Beutl.Media.Decoding;
using FFmpeg.AutoGen.Abstractions;
using FFmpegSharp;

namespace Beutl.FFmpegWorker.Tests;

[TestFixture]
public sealed class EncodingDurationTests
{
    // A provider zero-fills its last block. Encoding that padding extends the file beyond its last picture (#3594).
    [TestCase(30, 44100, "aac")]
    [TestCase(151, 44100, "aac")]
    [TestCase(32, 48000, "aac")] // Exactly 50 AAC blocks, with no padding to trim.
    [TestCase(30, 44100, "pcm_s16le")]
    public async Task Audio_padding_does_not_extend_the_export(int frameCount, int sampleRate, string audioCodec)
    {
        FFmpegTestHelpers.RequireFFmpeg();
        if (MediaCodec.FindEncoder("libx264") == null || MediaCodec.FindEncoder(audioCodec) == null)
            Assert.Ignore("FFmpeg native libraries are not available.");

        string extension = audioCodec == "aac" ? "mp4" : "mov";
        string path = Path.Combine(Path.GetTempPath(), $"beutl-duration-{Guid.NewGuid():N}.{extension}");
        var rate = new Rational(30);
        var size = new PixelSize(32, 24);
        long sampleCount = (long)frameCount * sampleRate / 30;
        double duration = frameCount / 30.0;
        try
        {
            var controller = FFmpegTestHelpers.CreateController(path, size, rate, sampleRate, channels: 2);
            controller.VideoSettings.Codec.Value = new CodecRecord("libx264", "H.264");
            controller.VideoSettings.Format.Value = (int)AVPixelFormat.AV_PIX_FMT_YUV420P;
            controller.AudioSettings.Codec.Value = new CodecRecord(audioCodec, audioCodec);
            controller.AudioSettings.Format.Value = FFmpegAudioEncoderSettings.AudioFormat.Default;
            using var frames = new SolidFrameProvider(size, frameCount);
            using var samples = new SineSampleProvider(sampleCount, sampleRate);
            await controller.Encode(frames, samples, CancellationToken.None);

            using var demuxer = MediaDemuxer.Open(path);
            MediaStream video = demuxer.Single(s => s.CodecparRef.codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO);
            MediaStream audio = demuxer.Single(s => s.CodecparRef.codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO);
            Assert.Multiple(() =>
            {
                Assert.That(video.NbFrames, Is.EqualTo(frameCount));
                Assert.That(video.Duration * ffmpeg.av_q2d(video.TimeBase), Is.EqualTo(duration).Within(0.001));
                Assert.That(audio.Duration * ffmpeg.av_q2d(audio.TimeBase), Is.EqualTo(duration).Within(0.001));
                Assert.That(MediaTime.FromSeconds(demuxer.Duration / (double)ffmpeg.AV_TIME_BASE)
                    .ToFrame(rate, MidpointRounding.AwayFromZero), Is.EqualTo(frameCount),
                    "Reimporting the export must not add empty frames after its last picture.");
            });

            using var reader = new FFmpegReader(path, new MediaOptions(MediaMode.Video), new FFmpegDecodingSettings());
            for (int frame = 0; frame < frameCount; frame++)
            {
                Assert.That(reader.ReadVideo(frame, out var bitmap), Is.True, $"frame {frame}");
                using (bitmap)
                    Assert.That(bitmap!.Value.GetRow(12)[16 * 4 + 2], Is.GreaterThan(100), $"frame {frame}");
            }
        }
        finally
        {
            File.Delete(path);
        }
    }
}
