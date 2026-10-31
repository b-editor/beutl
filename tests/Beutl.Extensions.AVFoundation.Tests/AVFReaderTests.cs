using System.Diagnostics;
using Beutl.Audio;
using Beutl.Extensions.AVFoundation.Decoding;
using Beutl.Extensions.AVFoundation.Interop;
using Beutl.Media.Decoding;
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

    [Test]
    public void OpenIncompleteMp4_PreservesNativeErrorDetails()
    {
        if (!OperatingSystem.IsMacOSVersionAtLeast(13))
            Assert.Ignore("Async track loading requires macOS 13 or later.");

        string file = Path.Combine(_workDir, $".clip.beutl-part-{Guid.NewGuid():N}.mp4");
        File.WriteAllBytes(file, []);

        var error = Assert.Throws<BeutlAVFException>(() =>
        {
            using var reader = new AVFReader(file, new MediaOptions(MediaMode.Video), new AVFDecodingExtension());
        });
        Assert.Multiple(() =>
        {
            Assert.That(error!.Code, Is.EqualTo(-103));
            Assert.That(error.Message, Does.Contain("Reader failed: loadTracks failed:"));
            Assert.That(error.Message, Does.Contain("domain=AVFoundationErrorDomain"));
            Assert.That(error.Message, Does.Contain("code="));
        });

        using var validReader = OpenReader(_file);
        Assert.That(validReader.HasAudio, Is.True, "A failed open must not prevent subsequent valid reads.");
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

    // Like the other decoders, the reader opens the requested streams the file has, and fails without any of them.
    [Test]
    public void Open_OpensTheRequestedStreamsTheFileHas()
    {
        using var reader = new AVFReader(_file, new MediaOptions(MediaMode.AudioVideo), new AVFDecodingExtension());

        Assert.Multiple(() =>
        {
            Assert.That(reader.HasAudio, Is.True);
            Assert.That(reader.HasVideo, Is.False);
            Assert.That(
                () => new AVFReader(_file, new MediaOptions(MediaMode.Video), new AVFDecodingExtension()),
                Throws.InvalidOperationException);
        });
    }

    [Test]
    public void ReadAudio_MonoKeepsOneChannelAtUnityGain()
    {
        string file = Path.Combine(_workDir, "mono.wav");
        WriteWaveFile(file, FrameCount, channels: 1);
        using var reader = OpenReader(file);
        AssertLayout(reader, AudioChannelLayout.Mono);
        Assert.That(reader.ReadAudio(0, FrameCount + 100, out var sound), Is.True);
        using (sound)
        {
            Assert.That(sound!.ChannelCount, Is.EqualTo(1));
            Assert.That(sound.SampleCount, Is.EqualTo(FrameCount));
            for (int i = 0; i < FrameCount; i++)
                Assert.That(sound.GetChannelData(0)[i], Is.EqualTo(SampleForFrame(i) / 32768f).Within(1e-6f));
        }
        Assert.That(reader.ReadAudio(0, 0, out var empty), Is.True);
        using (empty)
        {
            Assert.That(empty!.ChannelCount, Is.EqualTo(1));
            Assert.That(empty.SampleCount, Is.Zero);
        }
    }

    [TestCase(4, 0x33u)]
    [TestCase(6, 0x3fu)]
    [TestCase(8, 0x63fu)]
    // Mask-0 cases reach the DiscreteInOrder fallback.
    [TestCase(4, 0u)]
    [TestCase(6, 0u)]
    public void ReadAudio_MultichannelWaveKeepsWaveOrderAcrossCacheSeekAndEof(int channels, uint mask)
    {
        string file = Path.Combine(_workDir, $"tones-{channels}.wav");
        WriteToneWaveFile(file, channels, mask);
        using var reader = OpenReader(file);
        AssertLayout(reader, new AudioChannelLayout(channels, (AudioSpeakers)mask));

        // The last two channels of 7.1 must be SL/SR, after BL/BR, including after a seek.
        (int Start, int Length, int Expected)[] reads =
        [
            (0, 24000, 24000),
            (6000, 12000, 12000),
            (60000, 24000, 24000),
            (48000, 12000, 12000),
            (LongFrameCount - 12000, 24000, 12000),
            (LongFrameCount, 128, 0),
            (0, 0, 0)
        ];
        foreach (var read in reads)
        {
            Assert.That(reader.ReadAudio(read.Start, read.Length, out var sound), Is.True);
            using (sound)
            {
                Assert.That(sound!.ChannelCount, Is.EqualTo(channels));
                Assert.That(sound.SampleRate, Is.EqualTo(SampleRate));
                Assert.That(sound.SampleCount, Is.EqualTo(read.Expected));
                if (read.Expected > 0)
                    AssertChannelTones(sound);
            }
        }
    }

    [TestCase(1, 0x4u)]
    [TestCase(6, 0x3fu)]
    public void ReadAudio_AacKeepsNativeLayoutAndWaveOrder(int channels, uint mask)
    {
        string wave = Path.Combine(_workDir, $"aac-source-{channels}.wav");
        string file = Path.Combine(_workDir, $"tones-{channels}.m4a");
        WriteToneWaveFile(wave, channels, mask);
        EncodeAac(wave, file, channels);
        using var reader = OpenReader(file);
        AssertLayout(reader, new AudioChannelLayout(channels, (AudioSpeakers)mask));
        // Stay away from codec priming and padding; phase-independent tone checks tolerate AAC loss.
        Assert.That(reader.ReadAudio(SampleRate / 2, SampleRate / 2, out var sound), Is.True);
        using (sound)
        {
            Assert.That(sound!.ChannelCount, Is.EqualTo(channels));
            Assert.That(sound.SampleRate, Is.EqualTo(SampleRate));
            Assert.That(sound.SampleCount, Is.EqualTo(SampleRate / 2));
            AssertChannelTones(sound);
        }
    }

    private static void AssertLayout(AVFReader reader, AudioChannelLayout layout)
    {
        Assert.Multiple(() =>
        {
            Assert.That(reader.AudioInfo.NumChannels, Is.EqualTo(layout.ChannelCount));
            Assert.That(reader.AudioInfo.ChannelLayout, Is.EqualTo(layout));
        });
    }

    // The LFE tone is below the AAC low-pass cutoff. All tones have whole periods in a quarter second.
    private static readonly int[] s_frequencies = [300, 500, 700, 80, 900, 1100, 1300, 1500];

    private static void AssertChannelTones(AudioBuffer sound)
    {
        for (int channel = 0; channel < sound.ChannelCount; channel++)
        {
            ReadOnlySpan<float> samples = sound.GetChannelData(channel);
            for (int tone = 0; tone < sound.ChannelCount; tone++)
            {
                double real = 0;
                double imaginary = 0;
                double step = 2 * Math.PI * s_frequencies[tone] / SampleRate;
                for (int i = 0; i < samples.Length; i++)
                {
                    real += samples[i] * Math.Cos(step * i);
                    imaginary += samples[i] * Math.Sin(step * i);
                }
                double amplitude = 2 * Math.Sqrt(real * real + imaginary * imaginary) / samples.Length;
                Assert.That(amplitude, Is.EqualTo(channel == tone ? 0.25 : 0).Within(0.025),
                    $"Channel {channel}, {s_frequencies[tone]} Hz: channels must retain WAVE order without mixing.");
            }
        }
    }

    private static void WriteToneWaveFile(string path, int channels, uint mask)
    {
        int blockAlign = channels * sizeof(short);
        int dataSize = LongFrameCount * blockAlign;
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write(SystemEncoding.ASCII.GetBytes("RIFF"));
        writer.Write(60 + dataSize);
        writer.Write(SystemEncoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(40);
        writer.Write((ushort)0xfffe); // WAVEFORMATEXTENSIBLE
        writer.Write((ushort)channels);
        writer.Write(SampleRate);
        writer.Write(SampleRate * blockAlign);
        writer.Write((ushort)blockAlign);
        writer.Write((ushort)16);
        writer.Write((ushort)22);
        writer.Write((ushort)16);
        writer.Write(mask);
        writer.Write(new Guid("00000001-0000-0010-8000-00aa00389b71").ToByteArray());
        writer.Write(SystemEncoding.ASCII.GetBytes("data"));
        writer.Write(dataSize);
        for (int frame = 0; frame < LongFrameCount; frame++)
        {
            for (int channel = 0; channel < channels; channel++)
                writer.Write((short)Math.Round(8192 * Math.Sin(2 * Math.PI * s_frequencies[channel] * frame / SampleRate)));
        }
    }

    private static void EncodeAac(string source, string destination, int channels)
    {
        string? ffmpeg = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Select(directory => Path.Combine(directory, "ffmpeg")).FirstOrDefault(File.Exists);
        string? encoder = ffmpeg ?? (File.Exists("/usr/bin/afconvert") ? "/usr/bin/afconvert" : null);
        if (encoder == null)
            Assert.Ignore("AAC fixture generation requires ffmpeg or afconvert.");

        var info = new ProcessStartInfo(encoder!)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        string bitrate = channels == 1 ? "128000" : "384000";
        string[] arguments = ffmpeg != null
            ? ["-hide_banner", "-loglevel", "error", "-y", "-i", source, "-c:a", "aac", "-b:a", bitrate, destination]
            : [source, destination, "-f", "m4af", "-d", "aac ", "-b", bitrate];
        foreach (string argument in arguments)
            info.ArgumentList.Add(argument);
        if (ffmpeg == null && channels == 6)
        {
            // afconvert needs an explicit AAC layout for 5.1.
            info.ArgumentList.Add("-l");
            info.ArgumentList.Add("AAC_5_1");
        }
        using var process = Process.Start(info)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30000))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("AAC fixture generation timed out.");
        }
        Assert.That(process.ExitCode, Is.Zero, $"AAC fixture generation failed: {output.GetAwaiter().GetResult()} {error.GetAwaiter().GetResult()}");
    }

    private static AVFReader OpenReader(string file)
    {
        return new AVFReader(file, new MediaOptions(MediaMode.Audio), new AVFDecodingExtension());
    }

    private static void AssertRead(AVFReader reader, int start, int length, int expectedFrames, bool silent = false)
    {
        AssertLayout(reader, AudioChannelLayout.Stereo);
        Assert.That(reader.ReadAudio(start, length, out var sound), Is.True);
        using (sound)
        {
            Assert.That(sound!.SampleCount, Is.EqualTo(expectedFrames));
            Assert.That(sound.SampleRate, Is.EqualTo(SampleRate));
            Assert.That(sound.ChannelCount, Is.EqualTo(2));

            for (int i = 0; i < sound.SampleCount; i++)
            {
                float expected = silent ? 0 : SampleForFrame(start + i) / 32768f;
                Assert.That(sound.GetChannelData(0)[i], Is.EqualTo(expected).Within(1e-6f));
                Assert.That(sound.GetChannelData(1)[i], Is.EqualTo(-expected).Within(1e-6f));
            }
        }
    }

    private static short SampleForFrame(int frame) => (short)(1024 + frame % 2048);

    // PCM avoids codec priming/padding and gives an exact frame count independent of the AVF encoder.
    private static void WriteWaveFile(string path, int frames, bool silent = false, short channels = 2)
    {
        short blockAlign = (short)(channels * sizeof(short));
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
            if (channels == 2)
                writer.Write((short)-sample);
        }
    }
}
