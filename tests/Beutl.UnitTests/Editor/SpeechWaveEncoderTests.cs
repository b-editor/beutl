using Beutl.Audio;
using Beutl.Editor.Components.AiWorkspaceTab.Services;
using Beutl.UnitTests.Engine.Audio;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public class SpeechWaveEncoderTests
{
    [TestCase(1, false)]
    [TestCase(1, true)]
    [TestCase(2, false)]
    [TestCase(6, false)]
    [TestCase(6, true)]
    [TestCase(9, false)]
    public void WriteSpeechWave_ConvertsNativeChannelsBeforeTheExistingAverage(int channels, bool mismatchedLayout)
    {
        using var samples = AudioTestBuffers.CreateBuffer(channels, 3, (ch, i) => (ch + 1) * (i + 1) / 32f, sampleRate: 16000);
        using var reader = new AudioTestReader(samples, mismatchedLayout ? AudioChannelLayout.Stereo : AudioChannelLayout.GetDefault(channels));
        using var stream = new MemoryStream();
        var result = SpeechWaveEncoder.WriteSpeechWave(reader, 0, 10, stream, CancellationToken.None);
        Assert.That(result.SourceSampleCount, Is.EqualTo(3));
        Assert.That(result.OutputSampleCount, Is.EqualTo(3));
        Assert.That(stream.Length, Is.EqualTo(50));
        byte[] bytes = stream.ToArray();
        for (int i = 0; i < 3; i++)
        {
            float left = samples.GetChannelData(0)[i];
            float right = samples.GetChannelData(Math.Min(1, channels - 1))[i];
            if (channels == 6)
            {
                left += 0.70710677f * samples.GetChannelData(2)[i];
                left += 0.70710677f * samples.GetChannelData(4)[i];
                right += 0.70710677f * samples.GetChannelData(2)[i];
                right += 0.70710677f * samples.GetChannelData(5)[i];
            }
            short expected = (short)Math.Round(Math.Clamp((left + right) / 2d, -1, 1) * short.MaxValue);
            Assert.That(BitConverter.ToInt16(bytes, 44 + i * 2), Is.EqualTo(expected));
        }
    }

    [Test]
    public void WriteSpeechWave_UsesDeclaredPositions()
    {
        var layout = new AudioChannelLayout(4, AudioSpeakers.FrontLeft | AudioSpeakers.FrontRight | AudioSpeakers.FrontCenter | AudioSpeakers.BackCenter);
        using var samples = AudioTestBuffers.CreateBuffer(4, 1, (ch, _) => (ch + 1) / 16f, sampleRate: 16000);
        using var reader = new AudioTestReader(samples, layout);
        using var stream = new MemoryStream();
        SpeechWaveEncoder.WriteSpeechWave(reader, 0, 1, stream, CancellationToken.None);
        float left = 1 / 16f + 0.70710677f * 3 / 16f + 0.5f * 4 / 16f;
        float right = 2 / 16f + 0.70710677f * 3 / 16f + 0.5f * 4 / 16f;
        Assert.That(BitConverter.ToInt16(stream.ToArray(), 44),
            Is.EqualTo((short)Math.Round((left + right) / 2d * short.MaxValue)));
    }

}
