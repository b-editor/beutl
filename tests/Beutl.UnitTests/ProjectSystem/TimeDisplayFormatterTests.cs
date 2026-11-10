using Beutl.Configuration;
using Beutl.ProjectSystem;

namespace Beutl.UnitTests.ProjectSystem;

[TestFixture]
public class TimeDisplayFormatterTests
{
    private static readonly IReadOnlyList<SceneMarker> s_noMarkers = [];

    [TestCase(TimeDisplayFormat.Time, "00:00:02.50")]
    [TestCase(TimeDisplayFormat.Timecode, "00:00:02:15")]
    [TestCase(TimeDisplayFormat.Frames, "75")]
    public void Format_WritesTwoAndAHalfSecondsAt30Fps(TimeDisplayFormat format, string expected)
    {
        Assert.That(TimeDisplayFormatter.Format(MediaTime.FromSeconds(2.5), new Rational(30), format), Is.EqualTo(expected));
    }

    [TestCase(24, 1, 23, "00:00:00:23")]
    [TestCase(24, 1, 24, "00:00:01:00")]
    [TestCase(25, 1, 25 * 61, "00:01:01:00")]
    [TestCase(120, 1, 5, "00:00:00:005")]
    [TestCase(120, 1, 119, "00:00:00:119")]
    [TestCase(30, 1, 30 * 3600 * 25, "25:00:00:00")]
    // 23.976 fps counts 24 frames to a second and drops none.
    [TestCase(24000, 1001, 24 * 60, "00:01:00:00")]
    public void Format_WritesTheTimecodeOfTheFrame(long numerator, long denominator, long frame, string expected)
    {
        var rate = new Rational(numerator, denominator);

        Assert.That(TimeDisplayFormatter.Format(MediaTime.FromFrames(frame, rate), rate, TimeDisplayFormat.Timecode), Is.EqualTo(expected));
    }

    // Drop-frame skips the first numbers of every minute but each tenth, and catches up with the clock every ten minutes.
    [TestCase(30000, 1001, 0, "00:00:00;00")]
    [TestCase(30000, 1001, 1799, "00:00:59;29")]
    [TestCase(30000, 1001, 1800, "00:01:00;02")]
    [TestCase(30000, 1001, 3597, "00:01:59;29")]
    [TestCase(30000, 1001, 3598, "00:02:00;02")]
    [TestCase(30000, 1001, 17981, "00:09:59;29")]
    [TestCase(30000, 1001, 17982, "00:10:00;00")]
    [TestCase(30000, 1001, 17983, "00:10:00;01")]
    [TestCase(30000, 1001, 107892, "01:00:00;00")]
    [TestCase(60000, 1001, 3599, "00:00:59;59")]
    [TestCase(60000, 1001, 3600, "00:01:00;04")]
    [TestCase(60000, 1001, 35964, "00:10:00;00")]
    public void Format_CountsDropFrameAt29_97And59_94(long numerator, long denominator, long frame, string expected)
    {
        var rate = new Rational(numerator, denominator);

        Assert.That(TimeDisplayFormatter.Format(MediaTime.FromFrames(frame, rate), rate, TimeDisplayFormat.Timecode), Is.EqualTo(expected));
    }

    [Test]
    public void Format_AtADropFrameRateWrittenUnreduced_StillDropsFrames()
    {
        var rate = new Rational(60000, 2002);

        Assert.That(TimeDisplayFormatter.Format(MediaTime.FromFrames(1800, rate), rate, TimeDisplayFormat.Timecode), Is.EqualTo("00:01:00;02"));
    }

    // A time between frames is the nearer frame, the one the preview shows there.
    [TestCase(0.4, "00:00:00:00", "0")]
    [TestCase(0.6, "00:00:00:01", "1")]
    public void Format_WritesATimeBetweenFramesAsTheNearerFrame(double frames, string timecode, string frameNumber)
    {
        var rate = new Rational(30);
        MediaTime time = MediaTime.FromSeconds(frames / 30);

        Assert.That(
            (TimeDisplayFormatter.Format(time, rate, TimeDisplayFormat.Timecode), TimeDisplayFormatter.Format(time, rate, TimeDisplayFormat.Frames)),
            Is.EqualTo((timecode, frameNumber)));
    }

    // The time has always wrapped at 24 hours, while a timecode counts its hours on.
    [Test]
    public void Format_TimeWrapsAtADay()
    {
        Assert.That(TimeDisplayFormatter.Format(MediaTime.FromSeconds(25 * 3600), new Rational(30), TimeDisplayFormat.Time), Is.EqualTo("01:00:00.00"));
    }

    [Test]
    public void Format_CountsAFrameBeforeTheStartBack()
    {
        var rate = new Rational(30);
        MediaTime time = MediaTime.FromFrames(-5, rate);

        Assert.That(
            (TimeDisplayFormatter.Format(time, rate, TimeDisplayFormat.Timecode), TimeDisplayFormatter.Format(time, rate, TimeDisplayFormat.Frames)),
            Is.EqualTo(("-00:00:00:05", "-5")));
    }

    [TestCase(TimeDisplayFormat.Time)]
    [TestCase(TimeDisplayFormat.Timecode)]
    [TestCase(TimeDisplayFormat.Frames)]
    public void Format_ANonPositiveRate_Throws(TimeDisplayFormat format)
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => TimeDisplayFormatter.Format(MediaTime.Zero, new Rational(0), format));
            Assert.Throws<ArgumentOutOfRangeException>(() => TimeDisplayFormatter.Format(MediaTime.Zero, new Rational(-30), format));
        });
    }

    // Past ten minutes at 59.94 fps, every frame of the first ten minutes and more.
    [TestCase(24, 1)]
    [TestCase(25, 1)]
    [TestCase(30, 1)]
    [TestCase(60, 1)]
    [TestCase(120, 1)]
    [TestCase(24000, 1001)]
    [TestCase(30000, 1001)]
    [TestCase(60000, 1001)]
    public void TimecodesAndFrameNumbers_ParseBackToTheirFrame(long numerator, long denominator)
    {
        var rate = new Rational(numerator, denominator);
        var failures = new List<string>();
        for (long frame = 0; frame < 40_000; frame++)
        {
            MediaTime time = MediaTime.FromFrames(frame, rate);
            foreach (TimeDisplayFormat format in new[] { TimeDisplayFormat.Timecode, TimeDisplayFormat.Frames })
            {
                string text = TimeDisplayFormatter.Format(time, rate, format);
                if (!GotoTimecodeParser.TryParse(text, rate, MediaTime.Zero, s_noMarkers, format, out MediaTime result, out GotoTimecodeError error)
                    || result != time)
                {
                    failures.Add($"{frame} as {text}: {error}, {result}");
                }
            }
        }

        Assert.That(failures, Is.Empty);
    }
}
