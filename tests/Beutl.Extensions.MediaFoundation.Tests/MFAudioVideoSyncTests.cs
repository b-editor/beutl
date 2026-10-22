using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Beutl.Audio;
using Beutl.Embedding.MediaFoundation.Decoding;
using Beutl.Media.Decoding;

namespace Beutl.Extensions.MediaFoundation.Tests;

[TestFixture]
[Platform("Win")]
[SupportedOSPlatform("windows")]
public class MFAudioVideoSyncTests
{
    // Both streams count from the container start. Without an edit list, the B-frame delay
    // starts the video of marker-bframes-no-edit.mp4 and its audio packets 0.2 s late.
    [TestCase("marker-bframes.mp4", MediaMode.Audio, 0.0)]
    [TestCase("marker-bframes.mp4", MediaMode.AudioVideo, 0.0)]
    [TestCase("marker-bframes-no-edit.mp4", MediaMode.Audio, 0.2)]
    [TestCase("marker-bframes-no-edit.mp4", MediaMode.AudioVideo, 0.2)]
    public void ReadAudio_SeeksStayAlignedWithVideo(string fixture, MediaMode mode, double delay)
    {
        using var audio = Open(fixture, mode);
        using var video = Open(fixture, MediaMode.Video);

        // The picture changes from red to blue 1 s into the content, when the tone changes
        // from 440 to 880 Hz. Jump in both directions so this exercises actual seeks.
        foreach (double seconds in new[] { 1.1, 0.9, 3.1, 2.1, 1.1 })
        {
            double content = seconds - delay;
            int frame = (int)(seconds * video.VideoInfo.FrameRate.ToDouble());
            Assert.That(video.ReadVideo(frame, out var image), Is.True);
            using (image)
            {
                nint pixel = image!.Value.Data + image.Value.RowBytes * 32 + 32 * 4;
                int dominantChannel = content < 1 ? 2 : 0; // BGRA: red, then blue.
                Assert.That(Marshal.ReadByte(pixel, dominantChannel), Is.GreaterThan(240));
                Assert.That(Marshal.ReadByte(pixel, 2 - dominantChannel), Is.LessThan(10));
            }

            AssertTone(audio, seconds, 440 * (1 + (int)content));
        }
    }

    // The first frame of marker-bframes-no-edit.mp4 is shown from 0.2 s; earlier frames show it.
    [Test]
    public void ReadVideo_BeforeTheFirstVideoFrame_ShowsIt()
    {
        using var video = Open("marker-bframes-no-edit.mp4", MediaMode.Video);
        Assert.That(video.VideoInfo.NumFrames, Is.EqualTo(42));
        foreach (int frame in new[] { 0, 1, 2, 11 })
        {
            Assert.That(video.ReadVideo(frame, out var image), Is.True);
            using (image)
            {
                nint pixel = image!.Value.Data + image.Value.RowBytes * 32 + 32 * 4;
                Assert.That(Marshal.ReadByte(pixel, 2), Is.GreaterThan(240), $"frame {frame} is red");
            }
        }

        Assert.That(video.ReadVideo(12, out var blue), Is.True);
        using (blue)
        {
            nint pixel = blue!.Value.Data + blue.Value.RowBytes * 32 + 32 * 4;
            Assert.That(Marshal.ReadByte(pixel, 0), Is.GreaterThan(240), "frame 12 is blue");
        }
    }

    [TestCase("marker-bframes.mp4", MediaMode.Audio, 0.0)]
    [TestCase("marker-bframes.mp4", MediaMode.AudioVideo, 0.0)]
    [TestCase("marker-bframes-no-edit.mp4", MediaMode.Audio, 0.2)]
    [TestCase("marker-bframes-no-edit.mp4", MediaMode.AudioVideo, 0.2)]
    public void AudioDuration_CountsFromTheContainerStart(string fixture, MediaMode mode, double delay)
    {
        using var reader = Open(fixture, mode);
        Assert.That(reader.AudioInfo.Duration.ToDouble(), Is.EqualTo(4 + delay).Within(1d / 48000));
    }

    [TestCase("marker-bframes.mp4", MediaMode.Audio, 0.0)]
    [TestCase("marker-bframes.mp4", MediaMode.AudioVideo, 0.0)]
    [TestCase("marker-bframes-no-edit.mp4", MediaMode.Audio, 0.2)]
    [TestCase("marker-bframes-no-edit.mp4", MediaMode.AudioVideo, 0.2)]
    public void ReadAudio_AtAndPastTheEnd_ReturnsEmptyAndCanSeekBack(string fixture, MediaMode mode, double delay)
    {
        using var reader = Open(fixture, mode);
        foreach (int start in new[] { (int)Math.Round(48000 * (4 + delay)), 48000 * 5, int.MaxValue })
        {
            Assert.That(reader.ReadAudio(start, 4096, out var pcm), Is.True);
            using (pcm)
            {
                Assert.That(pcm!.SampleCount, Is.Zero);
                Assert.That(pcm.SampleRate, Is.EqualTo(48000));
                Assert.That(pcm.ChannelCount, Is.EqualTo(2));
            }
        }

        AssertTone(reader, 1.1 + delay, 880);
    }

    [TestCase("marker-bframes.mp4", MediaMode.Audio, 0.0)]
    [TestCase("marker-bframes.mp4", MediaMode.AudioVideo, 0.0)]
    [TestCase("marker-bframes-no-edit.mp4", MediaMode.Audio, 0.2)]
    [TestCase("marker-bframes-no-edit.mp4", MediaMode.AudioVideo, 0.2)]
    public void ReadAudio_CrossingTheEnd_DoesNotIncludeCodecPadding(string fixture, MediaMode mode, double delay)
    {
        using var reader = Open(fixture, mode);
        Assert.That(reader.ReadAudio((int)Math.Round(48000 * (4 + delay)) - 480, 4800, out var pcm), Is.True);
        using (pcm)
        {
            Assert.That(pcm!.SampleCount, Is.InRange(479, 480));
            Assert.That(EstimateFrequency(pcm), Is.EqualTo(1760).Within(150));
        }
        AssertTone(reader, 0.9 + delay, 440);
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
            Assert.That(pcm!.SampleCount, Is.EqualTo(rate / 20));
            Assert.That(EstimateFrequency(pcm),
                Is.EqualTo(expectedFrequency).Within(60), $"wrong audio content at {seconds}s");
        }
    }

    private static double EstimateFrequency(AudioBuffer pcm)
    {
        int crossings = 0;
        var samples = pcm.GetChannelData(0);
        for (int i = 1; i < samples.Length; i++)
        {
            if (samples[i - 1] <= 0 && samples[i] > 0)
                crossings++;
        }
        return crossings * pcm.SampleRate / (double)pcm.SampleCount;
    }
}
