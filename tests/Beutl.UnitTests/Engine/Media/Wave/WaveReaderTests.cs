using Beutl.Audio;
using Beutl.Media.Wave;
using Beutl.UnitTests.Engine.Audio;

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
    public void Open_ACorruptFileFailsWithoutLeavingAFinalizerThatCrashes()
    {
        string corrupt = Path.Combine(Path.GetTempPath(), $"corrupt-{Guid.NewGuid():N}.wav");
        File.WriteAllBytes(corrupt, [0, 1, 2, 3]);
        try
        {
            Assert.That(new WaveDecoderInfo().Open(corrupt, new Beutl.Media.Decoding.MediaOptions()), Is.Null);

            // An unhandled exception on the finalizer thread ends the test host, so getting
            // past this line is the assertion.
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        finally
        {
            File.Delete(corrupt);
        }
    }

    [Test]
    public void ReadAudio_InRange_ReturnsRequestedLengthAndMatchingSamples()
    {
        using var reader = new WaveReader(_file);

        const int length = 500;
        Assert.That(reader.ReadAudio(0, length, out AudioBuffer? sound), Is.True);
        using (sound)
        {
            Assert.That(sound!.SampleCount, Is.EqualTo(length));

            Span<float> left = sound.GetChannelData(0);
            Assert.That(left[0], Is.EqualTo(SampleForFrame(0)).Within(1e-3f));
            Assert.That(left[^1], Is.EqualTo(SampleForFrame(length - 1)).Within(1e-3f));
            Assert.That(sound.ChannelCount, Is.EqualTo(1));
            Assert.That(reader.AudioInfo.NumChannels, Is.EqualTo(1));
            Assert.That(reader.AudioInfo.ChannelLayout, Is.EqualTo(AudioChannelLayout.Mono));
        }
    }

    [Test]
    public void ReadAudio_WithNonZeroStart_ReturnsSamplesFromOffset()
    {
        using var reader = new WaveReader(_file);

        const int start = 250;
        const int length = 300;
        Assert.That(reader.ReadAudio(start, length, out AudioBuffer? sound), Is.True);
        using (sound)
        {
            Assert.That(sound!.SampleCount, Is.EqualTo(length));

            Span<float> left = sound.GetChannelData(0);
            // The first decoded frame must map to the requested start index, proving the seek-then-read
            // path positions the reader before decoding.
            Assert.That(left[0], Is.EqualTo(SampleForFrame(start)).Within(1e-3f));
            Assert.That(left[^1], Is.EqualTo(SampleForFrame(start + length - 1)).Within(1e-3f));
        }
    }

    [Test]
    public void ReadAudio_StartsAtTheRequestedFrameForEveryOffset()
    {
        using var reader = new WaveReader(_file);

        // Neighbouring frames differ by 5e-4, so the tolerance tells them apart.
        for (int start = 0; start < FrameCount; start++)
        {
            Assert.That(reader.ReadAudio(start, 1, out AudioBuffer? sound), Is.True);
            using (sound)
            {
                Assert.That(sound!.GetChannelData(0)[0], Is.EqualTo(SampleForFrame(start)).Within(1e-4f), $"start={start}");
            }
        }
    }

    [Test]
    public void ReadAudio_RequestCrossesEof_ReturnsShortReadOfActualSamples()
    {
        using var reader = new WaveReader(_file);

        const int length = 1500; // exceeds the 1000-frame file
        Assert.That(reader.ReadAudio(0, length, out AudioBuffer? sound), Is.True);
        using (sound)
        {
            Assert.That(sound!.SampleCount, Is.EqualTo(FrameCount),
                "a read crossing EOF must report the actual decoded sample count, not the requested length");

            Assert.That(sound.GetChannelData(0)[FrameCount - 1], Is.EqualTo(SampleForFrame(FrameCount - 1)).Within(1e-3f));
        }
    }

    [Test]
    public void ReadAudio_StartPastEof_ReturnsTrueWithEmptyBuffer()
    {
        using var reader = new WaveReader(_file);

        // Per the ReadAudio contract, end-of-stream is signalled by SampleCount == 0, not by false:
        // false is reserved for disposed/no-stream/error.
        Assert.That(reader.ReadAudio(FrameCount * 2, 100, out AudioBuffer? sound), Is.True);
        using (sound)
        {
            Assert.That(sound!.SampleCount, Is.Zero);
            Assert.That(sound.ChannelCount, Is.EqualTo(1));
        }
    }

    [Test]
    public void ReadAudio_ZeroLength_ReturnsTrueWithEmptyBuffer()
    {
        using var reader = new WaveReader(_file);

        Assert.That(reader.ReadAudio(0, 0, out AudioBuffer? sound), Is.True);
        using (sound)
        {
            Assert.That(sound!.SampleCount, Is.Zero);
            Assert.That(sound.ChannelCount, Is.EqualTo(1));
        }
    }

    [TestCase(1, false, 0u, 0x4u)]
    [TestCase(2, false, 0u, 0x3u)]
    [TestCase(4, false, 0u, 0u)]
    [TestCase(6, false, 0u, 0u)]
    [TestCase(9, false, 0u, 0u)]
    [TestCase(4, true, 0x33u, 0x33u)]
    [TestCase(4, true, 0x603u, 0x603u)]
    [TestCase(6, true, 0x3Fu, 0x3Fu)]
    [TestCase(6, true, 0x60Fu, 0x60Fu)]
    [TestCase(4, true, 0u, 0u)]
    [TestCase(6, true, 0u, 0u)]
    [TestCase(6, true, 0x33u, 0u)]
    [TestCase(6, true, 0x8000003Fu, 0x3Fu)]
    [TestCase(6, true, 0x8000001Fu, 0u)]
    public void ReadAudio_ReturnsNativeChannelOrderAndDeclaredLayout(int channels, bool extensible, uint mask, uint expectedMask)
    {
        AudioTestWaves.WithTemporaryWave(SampleRate, 8, (ch, i) => (ch * 8 + i) / 128f, source =>
        {
            using var reader = new WaveReader(source.Uri.LocalPath);
            Assert.That(reader.AudioInfo.NumChannels, Is.EqualTo(channels));
            Assert.That(reader.AudioInfo.ChannelLayout, Is.EqualTo(new AudioChannelLayout(channels, (AudioSpeakers)expectedMask)));
            Assert.That(reader.ReadAudio(2, 20, out var audio), Is.True);
            using (audio)
            {
                Assert.That(audio!.ChannelCount, Is.EqualTo(channels));
                Assert.That(audio.SampleCount, Is.EqualTo(6));
                for (int ch = 0; ch < channels; ch++)
                    Assert.That(audio.GetChannelData(ch).ToArray(), Is.EqualTo(Enumerable.Range(2, 6).Select(i => (ch * 8 + i) / 128f)));
            }
            foreach ((int start, int length) in new[] { (0, 0), (8, 4), (12, 4) })
            {
                Assert.That(reader.ReadAudio(start, length, out var empty), Is.True);
                using (empty)
                {
                    Assert.That(empty!.ChannelCount, Is.EqualTo(channels));
                    Assert.That(empty.SampleCount, Is.Zero);
                }
            }
        }, channels, extensible ? (AudioSpeakers)mask : null);
    }
}
