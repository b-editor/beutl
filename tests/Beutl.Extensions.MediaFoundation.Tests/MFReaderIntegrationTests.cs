using System.Runtime.Versioning;

using Beutl.Audio;
using Beutl.Composition;
using Beutl.Embedding.MediaFoundation.Decoding;
using Beutl.Engine;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Beutl.Media.Source;

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
            // Under the ReadAudio contract, ReadAudioCore always returns true with a buffer once the
            // reader is initialized — end-of-stream is signalled by a short/empty NumSamples, not by
            // false. The fixture is a 440Hz sine at 0.3 amplitude longer than 4410 frames, so an in-range
            // read yields the requested length and carries the signal rather than silence.
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

    [TestCase(1, 8820)]
    [TestCase(1, 13230)]
    [TestCase(2, 8820)]
    [TestCase(2, 13230)]
    [TestCase(2, int.MaxValue)]
    public void ReadAudio_AtOrPastEndOfStream_ReturnsEmptyBuffer(int channels, int start)
    {
        // The 0.2s fixture has 8820 frames, independently of its channel count.
        string wav = WriteSineWav(channels: channels);
        using MediaReader? reader = CreateDecoderInfo().Open(wav, new MediaOptions(MediaMode.Audio));
        Assert.That(reader, Is.Not.Null);

        bool read = reader!.ReadAudio(start, 4096, out var pcm);
        Assert.That(read, Is.True);
        using (pcm)
        {
            Assert.That(pcm, Is.Not.Null);
            Assert.That(pcm!.Value.NumSamples, Is.Zero);
            Assert.That(pcm.Value.SampleRate, Is.EqualTo(44100));
            Assert.That(pcm.Value.NumChannels, Is.EqualTo(2));
        }

        // An EOF request must leave the reader usable for a later in-range seek.
        Assert.That(reader.ReadAudio(0, 1024, out var firstSamples), Is.True);
        using (firstSamples)
        {
            Assert.That(firstSamples!.Value.NumSamples, Is.EqualTo(1024));
        }
    }

    [TestCase(1)]
    [TestCase(2)]
    public void ReadAudio_CrossingEndOfStream_PreservesRemainingSamples(int channels)
    {
        string wav = WriteSineWav(channels: channels);
        using MediaReader? reader = CreateDecoderInfo().Open(wav, new MediaOptions(MediaMode.Audio));
        Assert.That(reader, Is.Not.Null);

        bool read = reader!.ReadAudio(4410, 8820, out var pcm);
        Assert.That(read, Is.True);
        using (pcm)
        {
            Assert.That(pcm!.Value.NumSamples, Is.EqualTo(4410));
            var samples = ((Pcm<Stereo32BitFloat>)pcm.Value).DataSpan;
            Assert.That(samples.ToArray().Any(sample => Math.Abs(sample.Left) > 1e-4f), Is.True);
        }
    }

    [NonParallelizable]
    [TestCase(3d, 0d, 100f)]
    [TestCase(2d, 0.5d, 100f)]
    [TestCase(2d, 0d, 200f)]
    public async Task GetWaveformChunks_SourceRangePastEnd_ProducesAllChunksWithSilentTail(
        double duration, double offset, float speed)
    {
        string wav = WriteSineWav(seconds: 2);
        IDecoderInfo decoder = CreateDecoderInfo();
        DecoderRegistry.Register(decoder);
        try
        {
            var source = new SoundSource();
            source.ReadFrom(new Uri(wav));
            using var metadata = source.ToResource(CompositionContext.Default);
            Assert.That(metadata.MediaReader, Is.TypeOf<MFReader>());

            var sound = new SourceSound
            {
                Source = { CurrentValue = source },
                TimeRange = new TimeRange(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(duration)),
                OffsetPosition = { CurrentValue = TimeSpan.FromSeconds(offset) },
                Speed = { CurrentValue = speed }
            };

            var chunks = new List<WaveformChunk>();
            await foreach (var chunk in sound.GetWaveformChunksAsync(20, 4096, null))
            {
                chunks.Add(chunk);
            }

            Assert.That(chunks.Select(chunk => chunk.Index), Is.EqualTo(Enumerable.Range(0, 20)));
            Assert.That(chunks[0].MinValue, Is.LessThan(-0.1f));
            Assert.That(chunks[0].MaxValue, Is.GreaterThan(0.1f));
            Assert.That(chunks[^1].MinValue, Is.EqualTo(0f).Within(1e-6f));
            Assert.That(chunks[^1].MaxValue, Is.EqualTo(0f).Within(1e-6f));
        }
        finally
        {
            DecoderRegistry.Unregister(decoder);
        }
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
