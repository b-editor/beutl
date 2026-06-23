using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Beutl.Media.Source;
using Beutl.Media.Wave;

using NAudio.Wave;

namespace Beutl.UnitTests.Engine.Media.Wave;

[TestFixture]
public class WaveReaderTests
{
    private const int SampleRate = 44100;
    private const int FrameCount = 1000;

    private string _file = null!;

    // Writes a mono 16-bit PCM WAV of FrameCount frames whose Nth sample encodes N, so a decoded
    // frame can be mapped back to its source index regardless of where the read started.
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _file = Path.Combine(Path.GetTempPath(), $"beutl-wavereader-{Guid.NewGuid():N}.wav");
        var format = new WaveFormat(SampleRate, 16, 1);
        using var writer = new WaveFileWriter(_file, format);
        for (int i = 0; i < FrameCount; i++)
        {
            writer.WriteSample(SampleForFrame(i));
        }
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        if (File.Exists(_file))
            File.Delete(_file);
    }

    private static float SampleForFrame(int frame) => frame / (float)FrameCount * 0.5f;

    [Test]
    public void ReadAudio_InRange_ReturnsRequestedLengthAndMatchingSamples()
    {
        using var reader = new WaveReader(_file);

        const int length = 500;
        Assert.That(reader.ReadAudio(0, length, out Ref<IPcm>? sound), Is.True);
        using (sound)
        {
            var pcm = (Pcm<Stereo32BitFloat>)sound!.Value;
            Assert.That(pcm.NumSamples, Is.EqualTo(length));

            Span<Stereo32BitFloat> data = pcm.DataSpan;
            Assert.That(data[0].Left, Is.EqualTo(SampleForFrame(0)).Within(1e-3f));
            Assert.That(data[^1].Left, Is.EqualTo(SampleForFrame(length - 1)).Within(1e-3f));
            // Mono source is duplicated to both channels.
            Assert.That(data[^1].Right, Is.EqualTo(data[^1].Left).Within(1e-6f));
        }
    }

    [Test]
    public void ReadAudio_RequestCrossesEof_ReturnsShortReadOfActualSamples()
    {
        using var reader = new WaveReader(_file);

        const int length = 1500; // exceeds the 1000-frame file
        Assert.That(reader.ReadAudio(0, length, out Ref<IPcm>? sound), Is.True);
        using (sound)
        {
            var pcm = (Pcm<Stereo32BitFloat>)sound!.Value;
            Assert.That(pcm.NumSamples, Is.EqualTo(FrameCount),
                "a read crossing EOF must report the actual decoded sample count, not the requested length");

            Span<Stereo32BitFloat> data = pcm.DataSpan;
            Assert.That(data[FrameCount - 1].Left, Is.EqualTo(SampleForFrame(FrameCount - 1)).Within(1e-3f));
        }
    }

    [Test]
    public void ReadAudio_StartPastEof_ReturnsFalse()
    {
        using var reader = new WaveReader(_file);

        Assert.That(reader.ReadAudio(FrameCount * 2, 100, out Ref<IPcm>? sound), Is.False);
        Assert.That(sound, Is.Null);
    }

    [Test]
    public void ReadAudio_ZeroLength_ReturnsTrueWithEmptyPcm()
    {
        using var reader = new WaveReader(_file);

        Assert.That(reader.ReadAudio(0, 0, out Ref<IPcm>? sound), Is.True);
        using (sound)
        {
            Assert.That(sound!.Value.NumSamples, Is.Zero);
        }
    }
}
