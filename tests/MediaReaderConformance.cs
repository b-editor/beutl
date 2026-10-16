using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Beutl.Media.Source;

namespace Beutl;

// Checks one decoder against the MediaReader contract. The FFmpegIpc, MediaFoundation and AVFoundation test
// projects link this file and derive one fixture each.
public abstract class MediaReaderConformanceTests
{
    // 4 s of 64x64 H.264 (10 fps, B-frames) and 48 kHz stereo AAC: red turning blue at 1 s, with a tone of 440,
    // 880, 1320 and 1760 Hz in successive seconds. Fixtures/README.md in the MediaFoundation tests says how.
    private const string MarkerFixture = "marker-bframes.mp4";
    private const int FrameRate = 10;
    private const int SampleRate = 48000;
    private const int DurationSeconds = 4;

    // Opens the file with the decoder under test.
    protected abstract MediaReader? Open(string path, MediaOptions options);

    // Contract points the decoder does not meet yet, by test name with the tracking issue; those tests skip.
    protected virtual IReadOnlyDictionary<string, string> KnownDeviations { get; } = new Dictionary<string, string>();

    [SetUp]
    public void SkipKnownDeviation()
    {
        if (KnownDeviations.TryGetValue(TestContext.CurrentContext.Test.MethodName ?? "", out string? issue))
            Assert.Ignore($"Known deviation from the MediaReader contract: {issue}");
    }

    [Test]
    public void Open_BothStreams_ReportsTheStreamsOfTheFile()
    {
        using MediaReader reader = OpenMarker(MediaMode.AudioVideo);
        Assert.Multiple(() =>
        {
            Assert.That(reader.HasVideo, Is.True);
            Assert.That(reader.HasAudio, Is.True);
            Assert.That(reader.VideoInfo.FrameSize, Is.EqualTo(new PixelSize(64, 64)));
            Assert.That(reader.VideoInfo.FrameRate, Is.EqualTo(new Rational(FrameRate)));
            Assert.That(reader.AudioInfo.SampleRate, Is.EqualTo(SampleRate));
            Assert.That(reader.AudioInfo.NumChannels, Is.EqualTo(2));
        });
    }

    [TestCase(MediaMode.Video)]
    [TestCase(MediaMode.Audio)]
    public void Open_OneStream_LoadsOnlyThatStream(MediaMode mode)
    {
        using MediaReader reader = OpenMarker(mode);
        Assert.Multiple(() =>
        {
            Assert.That(reader.HasVideo, Is.EqualTo(mode == MediaMode.Video));
            Assert.That(reader.HasAudio, Is.EqualTo(mode == MediaMode.Audio));
        });
    }

    // The reads jump both ways, so each one after the first is a seek.
    [Test]
    public void ReadVideo_ReturnsTheFrameThatCoversItsTime()
    {
        using MediaReader reader = OpenMarker(MediaMode.Video);
        foreach (int frame in new[] { 11, 9, 10, 31, 21, 11, 0 })
        {
            AssertColor(reader, frame, red: frame < FrameRate);
        }
    }

    // Sample 0 is the same instant as frame 0, so the tone changes when the picture does.
    [Test]
    public void ReadAudio_CountsFromTheSameStartAsVideo()
    {
        using MediaReader reader = OpenMarker(MediaMode.Audio);
        foreach (double seconds in new[] { 1.1, 0.9, 3.1, 2.1, 1.1, 0.1 })
        {
            AssertTone(reader, seconds, 440 * (1 + (int)seconds));
        }
    }

    [TestCase(FrameRate * DurationSeconds)]
    [TestCase(FrameRate * (DurationSeconds + 1))]
    public void ReadVideo_PastTheLastFrame_ReturnsFalse(int frame)
    {
        using MediaReader reader = OpenMarker(MediaMode.Video);
        Assert.Multiple(() =>
        {
            Assert.That(reader.ReadVideo(frame, out Ref<Bitmap>? image), Is.False);
            Assert.That(image, Is.Null);
        });
    }

