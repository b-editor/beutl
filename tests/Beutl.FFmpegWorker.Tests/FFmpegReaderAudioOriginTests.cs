using Beutl.Audio;
using Beutl.FFmpegWorker.Decoding;
using Beutl.Media.Decoding;

namespace Beutl.FFmpegWorker.Tests;

// Audio samples count from the container start, whether a read follows the previous one or seeks (#3436, #3424).
[TestFixture]
public sealed class FFmpegReaderAudioOriginTests
{
    private const string Silent = "silent";
    private const string High = "880 Hz";

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        FFmpegTestHelpers.RequireFFmpeg();
    }

    // late-audio.mp4's audio starts 0.37 s after the container start, and its tone switches at 1.5 s.
    [TestCase(0.0)]
    [TestCase(1.0)]
    public void Audio_that_starts_late_keeps_its_place(double from)
    {
        using FFmpegReader reader = OpenAudio("late-audio.mp4", MediaMode.Audio);

        Dictionary<double, string> tones = ReadTones(reader, from, 1.8);

        Assert.Multiple(() =>
        {
            Assert.That(tones.Where(t => t.Key < 0.36).Select(t => t.Value), Is.All.EqualTo(Silent));
            Assert.That(FirstHigh(tones), Is.EqualTo(1.5).Within(0.03));
        });
    }

    // The first audio packet of marker-bframes-no-edit.mp4 lasts 0.2 s but holds 21 ms of samples.
    [TestCase(0.0)]
    [TestCase(1.0)]
    public void Audio_after_a_timestamp_gap_keeps_its_place(double from)
    {
        using FFmpegReader reader = OpenAudio("marker-bframes-no-edit.mp4", MediaMode.Audio);

        Assert.That(FirstHigh(ReadTones(reader, from, 1.5)), Is.EqualTo(1.2).Within(0.03));
    }

    // A reader that opens both streams decodes the first video frame first, which skips audio packets.
    [Test]
    public void Audio_read_after_the_first_video_frame_keeps_its_place()
    {
        using FFmpegReader audioOnly = OpenAudio("marker-bframes.mp4", MediaMode.Audio);
        using FFmpegReader both = OpenAudio("marker-bframes.mp4", MediaMode.AudioVideo);

        Assert.Multiple(() =>
        {
            Assert.That(FirstHigh(ReadTones(audioOnly, 0, 1.3)), Is.EqualTo(1.0).Within(0.03));
            Assert.That(FirstHigh(ReadTones(both, 0, 1.3)), Is.EqualTo(1.0).Within(0.03));
        });
    }

    // FFmpeg gives a WAV no start time, and its timestamps start at its first sample.
    [TestCase(0.0)]
    [TestCase(0.9)]
    public void A_wav_counts_from_its_first_sample(double from)
    {
        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"tone-{Guid.NewGuid():N}.wav");
        WriteToneWav(path);
        try
        {
            using var reader = new FFmpegReader(path, new MediaOptions(MediaMode.Audio), new FFmpegDecodingSettings());

            Assert.That(FirstHigh(ReadTones(reader, from, 1.3)), Is.EqualTo(1.0).Within(0.03));
        }
        finally
        {
            File.Delete(path);
        }
    }

    // A seek stops on the frame that ends at the target, and the read has to go on to the next frame.
    [Test]
    public void A_read_that_starts_where_a_frame_ends_returns_its_audio()
    {
        using FFmpegReader reader = OpenAudio("marker-bframes.mp4", MediaMode.Audio);
        Assert.That(reader.ReadAudio(96000, 960, out AudioBuffer? far), Is.True);
        far!.Dispose();

        Assert.That(reader.ReadAudio(4096, 960, out AudioBuffer? pcm), Is.True);
        using (pcm)
        {
            Assert.That(Classify(pcm!), Is.Not.EqualTo(Silent));
        }
    }

    private static FFmpegReader OpenAudio(string name, MediaMode mode)
        => new(Path.Combine(AppContext.BaseDirectory, "Fixtures", name), new MediaOptions(mode), new FFmpegDecodingSettings());

    // Reads 20 ms windows in order from one time to another and names the tone of each.
    private static Dictionary<double, string> ReadTones(FFmpegReader reader, double from, double to)
    {
        int rate = reader.AudioInfo.SampleRate;
        int window = rate / 50;
        var tones = new Dictionary<double, string>();
        for (int start = (int)(from * rate); start + window <= to * rate; start += window)
        {
            Assert.That(reader.ReadAudio(start, window, out AudioBuffer? pcm), Is.True);
            using (pcm)
            {
                tones[start / (double)rate] = Classify(pcm!);
            }
        }

        return tones;
    }

    // 1.5 s of 8 kHz mono 16-bit PCM whose tone switches from 440 to 880 Hz at 1 s.
    private static void WriteToneWav(string path)
    {
        const int rate = 8000;
        short[] samples = Enumerable.Range(0, rate * 3 / 2)
            .Select(i => (short)(9000 * Math.Sin(2 * Math.PI * (i < rate ? 440 : 880) * i / rate)))
            .ToArray();

        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8);
        writer.Write(36 + (samples.Length * 2));
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(rate);
        writer.Write(rate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(samples.Length * 2);
        foreach (short sample in samples)
            writer.Write(sample);
    }

    private static double FirstHigh(Dictionary<double, string> tones)
        => tones.Where(t => t.Value == High).Min(t => t.Key);

    private static string Classify(AudioBuffer pcm)
    {
        if (!pcm.GetChannelData(0).ContainsAnyExcept(0f))
            return Silent;

        double low = Power(pcm, 440);
        double high = Power(pcm, 880);
        if (high > 4 * low)
            return High;
        return low > 4 * high ? "440 Hz" : "mixed";
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
