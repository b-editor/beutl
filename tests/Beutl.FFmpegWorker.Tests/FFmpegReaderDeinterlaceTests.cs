using System.Reflection;
using Beutl.FFmpegWorker.Decoding;
using Beutl.Media;
using Beutl.Media.Decoding;

namespace Beutl.FFmpegWorker.Tests;

[TestFixture]
public sealed class FFmpegReaderDeinterlaceTests
{
    [OneTimeSetUp]
    public void SetUp() => FFmpegTestHelpers.RequireFFmpeg();

    [TestCase("tff", DeinterlaceMethod.Bwdif)]
    [TestCase("tff", DeinterlaceMethod.Yadif)]
    [TestCase("bff", DeinterlaceMethod.Bwdif)]
    [TestCase("bff", DeinterlaceMethod.Yadif)]
    public void AutoReducesCombingOnInterlacedVideo(string parity, DeinterlaceMethod method)
    {
        using var original = Open(parity, default);
        using var filtered = Open(parity, new(DeinterlaceMode.Auto, DeinterlaceFieldOrder.Auto, method, DeinterlaceFrameRateMode.Frame));
        int combed = CombEnergy(Read(original, 5), original.VideoInfo.FrameSize);
        int smooth = CombEnergy(Read(filtered, 5), filtered.VideoInfo.FrameSize);
        Assert.Multiple(() =>
        {
            Assert.That(original.VideoInfo.DeinterlaceStatus, Is.EqualTo(DeinterlaceApplicationStatus.Off));
            Assert.That(filtered.VideoInfo.DeinterlaceStatus, Is.EqualTo(DeinterlaceApplicationStatus.Applied));
            Assert.That(filtered.VideoInfo.DetectedFieldOrder,
                Is.EqualTo(parity == "tff" ? VideoFieldOrder.TopFieldFirst : VideoFieldOrder.BottomFieldFirst));
            Assert.That(filtered.VideoInfo.FrameRate, Is.EqualTo(original.VideoInfo.FrameRate));
            Assert.That(filtered.VideoInfo.NumFrames, Is.EqualTo(original.VideoInfo.NumFrames));
            Assert.That(combed, Is.GreaterThan(100_000), "the moving bar must have visible combing");
            Assert.That(smooth, Is.LessThan(combed / 3), "the filter should remove most comb energy");
        });
    }

    [TestCase("tff", DeinterlaceFieldOrder.TopFieldFirst, DeinterlaceFieldOrder.BottomFieldFirst)]
    [TestCase("bff", DeinterlaceFieldOrder.BottomFieldFirst, DeinterlaceFieldOrder.TopFieldFirst)]
    public void FieldRatePreservesDurationAndUsesTheSelectedParity(
        string parity, DeinterlaceFieldOrder fieldOrder, DeinterlaceFieldOrder wrongOrder)
    {
        var options = new VideoDeinterlaceOptions(DeinterlaceMode.On, fieldOrder, DeinterlaceMethod.Bwdif, DeinterlaceFrameRateMode.Field);
        using var original = Open(parity, default);
        using var forced = Open(parity, options);
        using var automaticParity = Open(parity, options with { FieldOrder = DeinterlaceFieldOrder.Auto });
        using var reversed = Open(parity, options with { FieldOrder = wrongOrder });

        Assert.Multiple(() =>
        {
            Assert.That(forced.VideoInfo.FrameRate, Is.EqualTo(new Rational(60000, 1001)));
            Assert.That(forced.VideoInfo.NumFrames, Is.EqualTo(original.VideoInfo.NumFrames * 2));
            Assert.That(forced.VideoInfo.Duration, Is.EqualTo(original.VideoInfo.Duration));
        });
        for (int frame = 8; frame < 20; frame += 2)
        {
            var first = Read(forced, frame);
            var second = Read(forced, frame + 1);
            PixelSize size = forced.VideoInfo.FrameSize;
            Assert.That(BarPosition(first, size), Is.LessThan(BarPosition(second, size) - 2));
            Assert.That(BarPosition(Read(reversed, frame), size), Is.GreaterThan(BarPosition(Read(reversed, frame + 1), size) + 2));
            Assert.That(Read(automaticParity, frame), Is.EqualTo(first));
            Assert.That(Read(automaticParity, frame + 1), Is.EqualTo(second));
        }
    }

