using Beutl.FFmpegWorker.Decoding;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Source;
using FFmpeg.AutoGen.Abstractions;
using FFmpegSharp;

namespace Beutl.FFmpegWorker.Tests;

// Video frames are numbered at the average frame rate. A frame stays on screen from the number its timestamp rounds to
// until the next frame's, which is how ffmpeg's fps filter (round=near) presents variable frame rate media.
[TestFixture]
public sealed class FFmpegReaderVariableFrameRateTests
{
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        FFmpegTestHelpers.RequireFFmpeg();
    }

    public static IEnumerable<TestCaseData> ReadOrders()
    {
        foreach (string name in new[] { "vfr-30-60-30.mp4", "vfr-screen.mp4" })
        {
            foreach (string order in new[] { "sequential", "backward", "random", "stride7" })
                yield return new TestCaseData(name, order);
        }
    }

    // The reader counted decoded frames, which drift from the numbers, showed the next frame across a gap, and after a
    // seek skipped a B frame on screen when its packet duration, a decode order interval, ended before the target.
    [TestCaseSource(nameof(ReadOrders))]
    public void Every_read_shows_the_frame_on_screen(string name, string order)
    {
        string path = FixturePath(name);
        int[] expected = FramesOnScreen(path);
        var random = new Random(1234);
        IEnumerable<int> frames = Enumerable.Range(0, expected.Length);
        frames = order switch
        {
            "backward" => frames.Reverse(),
            "random" => frames.OrderBy(_ => random.Next()),
            "stride7" => frames.Where(f => f % 7 == 0),
            _ => frames,
        };
        using var reader = new FFmpegReader(path, new MediaOptions(MediaMode.Video), new FFmpegDecodingSettings());

        Assert.Multiple(() =>
        {
            foreach (int frame in frames)
                Assert.That(ReadIndex(reader, frame), Is.EqualTo(expected[frame]), $"frame {frame}");
        });
    }

    // The edit list hides 5 of the trimmed clip's 30 samples, and the screen recording's 181 frames span 161 numbers.
    [TestCase("edit-list-trim.mp4", 25)]
    [TestCase("vfr-screen.mp4", 161)]
    public void The_frame_count_ends_where_the_stream_ends(string name, int numFrames)
    {
        using var reader = new FFmpegReader(FixturePath(name), new MediaOptions(MediaMode.Video), new FFmpegDecodingSettings());

        Assert.Multiple(() =>
        {
            Assert.That(reader.VideoInfo.NumFrames, Is.EqualTo(numFrames));
            Assert.That(reader.ReadVideo(numFrames - 1, out Ref<Bitmap>? last), Is.True, "the last frame");
            last?.Dispose();
        });
    }

    private static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    // Decodes every frame in presentation order, then takes for each number up to where the stream ends the last frame
    // whose timestamp rounds to it or earlier. Numbers before the first frame show it.
    private static unsafe int[] FramesOnScreen(string path)
    {
        using var demuxer = MediaDemuxer.Open(path);
        MediaStream stream = demuxer.First(s => s.CodecparRef.codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO);
        double timeBase = ffmpeg.av_q2d(stream.TimeBase);
        double rate = ffmpeg.av_q2d(stream.AvgFrameRate);
        double start = demuxer.StartTime / (double)ffmpeg.AV_TIME_BASE;
        long Number(long pts) => (long)Math.Floor(((pts * timeBase) - start) * rate + 0.5);

        var presented = new List<(long Number, int Index)>();
        using (var decoder = MediaDecoder.CreateDecoder(stream.CodecparRef))
        using (var packet = new MediaPacket())
        using (var decoded = new MediaFrame())
        {
            foreach (MediaPacket read in demuxer.ReadPackets(packet))
            {
                if (read.StreamIndex != stream.Index)
                    continue;
                foreach (MediaFrame frame in decoder.DecodePacket(read, decoded))
                    presented.Add((Number(frame.Pts), LumaIndex(frame)));
            }

            foreach (MediaFrame frame in decoder.DecodePacket(null, decoded))
                presented.Add((Number(frame.Pts), LumaIndex(frame)));
        }

        var expected = new int[Number(stream.StartTime + stream.Duration)];
        for (int number = 0; number < expected.Length; number++)
        {
            long target = Math.Max(number, presented[0].Number);
            expected[number] = presented.Last(f => f.Number <= target).Index;
        }

        return expected;
    }

    private static int ReadIndex(FFmpegReader reader, int frame)
    {
        if (!reader.ReadVideo(frame, out Ref<Bitmap>? image))
            return -1;

        using (image)
        {
            Bitmap bitmap = image!.Value;
            Span<byte> row = bitmap.GetRow(bitmap.Height / 2);
            // The green of the BGRA gray, scaled back to the limited-range luma step.
            return Digit(row[(bitmap.Width / 4 * 4) + 1] * 219.0 / 255)
                + (20 * Digit(row[(bitmap.Width * 3 / 4 * 4) + 1] * 219.0 / 255));
        }
    }

    // The left half holds the index mod 20 and the right half the index / 20, at luma 16 + 10 * digit.
    private static unsafe int LumaIndex(MediaFrame frame)
    {
        byte* row = frame.Data[0] + ((long)frame.Linesize[0] * (frame.Height / 2));
        return Digit(row[frame.Width / 4] - 16) + (20 * Digit(row[frame.Width * 3 / 4] - 16));
    }

    private static int Digit(double luma) => (int)Math.Round(luma / 10);
}
