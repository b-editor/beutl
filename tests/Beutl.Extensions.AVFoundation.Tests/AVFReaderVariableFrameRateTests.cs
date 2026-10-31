using Beutl.Extensions.AVFoundation.Decoding;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Source;

namespace Beutl.Extensions.AVFoundation.Tests;

// Video frames are numbered at the frame rate the reader reports. A frame stays on screen from the number its
// timestamp rounds to until the next frame's, which is how ffmpeg's fps filter (round=near) presents variable frame
// rate media at that rate.
[TestFixture]
[Platform("MacOSX")]
public class AVFReaderVariableFrameRateTests
{
    // Frame counts, the time the last frame ends and presentation times, in 1/600 s, from the setpts expressions in the
    // FFmpeg worker tests' Fixtures/README.md.
    private static readonly Dictionary<string, (int Count, int End, Func<int, int> Pts)> s_fixtures = new()
    {
        ["vfr-30-60-30.mp4"] = (300, 5100, n =>
            n < 105 ? n * 20
            : n < 195 ? 2100 + (n - 105) * 10
            : 3000 + (n - 195) * 20),
        ["vfr-screen.mp4"] = (181, 4800, n =>
            n < 1 ? 0
            : n < 41 ? 600 + (n - 1) * 10
            : n < 71 ? 1900 + (n - 41) * 20
            : n < 131 ? 3700 + (n - 71) * 5
            : 4300 + (n - 131) * 10),
    };

    public static IEnumerable<TestCaseData> ReadOrders()
    {
        foreach (string name in s_fixtures.Keys)
        {
            foreach (string order in new[] { "sequential", "backward", "random", "stride7" })
                yield return new TestCaseData(name, order);
        }
    }

    // The reader counted decoded samples, which drift from the numbers, and numbered the first sample after a seek by
    // the whole second the seek started at instead of the sample's own time.
    [TestCaseSource(nameof(ReadOrders))]
    public void ReadVideo_VariableFrameRate_ShowsTheFrameOnScreen(string name, string order)
    {
        using var reader = new AVFReader(FixturePath(name), new MediaOptions(MediaMode.Video), new AVFDecodingExtension());
        int[] expected = FramesOnScreen(s_fixtures[name], reader.VideoInfo.FrameRate.ToDouble());
        var random = new Random(1234);
        IEnumerable<int> frames = Enumerable.Range(0, expected.Length);
        frames = order switch
        {
            "backward" => frames.Reverse(),
            "random" => frames.OrderBy(_ => random.Next()),
            "stride7" => frames.Where(f => f % 7 == 0),
            _ => frames,
        };

        Assert.Multiple(() =>
        {
            Assert.That(reader.VideoInfo.NumFrames, Is.EqualTo(expected.Length), "the frame count");
            foreach (int frame in frames)
                Assert.That(ReadIndex(reader, frame), Is.EqualTo(expected[frame]), $"frame {frame}");

            // The last frame stays on screen until the track ends, and reads past that fail.
            Assert.That(ReadIndex(reader, expected.Length - 1), Is.EqualTo(expected[^1]), "the last frame");
            Assert.That(ReadIndex(reader, expected.Length), Is.EqualTo(-1), "past the end");
        });
    }

    private static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    // Takes for each number up to where the track ends the last frame whose timestamp rounds to it or earlier at the
    // reported rate. Numbers before the first frame show it.
    private static int[] FramesOnScreen((int Count, int End, Func<int, int> Pts) fixture, double rate)
    {
        long Number(int time) => (long)Math.Round(time / 600.0 * rate, MidpointRounding.AwayFromZero);

        var expected = new int[Number(fixture.End)];
        for (int number = 0; number < expected.Length; number++)
        {
            long target = Math.Max(number, Number(fixture.Pts(0)));
            expected[number] = Enumerable.Range(0, fixture.Count).Last(index => Number(fixture.Pts(index)) <= target);
        }

        return expected;
    }

    private static int ReadIndex(AVFReader reader, int frame)
    {
        if (!reader.ReadVideo(frame, out Ref<Bitmap>? image))
            return -1;

        using (image)
        {
            Bitmap bitmap = image!.Value;
            Span<byte> row = bitmap.GetRow(bitmap.Height / 2);
            // The green of the BGRA gray, scaled back to the limited-range luma step. The left half holds the index
            // mod 20 and the right half the index / 20, at luma 16 + 10 * digit.
            return Digit(row[(bitmap.Width / 4 * 4) + 1] * 219.0 / 255)
                + (20 * Digit(row[(bitmap.Width * 3 / 4 * 4) + 1] * 219.0 / 255));
        }
    }

    private static int Digit(double luma) => (int)Math.Round(luma / 10);
}