    // A read does not depend on earlier ones, even after a seek past the end.
    [Test]
    public void ReadVideo_AfterAReadPastTheEnd_ReadsEarlierFrames()
    {
        using MediaReader reader = OpenMarker(MediaMode.Video);
        AssertColor(reader, 0, red: true);
        Assert.That(reader.ReadVideo(FrameRate * (DurationSeconds + 4), out Ref<Bitmap>? image), Is.False);
        image?.Dispose();

        foreach (int frame in new[] { 5, 6, 15 })
        {
            AssertColor(reader, frame, red: frame < FrameRate);
        }
    }

    // A second past the end, beyond any codec padding: FFmpeg still returns the padding of the last AAC frame at 4 s.
    [TestCase(SampleRate * (DurationSeconds + 1))]
    [TestCase(int.MaxValue)]
    public void ReadAudio_PastTheEnd_ReturnsNoSamples(int start)
    {
        using MediaReader reader = OpenMarker(MediaMode.Audio);
        Assert.That(reader.ReadAudio(start, 4096, out Ref<IPcm>? sound), Is.True);
        using (sound)
        {
            Assert.That(sound!.Value.NumSamples, Is.Zero);
        }

        AssertTone(reader, 1.1, 880);
    }

    [Test]
    public void ReadAudio_CrossingTheEnd_IsShort()
    {
        using MediaReader reader = OpenMarker(MediaMode.Audio);
        Assert.That(reader.ReadAudio(SampleRate * DurationSeconds - 480, 4800, out Ref<IPcm>? sound), Is.True);
        using (sound)
        {
            Assert.That(sound!.Value.NumSamples, Is.InRange(1, 4799));
        }
    }

    [Test]
    public void Reads_AfterDispose_ReturnFalse()
    {
        MediaReader reader = OpenMarker(MediaMode.AudioVideo);
        reader.Dispose();
        Assert.Multiple(() =>
        {
            Assert.That(reader.ReadVideo(0, out Ref<Bitmap>? image), Is.False);
            Assert.That(image, Is.Null);
            Assert.That(reader.ReadAudio(0, 4096, out Ref<IPcm>? sound), Is.False);
            Assert.That(sound, Is.Null);
        });
    }

    private MediaReader OpenMarker(MediaMode mode)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", MarkerFixture);
        Assert.That(File.Exists(path), Is.True, $"{MarkerFixture} is missing from the test output");
        MediaReader? reader = Open(path, new MediaOptions(mode));
        Assert.That(reader, Is.Not.Null, $"the backend must open {MarkerFixture} for {mode}");
        return reader!;
    }

    private static void AssertColor(MediaReader reader, int frame, bool red)
    {
        Assert.That(reader.ReadVideo(frame, out Ref<Bitmap>? image), Is.True, $"frame {frame} must decode");
        using (image)
        using (Bitmap bgra = image!.Value.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Unpremul))
        {
            Span<byte> pixel = bgra.GetRow(32).Slice(32 * 4, 4);
            (byte strong, byte weak) = red ? (pixel[2], pixel[0]) : (pixel[0], pixel[2]);
            Assert.That(strong, Is.GreaterThan(240), $"frame {frame} must be {(red ? "red" : "blue")}");
            Assert.That(weak, Is.LessThan(10), $"frame {frame} must be {(red ? "red" : "blue")}");
        }
    }

    // Counts rising zero crossings in 50 ms of the left channel.
    private static void AssertTone(MediaReader reader, double seconds, double expectedFrequency)
    {
        int length = SampleRate / 20;
        Assert.That(reader.ReadAudio((int)(seconds * SampleRate), length, out Ref<IPcm>? sound), Is.True);
        using (sound)
        {
            var pcm = (Pcm<Stereo32BitFloat>)sound!.Value;
            Assert.That(pcm.NumSamples, Is.EqualTo(length));
            Assert.That(pcm.SampleRate, Is.EqualTo(SampleRate));

            int crossings = 0;
            Span<Stereo32BitFloat> samples = pcm.DataSpan;
            for (int i = 1; i < samples.Length; i++)
            {
                if (samples[i - 1].Left <= 0 && samples[i].Left > 0)
                    crossings++;
            }

            Assert.That(crossings * (double)SampleRate / length, Is.EqualTo(expectedFrequency).Within(60),
                $"wrong tone at {seconds} s");
        }
    }
}