    [TestCase("tff", DeinterlaceMethod.Bwdif, DeinterlaceFrameRateMode.Field)]
    [TestCase("bff", DeinterlaceMethod.Yadif, DeinterlaceFrameRateMode.Field)]
    [TestCase("tff", DeinterlaceMethod.Bwdif, DeinterlaceFrameRateMode.Frame)]
    public void SeeksAndEofDrainMatchSequentialReads(string parity, DeinterlaceMethod method, DeinterlaceFrameRateMode rate)
    {
        var options = new VideoDeinterlaceOptions(DeinterlaceMode.Auto, DeinterlaceFieldOrder.Auto, method, rate);
        using var sequential = Open(parity, options);
        int count = checked((int)sequential.VideoInfo.NumFrames);
        var expected = Enumerable.Range(0, count).Select(frame => Read(sequential, frame)).ToArray();
        using var seeking = Open(parity, options);
        int[] targets = [count - 1, count - 2, 0, 1, count / 2, count / 2 + 1, 35, 36, 37, count - 1, 0];
        foreach (int frame in targets)
        {
            Assert.That(Read(seeking, frame).AsSpan().SequenceEqual(expected[frame]), Is.True, $"frame {frame}");
        }
        Assert.That(seeking.ReadVideo(count, out _), Is.False, "EOF");
        Assert.That(expected[^2].AsSpan().SequenceEqual(expected[^1]), Is.False, "the final output must not just repeat the previous frame");
    }

