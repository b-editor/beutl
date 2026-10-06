using Beutl.Extensions.AVFoundation.Decoding;
using Beutl.Media.Decoding;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using SystemEncoding = System.Text.Encoding;

namespace Beutl.Extensions.AVFoundation.Tests;

[TestFixture]
[Platform("MacOSX")]
public class AVFReaderTests
{
    private const int SampleRate = 48000;
    private const int FrameCount = 1000;
    private const int LongFrameCount = SampleRate * 2;
    private string _workDir = null!;
    private string _file = null!;
    private string _longFile = null!;
    private string _silentFile = null!;

    [OneTimeSetUp]
    public void SetUp()
    {
        _workDir = Path.Combine(Path.GetTempPath(), $"beutl-avf-reader-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workDir);
        _file = Path.Combine(_workDir, "ramp.wav");
        _longFile = Path.Combine(_workDir, "long-ramp.wav");
        _silentFile = Path.Combine(_workDir, "silence.wav");
        WriteWaveFile(_file, FrameCount);
        WriteWaveFile(_longFile, LongFrameCount);
        WriteWaveFile(_silentFile, FrameCount, silent: true);
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        Directory.Delete(_workDir, recursive: true);
    }

    [TestCase(0, 128, 128)]
    [TestCase(250, 300, 300)]
    [TestCase(0, 1500, FrameCount)]
    [TestCase(900, 400, 100)]
    [TestCase(FrameCount, 128, 0)]
    [TestCase(FrameCount + 10, 128, 0)]
    [TestCase(30000, 128, 0)]
    [TestCase(30001, 128, 0)]
    [TestCase(SampleRate, 128, 0)]
    [TestCase(0, 0, 0)]
    public void ReadAudio_ReturnsDecodedFrames(int start, int length, int expectedFrames)
    {
        using var reader = OpenReader(_file);
        AssertRead(reader, start, length, expectedFrames);
    }

    [Test]
    public void ReadAudio_CachedReadsAndRepeatedEof_PreserveSamplesAndCounts()
    {
        using var reader = OpenReader(_file);
        AssertRead(reader, 0, FrameCount, FrameCount);
        AssertRead(reader, 250, 300, 300);
        AssertRead(reader, 900, 400, 100);
        AssertRead(reader, 900, 400, 100);
        AssertRead(reader, FrameCount, 128, 0);
        AssertRead(reader, 100, 128, 128);
    }

    [Test]
    public void ReadAudio_SeekNearEof_ReturnsShortRead()
    {
        using var reader = OpenReader(_longFile);
        AssertRead(reader, LongFrameCount - 100, 400, 100);
    }

    [Test]
    public void ReadAudio_AcrossMultipleNativeBuffers_ReturnsDecodedFrames()
    {
        using var reader = OpenReader(_longFile);
        AssertRead(reader, 0, LongFrameCount + 400, LongFrameCount);
    }

    [Test]
    public void ReadAudio_SeekPastEof_DoesNotPreventSubsequentReads()
    {
        using var reader = OpenReader(_file);
        AssertRead(reader, SampleRate, 128, 0);
        AssertRead(reader, 100, 128, 128);
    }

    [Test]
    public void ReadAudio_SilentFramesStillCountAsDecoded()
    {
        using var reader = OpenReader(_silentFile);
        AssertRead(reader, 900, 400, 100, silent: true);
    }

    [Test]
    public void ReadAudio_Disposed_ReturnsFalse()
    {
        using var reader = OpenReader(_file);
        reader.Dispose();

        Assert.That(reader.ReadAudio(0, 128, out var sound), Is.False);
        Assert.That(sound, Is.Null);
    }

    private static AVFReader OpenReader(string file)
    {
        return new AVFReader(file, new MediaOptions(MediaMode.Audio), new AVFDecodingExtension());
    }

    private static void AssertRead(AVFReader reader, int start, int length, int expectedFrames, bool silent = false)
    {
        Assert.That(reader.ReadAudio(start, length, out var sound), Is.True);
        using (sound)
        {
            var pcm = (Pcm<Stereo32BitFloat>)sound!.Value;
            Assert.That(pcm.NumSamples, Is.EqualTo(expectedFrames));
            Assert.That(pcm.SampleRate, Is.EqualTo(SampleRate));
            Assert.That(pcm.NumChannels, Is.EqualTo(2));

            for (int i = 0; i < pcm.NumSamples; i++)
            {
                float expected = silent ? 0 : SampleForFrame(start + i) / 32768f;
                Assert.That(pcm.DataSpan[i].Left, Is.EqualTo(expected).Within(1e-6f));
                Assert.That(pcm.DataSpan[i].Right, Is.EqualTo(-expected).Within(1e-6f));
            }
        }
    }

    private static short SampleForFrame(int frame) => (short)(1024 + frame % 2048);

    // PCM avoids codec priming/padding and gives an exact frame count independent of the AVF encoder.
    private static void WriteWaveFile(string path, int frames, bool silent = false)
    {
        const short channels = 2;
        const short blockAlign = channels * sizeof(short);
        int dataSize = frames * blockAlign;
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write(SystemEncoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataSize);
        writer.Write(SystemEncoding.ASCII.GetBytes("WAVE"));
        writer.Write(SystemEncoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(SampleRate);
        writer.Write(SampleRate * blockAlign);
        writer.Write(blockAlign);
        writer.Write((short)16);
        writer.Write(SystemEncoding.ASCII.GetBytes("data"));
        writer.Write(dataSize);
        for (int i = 0; i < frames; i++)
        {
            short sample = silent ? (short)0 : SampleForFrame(i);
            writer.Write(sample);
            writer.Write((short)-sample);
        }
    }
}
