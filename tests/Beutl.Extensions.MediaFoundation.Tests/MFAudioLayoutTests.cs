using System.Runtime.InteropServices;
using Beutl.Audio;
using Beutl.Embedding.MediaFoundation.Decoding;
using Beutl.Embedding.MediaFoundation.Encoding;
using Vortice.MediaFoundation;

namespace Beutl.Extensions.MediaFoundation.Tests;

[TestFixture]
public class MFAudioLayoutTests
{
    [TestCase(MFAudioCodec.AAC, 1, false, true)]
    [TestCase(MFAudioCodec.AAC, 2, false, true)]
    [TestCase(MFAudioCodec.AAC, 6, false, false)]
    [TestCase(MFAudioCodec.AAC, 6, true, true)]
    [TestCase(MFAudioCodec.AAC, 8, true, false)]
    [TestCase(MFAudioCodec.AAC, 4, true, false)]
    [TestCase(MFAudioCodec.PCM, 12, false, true)]
    [TestCase(MFAudioCodec.Float, 8, false, true)]
    public void ChannelValidationFollowsTheCodec(MFAudioCodec codec, int channels, bool windows10, bool accepted)
    {
        Assert.That(() => MFEncodingController.ValidateAudioChannels(codec, channels, windows10),
            accepted ? Throws.Nothing : Throws.TypeOf<NotSupportedException>().With.Message.Contains("AAC"));
    }

    [TestCase(1, false)]
    [TestCase(6, false)]
    [TestCase(8, true)]
    [TestCase(12, true)]
    [Platform("Win")]
    public async Task PcmMediaTypesCarryTheChannelCountAndMask(int channels, bool floatingPoint)
    {
        await MFThread.Dispatcher.InvokeAsync(() =>
        {
            var layout = AudioChannelLayout.GetDefault(channels);
            int bits = floatingPoint ? 32 : 16;
            using var type = MFEncodingController.CreatePcm(floatingPoint ? AudioFormatGuids.Float : AudioFormatGuids.Pcm,
                48000, layout, bits);
            Assert.Multiple(() =>
            {
                Assert.That(type.GetUInt32(MediaTypeAttributeKeys.AudioNumChannels), Is.EqualTo(channels));
                Assert.That(type.GetUInt32(MediaTypeAttributeKeys.AudioChannelMask), Is.EqualTo((uint)layout.Speakers));
                Assert.That(type.GetUInt32(MediaTypeAttributeKeys.AudioBlockAlignment), Is.EqualTo(channels * bits / 8));
                Assert.That(type.GetUInt32(MediaTypeAttributeKeys.AudioAvgBytesPerSecond), Is.EqualTo(48000 * channels * bits / 8));
            });
        });
    }

    [TestCase(MFAudioCodec.PCM, 6)]
    [TestCase(MFAudioCodec.Float, 8)]
    [TestCase(MFAudioCodec.AAC, 6)]
    [Platform("Win")]
    public async Task EncodePreservesEachSpeakerTone(MFAudioCodec codec, int channels)
    {
        if (codec == MFAudioCodec.AAC && !OperatingSystem.IsWindowsVersionAtLeast(10))
            Assert.Ignore("Six-channel AAC requires Windows 10 or later.");
        const int rate = 48000;
        string path = Path.Combine(Path.GetTempPath(), $"beutl-speakers-{Guid.NewGuid():N}" + (codec == MFAudioCodec.AAC ? ".m4a" : ".wav"));
        try
        {
            var layout = AudioChannelLayout.GetDefault(channels);
            var controller = new MFEncodingController(path);
            controller.AudioSettings.Codec.Value = codec;
            controller.AudioSettings.SampleRate.Value = rate;
            controller.AudioSettings.Channels.Value = channels;
            using var frames = new GradientFrameProvider(0, new Rational(30), 32, 32);
            using var samples = new SpeakerToneSampleProvider(rate * 2, rate, layout);
            await controller.Encode(frames, samples, CancellationToken.None);
            await MFThread.Dispatcher.InvokeAsync(() =>
            {
                using var source = MediaFactory.MFCreateSourceReaderFromURL(path, null);
                int stream = (int)SourceReaderIndex.FirstAudioStream;
                using var native = source.GetNativeMediaType(stream, 0);
                Assert.That(native.GetUInt32(MediaTypeAttributeKeys.AudioNumChannels), Is.EqualTo(channels));
                using var type = MFEncodingController.CreatePcm(AudioFormatGuids.Float, rate, layout, 32);
                source.SetCurrentMediaType(stream, type);
                var decoded = Enumerable.Range(0, channels).Select(_ => new List<float>()).ToArray();
                while (true)
                {
                    using var sample = source.ReadSample(stream, SourceReaderControlFlag.None, out _, out var flags, out _);
                    if (sample != null)
                    {
                        using var buffer = sample.ConvertToContiguousBuffer();
                        buffer.Lock(out nint data, out _, out int length);
                        try
                        {
                            var values = new float[length / sizeof(float)];
                            Marshal.Copy(data, values, 0, values.Length);
                            for (int i = 0; i < values.Length; i++)
                                decoded[i % channels].Add(values[i]);
                        }
                        finally { buffer.Unlock(); }
                    }
                    if (flags.HasFlag(SourceReaderFlag.EndOfStream)) break;
                }
                for (int ch = 0; ch < channels; ch++)
                    for (int tone = 0; tone < channels; tone++)
                    {
                        double level = SpeakerToneSampleProvider.ToneLevel(decoded[ch].ToArray().AsSpan(rate / 2, rate),
                            SpeakerToneSampleProvider.Frequency(tone), rate);
                        Assert.That(level, tone == ch ? Is.InRange(0.18, 0.30) : Is.LessThan(0.025),
                            $"Speaker {ch}, tone {tone}: {level}");
                    }
            });
        }
        finally { File.Delete(path); }
    }
}