    [TestCase(DeinterlaceFrameRateMode.Frame, 1)]
    [TestCase(DeinterlaceFrameRateMode.Field, 2)]
    public void AutoKeepsProgressivePixelsAndUsesTheSelectedRate(DeinterlaceFrameRateMode rate, int samplesPerFrame)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "marker-bframes-no-edit.mp4");
        using var original = new FFmpegReader(path, new MediaOptions(MediaMode.Video), new FFmpegDecodingSettings());
        using var automatic = new FFmpegReader(path, new MediaOptions(MediaMode.Video)
        {
            Deinterlace = new(DeinterlaceMode.Auto, DeinterlaceFieldOrder.Auto, DeinterlaceMethod.Bwdif, rate),
        }, new FFmpegDecodingSettings());

        Assert.Multiple(() =>
        {
            Assert.That(automatic.VideoInfo.DetectedFieldOrder, Is.EqualTo(VideoFieldOrder.Progressive));
            Assert.That(automatic.VideoInfo.DeinterlaceStatus, Is.EqualTo(DeinterlaceApplicationStatus.Applied));
            Assert.That(automatic.VideoInfo.FrameRate, Is.EqualTo(original.VideoInfo.FrameRate * samplesPerFrame));
            Assert.That(automatic.VideoInfo.NumFrames, Is.EqualTo(original.VideoInfo.NumFrames * samplesPerFrame));
            Assert.That(automatic.VideoInfo.Duration, Is.EqualTo(original.VideoInfo.Duration));
            Assert.That(Read(automatic, 0), Is.EqualTo(Read(original, 0)));
        });

        var progressive = Read(original, 3);
        for (int i = 0; i < samplesPerFrame; i++)
            Assert.That(Read(automatic, 3 * samplesPerFrame + i), Is.EqualTo(progressive));
    }

    [TestCase(DeinterlaceFrameRateMode.Frame, 1)]
    [TestCase(DeinterlaceFrameRateMode.Field, 2)]
    public void AutoFollowsFrameFlagsInAnInterlacedContainer(DeinterlaceFrameRateMode rate, int samplesPerFrame)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "deinterlace-mixed.mkv");
        var options = new VideoDeinterlaceOptions(DeinterlaceMode.Auto, DeinterlaceFieldOrder.Auto,
            DeinterlaceMethod.Bwdif, rate);
        var settings = new FFmpegDecodingSettings();
        using var original = new FFmpegReader(path, new MediaOptions(MediaMode.Video), settings);
        using var mixed = new FFmpegReader(path, new MediaOptions(MediaMode.Video) { Deinterlace = options }, settings);

        Assert.That(mixed.VideoInfo.DeinterlaceStatus, Is.EqualTo(DeinterlaceApplicationStatus.Applied));
        Assert.That(mixed.VideoInfo.FrameRate, Is.EqualTo(new Rational(25 * samplesPerFrame, 1)));
        Assert.That(mixed.VideoInfo.Duration, Is.EqualTo(original.VideoInfo.Duration));
        int frameCount = 16 * samplesPerFrame;
        Assert.That(mixed.VideoInfo.NumFrames, Is.EqualTo(frameCount));

        var progressive = Read(original, 5);
        for (int i = 0; i < samplesPerFrame; i++)
            Assert.That(Read(mixed, 5 * samplesPerFrame + i), Is.EqualTo(progressive));

        Assert.That(CombEnergy(Read(mixed, 8 * samplesPerFrame), mixed.VideoInfo.FrameSize),
            Is.LessThan(CombEnergy(Read(original, 8), original.VideoInfo.FrameSize) / 3));
        var progressiveTail = Read(original, 13);
        for (int i = 0; i < samplesPerFrame; i++)
            Assert.That(Read(mixed, 13 * samplesPerFrame + i), Is.EqualTo(progressiveTail));
        Assert.That(Read(mixed, frameCount - 1), Is.Not.Empty);
        Assert.That(mixed.ReadVideo(frameCount, out _), Is.False);
        Assert.That(Read(mixed, 5 * samplesPerFrame), Is.EqualTo(progressive), "seeking back keeps the progressive frame intact");
    }

    [Test]
    public void AFailedInitialLookaheadCanBeRetried()
    {
        var options = new VideoDeinterlaceOptions(DeinterlaceMode.On, DeinterlaceFieldOrder.Auto,
            DeinterlaceMethod.Bwdif, DeinterlaceFrameRateMode.Field);
        using var expected = Open("tff", options);
        using var reader = Open("tff", options);

        // Force the first filter feed to throw so the next request must rebuild its lookahead.
        var field = typeof(FFmpegReader).GetField("_bufferSrcCtx", BindingFlags.Instance | BindingFlags.NonPublic)!;
        object? input = field.GetValue(reader);
        Assert.That(input, Is.Not.Null);
        field.SetValue(reader, null);
        try
        {
            Assert.Throws<NullReferenceException>(() => reader.ReadVideo(0, out _));
        }
        finally
        {
            field.SetValue(reader, input);
        }

        Assert.That(Read(reader, 0), Is.EqualTo(Read(expected, 0)));
        Assert.That(Read(reader, 1), Is.EqualTo(Read(expected, 1)));
    }

    [Test]
    public void AudioSeeksDoNotInterruptTemporalVideoDecoding()
    {
        var options = new VideoDeinterlaceOptions(DeinterlaceMode.On, DeinterlaceFieldOrder.Auto,
            DeinterlaceMethod.Bwdif, DeinterlaceFrameRateMode.Field);
        using var video = Open("tff", options);
        using var audioVideo = Open("tff", options, MediaMode.AudioVideo);
        using var audio = Open("tff", default, MediaMode.Audio);
        Assert.That(audioVideo.AudioInfo.Duration, Is.EqualTo(audioVideo.VideoInfo.Duration));

        for (int frame = 0; frame < 8; frame++)
        {
            int start = frame % 2 == 0 ? 48_000 : 130_000;
            var samples = new byte[512 * sizeof(float)];
            var expected = new byte[samples.Length];
            Assert.That(audio.ReadAudio(start, 512, expected, out var expectedInfo), Is.True);
            Assert.That(audioVideo.ReadAudio(start, 512, samples, out var actualInfo), Is.True);
            Assert.That(actualInfo.NumSamples, Is.EqualTo(expectedInfo.NumSamples));
            Assert.That(samples, Is.EqualTo(expected));
            Assert.That(Read(audioVideo, frame), Is.EqualTo(Read(video, frame)));
        }
    }

    private static FFmpegReader Open(string parity, VideoDeinterlaceOptions options, MediaMode mode = MediaMode.Video)
        => new(Path.Combine(AppContext.BaseDirectory, "Fixtures", $"deinterlace-{parity}.mp4"),
            new MediaOptions(mode) { Deinterlace = options }, new FFmpegDecodingSettings());

    private static byte[] Read(FFmpegReader reader, int frame)
    {
        Assert.That(reader.ReadVideo(frame, out var image), Is.True, $"read frame {frame}");
        using (image)
            return image!.Value.GetPixelSpan().ToArray();
    }

    private static int CombEnergy(byte[] pixels, PixelSize size)
    {
        int sum = 0;
        int stride = size.Width * 4;
        for (int y = 1; y < size.Height; y++)
        {
            for (int x = 0; x < stride; x += 4)
                sum += Math.Abs(pixels[y * stride + x] - pixels[(y - 1) * stride + x]);
        }
        return sum;
    }

    private static double BarPosition(byte[] pixels, PixelSize size)
    {
        long total = 0;
        long weighted = 0;
        for (int y = 0; y < size.Height; y++)
        {
            for (int x = 0; x < size.Width; x++)
            {
                int brightness = pixels[(y * size.Width + x) * 4];
                if (brightness < 100) continue;
                total += brightness;
                weighted += brightness * x;
            }
        }
        return (double)weighted / total;
    }
}
