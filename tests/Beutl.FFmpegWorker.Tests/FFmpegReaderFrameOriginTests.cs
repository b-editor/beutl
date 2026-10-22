using Beutl.Audio;
using Beutl.FFmpegWorker.Decoding;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Source;

namespace Beutl.FFmpegWorker.Tests;

// Video frames count from the container start, and frames before the first video frame show it (#3250).
[TestFixture]
public sealed class FFmpegReaderFrameOriginTests
{
    private const string Red = "red";
    private const string Blue = "blue";

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        FFmpegTestHelpers.RequireFFmpeg();
    }

    // Each video turns blue 1 s after its first frame, which comes 0.5 s and 0.2 s after the container start.
    [TestCase("late-video.mp4", 15, 25)]
    [TestCase("marker-bframes-no-edit.mp4", 12, 42)]
    public void Frames_count_from_the_container_start(string name, int firstBlueFrame, int numFrames)
    {
        using var reader = new FFmpegReader(FixturePath(name), new MediaOptions(MediaMode.Video), new FFmpegDecodingSettings());

        Assert.Multiple(() =>
        {
            Assert.That(reader.VideoInfo.NumFrames, Is.EqualTo(numFrames));
            for (int frame = 0; frame < numFrames; frame++)
                Assert.That(ReadColor(reader, frame), Is.EqualTo(frame < firstBlueFrame ? Red : Blue), $"frame {frame}");
            Assert.That(reader.ReadVideo(numFrames, out _), Is.False);
        });
    }

    // The picture and the tone of late-video.mp4 both switch 1.5 s after the container start.
    [Test]
    public void A_late_video_switches_with_its_tone()
    {
        string path = FixturePath("late-video.mp4");
        using var video = new FFmpegReader(path, new MediaOptions(MediaMode.Video), new FFmpegDecodingSettings());
        using var audio = new FFmpegReader(path, new MediaOptions(MediaMode.Audio), new FFmpegDecodingSettings());

        int firstBlueFrame = Enumerable.Range(0, (int)video.VideoInfo.NumFrames).First(f => ReadColor(video, f) == Blue);
        double pictureSwitch = firstBlueFrame / video.VideoInfo.FrameRate.ToDouble();

        Assert.That(pictureSwitch, Is.EqualTo(FindToneSwitch(audio)).Within(0.05));
    }

    private static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static string ReadColor(FFmpegReader reader, int frame)
    {
        if (!reader.ReadVideo(frame, out Ref<Bitmap>? image))
            return "no frame";

        using (image)
        {
            Bitmap bitmap = image!.Value;
            Span<byte> pixel = bitmap.GetRow(bitmap.Height / 2).Slice(bitmap.Width / 2 * 4, 4);
            if (pixel[2] > 200 && pixel[0] < 60)
                return Red;
            return pixel[0] > 200 && pixel[2] < 60 ? Blue : $"B={pixel[0]} R={pixel[2]}";
        }
    }

    // Reads the audio in order from the start and returns where the 440 Hz tone turns into 880 Hz.
    private static double FindToneSwitch(FFmpegReader audio)
    {
        int rate = audio.AudioInfo.SampleRate;
        int window = rate / 50;
        for (int start = 0; start < rate * 5 / 2; start += window)
        {
            Assert.That(audio.ReadAudio(start, window, out AudioBuffer? pcm), Is.True);
            using (pcm)
            {
                if (Power(pcm!, 880) > 4 * Power(pcm!, 440))
                    return start / (double)rate;
            }
        }

        throw new AssertionException("The tone never turns into 880 Hz.");
    }

    // Goertzel power of one frequency in the first channel.
    private static double Power(AudioBuffer pcm, double frequency)
    {
        double coefficient = 2 * Math.Cos(2 * Math.PI * frequency / pcm.SampleRate);
        double s1 = 0;
        double s2 = 0;
        foreach (float sample in pcm.GetChannelData(0))
        {
            double s0 = sample + (coefficient * s1) - s2;
            s2 = s1;
            s1 = s0;
        }

        return (s1 * s1) + (s2 * s2) - (coefficient * s1 * s2);
    }
}
