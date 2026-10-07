using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Beutl.Embedding.MediaFoundation.Decoding;
using Beutl.Media.Decoding;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;

namespace Beutl.Extensions.MediaFoundation.Tests;

[TestFixture]
[Platform("Win")]
[SupportedOSPlatform("windows")]
public class MFAudioVideoSyncTests
{
    [TestCase("marker-bframes.mp4", MediaMode.Audio)]
    [TestCase("marker-bframes.mp4", MediaMode.AudioVideo)]
    [TestCase("marker-bframes-no-edit.mp4", MediaMode.Audio)]
    [TestCase("marker-bframes-no-edit.mp4", MediaMode.AudioVideo)]
    public void ReadAudio_SeeksStayAlignedWithVideo(string fixture, MediaMode mode)
    {
        using var audio = Open(fixture, mode);
        using var video = Open(fixture, MediaMode.Video);

        // The picture changes from red to blue at 1 s, when the tone changes from
        // 440 to 880 Hz. Jump in both directions so this exercises actual seeks.
        foreach (double seconds in new[] { 1.1, 0.9, 3.1, 2.1, 1.1 })
        {
            int frame = (int)(seconds * video.VideoInfo.FrameRate.ToDouble());
            Assert.That(video.ReadVideo(frame, out var image), Is.True);
            using (image)
            {
                nint pixel = image!.Value.Data + image.Value.RowBytes * 32 + 32 * 4;
                int dominantChannel = seconds < 1 ? 2 : 0; // BGRA: red, then blue.
                Assert.That(Marshal.ReadByte(pixel, dominantChannel), Is.GreaterThan(240));
                Assert.That(Marshal.ReadByte(pixel, 2 - dominantChannel), Is.LessThan(10));
            }

            AssertTone(audio, seconds, 440 * (1 + (int)seconds));
        }
    }

    [TestCase("marker-bframes.mp4", MediaMode.Audio)]
    [TestCase("marker-bframes.mp4", MediaMode.AudioVideo)]
    [TestCase("marker-bframes-no-edit.mp4", MediaMode.Audio)]
    [TestCase("marker-bframes-no-edit.mp4", MediaMode.AudioVideo)]
    public void AudioDuration_ExcludesTheNormalizedVideoGap(string fixture, MediaMode mode)
    {
        using var reader = Open(fixture, mode);
        Assert.That(reader.AudioInfo.Duration.ToDouble(), Is.EqualTo(4).Within(1d / 48000));
    }

    [TestCase("marker-bframes.mp4", MediaMode.Audio)]
    [TestCase("marker-bframes.mp4", MediaMode.AudioVideo)]
    [TestCase("marker-bframes-no-edit.mp4", MediaMode.Audio)]
    [TestCase("marker-bframes-no-edit.mp4", MediaMode.AudioVideo)]
    public void ReadAudio_AtAndPastNormalizedEnd_ReturnsEmptyAndCanSeekBack(string fixture, MediaMode mode)
    {
        using var reader = Open(fixture, mode);
        foreach (int start in new[] { 48000 * 4, 48000 * 5, int.MaxValue })
        {
            Assert.That(reader.ReadAudio(start, 4096, out var pcm), Is.True);
            using (pcm)
            {
                Assert.That(pcm!.Value.NumSamples, Is.Zero);
                Assert.That(pcm.Value.SampleRate, Is.EqualTo(48000));
                Assert.That(pcm.Value.NumChannels, Is.EqualTo(2));
            }
        }

        AssertTone(reader, 1.1, 880);
    }

    [TestCase("marker-bframes.mp4", MediaMode.Audio)]
    [TestCase("marker-bframes.mp4", MediaMode.AudioVideo)]
    [TestCase("marker-bframes-no-edit.mp4", MediaMode.Audio)]
    [TestCase("marker-bframes-no-edit.mp4", MediaMode.AudioVideo)]
    public void ReadAudio_CrossingNormalizedEnd_DoesNotIncludeTheGapOrCodecPadding(string fixture, MediaMode mode)
    {
        using var reader = Open(fixture, mode);
        Assert.That(reader.ReadAudio(48000 * 4 - 480, 4800, out var pcm), Is.True);
        using (pcm)
        {
            Assert.That(pcm!.Value.NumSamples, Is.InRange(479, 480));
            Assert.That(EstimateFrequency((Pcm<Stereo32BitFloat>)pcm.Value), Is.EqualTo(1760).Within(150));
        }
        AssertTone(reader, 0.9, 440);
    }

    private static MediaReader Open(string fixture, MediaMode mode)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture);
        Assert.That(File.Exists(path), Is.True);
        var reader = new MFDecoderInfo(new MFDecodingExtension()).Open(path, new MediaOptions(mode));
        Assert.That(reader, Is.TypeOf<MFReader>(), $"MF must open {fixture} in {mode} mode");
        return reader!;
    }

    private static void AssertTone(MediaReader reader, double seconds, double expectedFrequency)
    {
        int rate = reader.AudioInfo.SampleRate;
        Assert.That(reader.ReadAudio((int)(seconds * rate), rate / 20, out var pcm), Is.True);
        using (pcm)
        {
            Assert.That(pcm!.Value.NumSamples, Is.EqualTo(rate / 20));
            Assert.That(EstimateFrequency((Pcm<Stereo32BitFloat>)pcm.Value),
                Is.EqualTo(expectedFrequency).Within(60), $"wrong audio content at {seconds}s");
        }
    }

    private static double EstimateFrequency(Pcm<Stereo32BitFloat> pcm)
    {
        int crossings = 0;
        var samples = pcm.DataSpan;
        for (int i = 1; i < samples.Length; i++)
        {
            if (samples[i - 1].Left <= 0 && samples[i].Left > 0)
                crossings++;
        }
        return crossings * pcm.SampleRate / (double)pcm.NumSamples;
    }
}
