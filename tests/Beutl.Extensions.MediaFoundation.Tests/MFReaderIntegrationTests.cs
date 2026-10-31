using System.Runtime.Versioning;
using Beutl.Audio;
using Beutl.Composition;
using Beutl.Embedding.MediaFoundation.Decoding;
using Beutl.Engine;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Source;

namespace Beutl.Extensions.MediaFoundation.Tests;

// Checks, on the Windows-only Media Foundation runtime, how audio-only files are handled and that real media files
// decode.
[TestFixture]
[Platform("Win")]
[SupportedOSPlatform("windows")]
public class MFReaderIntegrationTests
{
    private string _workDir = string.Empty;

    private static string SampleVideoPath
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample.mp4");

    private static IDecoderInfo CreateDecoderInfo() => new MFDecoderInfo(new MFDecodingExtension());

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
        string wav = SineWav.Write(_workDir);

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
            // ReadAudio signals end of stream with a short buffer, not false. The 440Hz fixture is longer than
            // 4410 frames, so this read is full length and carries the signal rather than silence.
            Assert.That(pcm!.SampleCount, Is.EqualTo(4410));

            bool nonSilent = false;
            for (int i = 0; i < pcm.SampleCount; i++)
            {
                if (Math.Abs(pcm.GetChannelData(0)[i]) > 1e-4f || Math.Abs(pcm.GetChannelData(1)[i]) > 1e-4f)
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
        string wav = SineWav.Write(_workDir);

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
        string wav = SineWav.Write(_workDir, channels: channels);
        using MediaReader? reader = CreateDecoderInfo().Open(wav, new MediaOptions(MediaMode.Audio));
        Assert.That(reader, Is.Not.Null);

        bool read = reader!.ReadAudio(start, 4096, out var pcm);
        Assert.That(read, Is.True);
        using (pcm)
        {
            Assert.That(pcm, Is.Not.Null);
            Assert.That(pcm!.SampleCount, Is.Zero);
            Assert.That(pcm.SampleRate, Is.EqualTo(44100));
            Assert.That(pcm.ChannelCount, Is.EqualTo(channels));
            Assert.That(reader.AudioInfo.ChannelLayout, Is.EqualTo(AudioChannelLayout.GetDefault(channels)));
        }

        // An EOF request must leave the reader usable for a later in-range seek.
        Assert.That(reader.ReadAudio(0, 1024, out var firstSamples), Is.True);
        using (firstSamples)
        {
            Assert.That(firstSamples!.SampleCount, Is.EqualTo(1024));
            Assert.That(firstSamples.ChannelCount, Is.EqualTo(channels));
        }
    }

    [TestCase(1)]
    [TestCase(2)]
    public void ReadAudio_CrossingEndOfStream_PreservesRemainingSamples(int channels)
    {
        string wav = SineWav.Write(_workDir, channels: channels);
        using MediaReader? reader = CreateDecoderInfo().Open(wav, new MediaOptions(MediaMode.Audio));
        Assert.That(reader, Is.Not.Null);

        bool read = reader!.ReadAudio(4410, 8820, out var pcm);
        Assert.That(read, Is.True);
        using (pcm)
        {
            Assert.That(pcm!.SampleCount, Is.EqualTo(4410));
            Assert.That(pcm.GetChannelData(0).ToArray().Any(sample => Math.Abs(sample) > 1e-4f), Is.True);
        }
    }

    [TestCase(4, 0x33u)]
    [TestCase(6, 0x3Fu)]
    [TestCase(6, 0x60Fu)]
    public void ReadAudio_ExtensibleWavePreservesNativeChannelsAndMask(int channels, uint mask)
    {
        string path = Path.Combine(_workDir, "native.wav");
        const int frames = 4800;
        const int sampleRate = 48000;
        using (var writer = new BinaryWriter(File.Create(path)))
        {
            int dataLength = frames * channels * sizeof(short);
            writer.Write("RIFF"u8);
            writer.Write(60 + dataLength);
            writer.Write("WAVEfmt "u8);
            writer.Write(40);
            writer.Write((ushort)0xFFFE);
            writer.Write((ushort)channels);
            writer.Write(sampleRate);
            writer.Write(sampleRate * channels * sizeof(short));
            writer.Write((ushort)(channels * sizeof(short)));
            writer.Write((ushort)16);
            writer.Write((ushort)22);
            writer.Write((ushort)16);
            writer.Write(mask);
            writer.Write(new Guid("00000001-0000-0010-8000-00aa00389b71").ToByteArray());
            writer.Write("data"u8);
            writer.Write(dataLength);
            for (int i = 0; i < frames; i++)
                for (int ch = 0; ch < channels; ch++)
                    writer.Write((short)((ch + 1) * 1000 + i));
        }
        using var reader = CreateDecoderInfo().Open(path, new MediaOptions(MediaMode.Audio));
        Assert.That(reader, Is.Not.Null);
        Assert.That(reader!.AudioInfo.NumChannels, Is.EqualTo(channels));
        Assert.That(reader.AudioInfo.ChannelLayout, Is.EqualTo(new AudioChannelLayout(channels, (AudioSpeakers)mask)));
        Assert.That(reader.ReadAudio(0, 4, out var audio), Is.True);
        using (audio)
        {
            Assert.That(audio!.ChannelCount, Is.EqualTo(channels));
            Assert.That(audio.SampleCount, Is.EqualTo(4));
            for (int ch = 0; ch < channels; ch++)
                Assert.That(audio.GetChannelData(ch).ToArray(),
                    Is.EqualTo(Enumerable.Range(0, 4).Select(i => ((ch + 1) * 1000 + i) / 32768f)));
        }
        Assert.That(reader.ReadAudio(0, 0, out var empty), Is.True);
        using (empty)
        {
            Assert.That(empty!.ChannelCount, Is.EqualTo(channels));
            Assert.That(empty.SampleCount, Is.Zero);
        }
    }

    [NonParallelizable]
    [TestCase(3d, 0d, 100f)]
    [TestCase(2d, 0.5d, 100f)]
    [TestCase(2d, 0d, 200f)]
    public async Task GetWaveformChunks_SourceRangePastEnd_ProducesAllChunksWithSilentTail(
        double duration, double offset, float speed)
    {
        string wav = SineWav.Write(_workDir, seconds: 2);
        IDecoderInfo decoder = CreateDecoderInfo();
        DecoderRegistry.Register(decoder);
        try
        {
            var source = new SoundSource();
            source.ReadFrom(new Uri(wav));
            // A private reader, so the check sees what the decoder opened rather than a shared handle.
            using var metadata = source.ToResource(new CompositionContext(MediaTime.Zero) { DisableResourceShare = true });
            Assert.That(metadata.MediaReader, Is.TypeOf<MFReader>());

            var sound = new SourceSound
            {
                Source = { Value = source },
                TimeRange = { Value = new TimeRange(MediaTime.FromSeconds(5), MediaTime.FromSeconds(duration)) },
                OffsetPosition = { Value = MediaTime.FromSeconds(offset) },
                Speed = { Value = speed }
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
        Assert.That(pcm!.SampleCount, Is.EqualTo(1000));
        pcm.Dispose();
    }
}
