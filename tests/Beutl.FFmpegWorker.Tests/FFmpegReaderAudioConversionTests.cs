using Beutl.FFmpegWorker.Decoding;
using FFmpeg.AutoGen.Abstractions;
using FFmpegSharp;

namespace Beutl.FFmpegWorker.Tests;

[TestFixture]
public sealed class FFmpegReaderAudioConversionTests
{
    [OneTimeSetUp]
    public void OneTimeSetUp() => FFmpegTestHelpers.RequireFFmpeg();

    [TestCase(44100)]
    [TestCase(48000)]
    public unsafe void ReusedConverter_PreservesSamplesAcrossChangingFrameSizes(int sampleRate)
    {
        using var converter = new FFmpegReader.AudioFrameConverter(sampleRate);
        int sequence = 0;
        foreach (int samples in new[] { 1024, 128, 4096, 37, 1024 })
        {
            using var input = new MediaFrame
            {
                ChLayout = new AVChannelLayout
                {
                    order = AVChannelOrder.AV_CHANNEL_ORDER_NATIVE,
                    nb_channels = 2,
                    u = new AVChannelLayout_u { mask = ffmpeg.AV_CH_LAYOUT_STEREO },
                },
                SampleRate = sampleRate,
                Format = (int)AVSampleFormat.AV_SAMPLE_FMT_FLTP,
                NbSamples = samples,
            };
            input.AllocateBuffer();
            float marker = ++sequence / 16f;
            for (int i = 0; i < samples; i++)
            {
                ((float*)input.Data[0])[i] = marker + i / 16384f;
                ((float*)input.Data[1])[i] = -marker - i / 16384f;
            }

            using MediaFrame output = converter.ConvertFrame(input);
            Assert.That(output.NbSamples, Is.EqualTo(samples));
            Assert.That(output.SampleRate, Is.EqualTo(sampleRate));
            Assert.That(output.Format, Is.EqualTo((int)AVSampleFormat.AV_SAMPLE_FMT_FLT));
            var interleaved = new ReadOnlySpan<float>(output.Data[0], samples * 2);
            for (int i = 0; i < samples; i++)
            {
                Assert.That(interleaved[i * 2], Is.EqualTo(marker + i / 16384f));
                Assert.That(interleaved[i * 2 + 1], Is.EqualTo(-marker - i / 16384f));
            }
        }
    }
}
