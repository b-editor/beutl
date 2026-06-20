using System.Runtime.Versioning;

using Beutl.Embedding.MediaFoundation.Decoding;
using Beutl.Media.Decoding;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;

namespace Beutl.Extensions.MediaFoundation.Tests;

// End-to-end coverage for the probe / fallback behavior that this branch hardened:
//   * an audio-only file has no video stream, so MFDecoder throws NoVideoStreamException and
//     MFReader transparently falls back to the NAudio audio path;
//   * requesting video-only from an audio-only file surfaces as a failed open (null reader);
//   * a real H.264 + AAC file decodes both a video frame and audio samples.
// All of this requires the Windows-only Media Foundation runtime.
[TestFixture]
[Platform("Win")]
[SupportedOSPlatform("windows")]
public class MFReaderIntegrationTests
{
    private string _workDir = string.Empty;

    private static string SampleVideoPath
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample.mp4");

    private static IDecoderInfo CreateDecoderInfo() => new MFDecoderInfo(new MFDecodingExtension());

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("Media Foundation is only available on Windows.");
        }
    }

    [SetUp]
    public void SetUp()
    {
        _workDir = Path.Combine(Path.GetTempPath(), "beutl-mf-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDir);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            Directory.Delete(_workDir, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }

    [Test]
    public void Open_AudioOnlyWav_AudioVideoMode_FallsBackToAudioOnly()
    {
        string wav = WriteSineWav();

        using MediaReader? reader = CreateDecoderInfo().Open(wav, new MediaOptions(MediaMode.AudioVideo));

        Assert.That(reader, Is.Not.Null, "an audio-only file should still open via the NAudio fallback");
        Assert.Multiple(() =>
        {
            Assert.That(reader!.HasVideo, Is.False, "the WAV has no video stream");
            Assert.That(reader.HasAudio, Is.True);
            Assert.That(reader.AudioInfo.SampleRate, Is.EqualTo(44100));
            Assert.That(reader.AudioInfo.NumChannels, Is.EqualTo(2));
        });

        bool read = reader!.ReadAudio(0, 4410, out var pcm);
        Assert.That(read, Is.True);
        using (pcm)
        {
            // `read` and a non-null Ref are structurally always true (ReadAudioCore returns true for
            // any non-negative provider count and allocates the buffer unconditionally). The fixture
            // is a 440Hz sine at 0.3 amplitude, so assert the decoded buffer is the requested length
            // AND actually carries the signal rather than silence.
            var samples = ((Pcm<Stereo32BitFloat>)pcm!.Value).DataSpan;
            Assert.That(samples.Length, Is.EqualTo(4410));

            bool nonSilent = false;
            foreach (Stereo32BitFloat s in samples)
            {
                if (Math.Abs(s.Left) > 1e-4f || Math.Abs(s.Right) > 1e-4f)
                {
                    nonSilent = true;
                    break;
                }
            }

            Assert.That(nonSilent, Is.True, "decoded PCM should contain the sine signal, not silence");
        }
    }

    [Test]
    public void Open_AudioOnlyWav_VideoOnlyMode_ReturnsNull()
    {
        string wav = WriteSineWav();

        // No audio flag means NoVideoStreamException is not caught: the open fails and MFDecoderInfo
        // returns null so another decoder can try.
        using MediaReader? reader = CreateDecoderInfo().Open(wav, new MediaOptions(MediaMode.Video));

        Assert.That(reader, Is.Null);
    }

    [Test]
    public void Open_VideoFixture_VideoOnlyMode_DecodesFrame()
    {
        Assert.That(File.Exists(SampleVideoPath), Is.True, $"fixture is missing: {SampleVideoPath}");

        using MediaReader? reader = CreateDecoderInfo().Open(SampleVideoPath, new MediaOptions(MediaMode.Video));

        Assert.That(reader, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(reader!.HasVideo, Is.True);
            Assert.That(reader.HasAudio, Is.False, "audio is not requested in video-only mode");
            Assert.That(reader.VideoInfo.FrameSize.Width, Is.EqualTo(64));
            Assert.That(reader.VideoInfo.FrameSize.Height, Is.EqualTo(64));
        });

        bool read = reader!.ReadVideo(0, out var image);
        Assert.That(read, Is.True, "the first frame should decode");
        using (image)
        {
            Assert.That(image!.Value.Width, Is.EqualTo(64));
            Assert.That(image.Value.Height, Is.EqualTo(64));
        }
    }

    [Test]
    public void Open_VideoFixture_AudioVideoMode_ExposesBothStreams()
    {
        Assert.That(File.Exists(SampleVideoPath), Is.True, $"fixture is missing: {SampleVideoPath}");

        using MediaReader? reader = CreateDecoderInfo().Open(SampleVideoPath, new MediaOptions(MediaMode.AudioVideo));

        Assert.That(reader, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(reader!.HasVideo, Is.True);
            Assert.That(reader.HasAudio, Is.True);
        });

        bool readVideo = reader!.ReadVideo(0, out var image);
        Assert.That(readVideo, Is.True);
        image?.Dispose();

        bool readAudio = reader.ReadAudio(0, 1000, out var pcm);
        Assert.That(readAudio, Is.True);
        // `readAudio` is structurally always true; assert the decoded buffer matches the requested
        // length so a regression in the NAudio length math would actually fail here. The fixture's
        // audio content is unknown, so non-silence is not asserted (unlike the sine-WAV test above).
        Assert.That(pcm!.Value.NumSamples, Is.EqualTo(1000));
        pcm.Dispose();
    }

    // Writes a short 16-bit PCM sine-wave WAV. Media Foundation's WAV byte-stream handler decodes
    // this through the NAudio MediaFoundationReader path used by MFReader, with no external tooling.
    private string WriteSineWav(int sampleRate = 44100, int channels = 2, double seconds = 0.2)
    {
        string path = Path.Combine(_workDir, "audio.wav");

        const short bitsPerSample = 16;
        int totalFrames = (int)(sampleRate * seconds);
        short blockAlign = (short)(channels * bitsPerSample / 8);
        int byteRate = sampleRate * blockAlign;
        int dataSize = totalFrames * blockAlign;

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream);

        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataSize);
        writer.Write("WAVE"u8.ToArray());

        writer.Write("fmt "u8.ToArray());
        writer.Write(16);                   // PCM format chunk size
        writer.Write((short)1);             // PCM
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);

        writer.Write("data"u8.ToArray());
        writer.Write(dataSize);

        for (int i = 0; i < totalFrames; i++)
        {
            double t = (double)i / sampleRate;
            var sample = (short)(Math.Sin(2 * Math.PI * 440 * t) * short.MaxValue * 0.3);
            for (int ch = 0; ch < channels; ch++)
            {
                writer.Write(sample);
            }
        }

        return path;
    }
}
