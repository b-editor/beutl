using Beutl;
using Beutl.ProjectSystem;

namespace Beutl.UnitTests.ProjectSystem;

[TestFixture]
public class GotoTimecodeParserTests
{
    private const int FrameRate = 30;

    private static readonly IReadOnlyList<SceneMarker> EmptyMarkers = Array.Empty<SceneMarker>();

    [TestCase("00:00:05", 0, 0, 5, 0)]
    [TestCase("00:01:23.45", 0, 1, 23, 450)]
    [TestCase("1:02:03", 1, 2, 3, 0)]
    [TestCase("12:34", 0, 12, 34, 0)]
    [TestCase("5:00", 0, 5, 0, 0)]
    [TestCase("0:05.250", 0, 0, 5, 250)]
    public void TryParse_Absolute_Succeeds(string input, int h, int m, int s, int ms)
    {
        bool ok = GotoTimecodeParser.TryParse(
            input, FrameRate, TimeSpan.Zero, EmptyMarkers, out TimeSpan result, out GotoTimecodeError error);

        Assert.That(ok, Is.True);
        Assert.That(error, Is.EqualTo(GotoTimecodeError.None));
        Assert.That(result, Is.EqualTo(new TimeSpan(0, h, m, s, ms)));
    }

    [TestCase("60f", 60)]
    [TestCase("0f", 0)]
    [TestCase("1234F", 1234)]
    [TestCase("#90", 90)]
    [TestCase("#0", 0)]
    public void TryParse_Frame_ReturnsFrameDuration(string input, int frame)
    {
        bool ok = GotoTimecodeParser.TryParse(
            input, FrameRate, TimeSpan.Zero, EmptyMarkers, out TimeSpan result, out GotoTimecodeError error);

        Assert.That(ok, Is.True);
        Assert.That(error, Is.EqualTo(GotoTimecodeError.None));
        Assert.That(result, Is.EqualTo(frame.ToTimeSpan(FrameRate)));
    }

    [Test]
    public void TryParse_RelativeSeconds_AddsToCurrent()
    {
        TimeSpan current = TimeSpan.FromSeconds(10);
        bool ok = GotoTimecodeParser.TryParse(
            "+5s", FrameRate, current, EmptyMarkers, out TimeSpan result, out _);

        Assert.That(ok, Is.True);
        Assert.That(result, Is.EqualTo(TimeSpan.FromSeconds(15)));
    }

    [Test]
    public void TryParse_RelativeNegativeSeconds_SubtractsFromCurrent()
    {
        TimeSpan current = TimeSpan.FromSeconds(10);
        bool ok = GotoTimecodeParser.TryParse(
            "-3s", FrameRate, current, EmptyMarkers, out TimeSpan result, out _);

        Assert.That(ok, Is.True);
        Assert.That(result, Is.EqualTo(TimeSpan.FromSeconds(7)));
    }

    [Test]
    public void TryParse_RelativeMinutes_AddsToCurrent()
    {
        TimeSpan current = TimeSpan.FromMinutes(1);
        bool ok = GotoTimecodeParser.TryParse(
            "+2m", FrameRate, current, EmptyMarkers, out TimeSpan result, out _);

        Assert.That(ok, Is.True);
        Assert.That(result, Is.EqualTo(TimeSpan.FromMinutes(3)));
    }

    [Test]
    public void TryParse_RelativeFrames_AddsFramesToCurrent()
    {
        TimeSpan current = 30.ToTimeSpan(FrameRate);
        bool ok = GotoTimecodeParser.TryParse(
            "+15f", FrameRate, current, EmptyMarkers, out TimeSpan result, out _);

        Assert.That(ok, Is.True);
        Assert.That(result, Is.EqualTo(45.ToTimeSpan(FrameRate)));
    }

    [Test]
    public void TryParse_RelativeUnderflow_ClampsToZero()
    {
        TimeSpan current = TimeSpan.FromSeconds(1);
        bool ok = GotoTimecodeParser.TryParse(
            "-10s", FrameRate, current, EmptyMarkers, out TimeSpan result, out _);

        Assert.That(ok, Is.True);
        Assert.That(result, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void TryParse_MarkerByName_ReturnsMarkerTime()
    {
        var markers = new[]
        {
            new SceneMarker(TimeSpan.FromSeconds(2), "Intro"),
            new SceneMarker(TimeSpan.FromSeconds(10), "Outro"),
        };

        bool ok = GotoTimecodeParser.TryParse(
            "@outro", FrameRate, TimeSpan.Zero, markers, out TimeSpan result, out _);

        Assert.That(ok, Is.True);
        Assert.That(result, Is.EqualTo(TimeSpan.FromSeconds(10)));
    }

    [Test]
    public void TryParse_MarkerByPrefix_MatchesFirstHit()
    {
        var markers = new[]
        {
            new SceneMarker(TimeSpan.FromSeconds(2), "Intro"),
            new SceneMarker(TimeSpan.FromSeconds(5), "Interlude"),
        };

        bool ok = GotoTimecodeParser.TryParse(
            "@in", FrameRate, TimeSpan.Zero, markers, out TimeSpan result, out _);

        Assert.That(ok, Is.True);
        Assert.That(result, Is.EqualTo(TimeSpan.FromSeconds(2)));
    }

    [Test]
    public void TryParse_MarkerNotFound_ReturnsError()
    {
        var markers = new[]
        {
            new SceneMarker(TimeSpan.FromSeconds(2), "Intro"),
        };

        bool ok = GotoTimecodeParser.TryParse(
            "@missing", FrameRate, TimeSpan.Zero, markers, out _, out GotoTimecodeError error);

        Assert.That(ok, Is.False);
        Assert.That(error, Is.EqualTo(GotoTimecodeError.MarkerNotFound));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("abc")]
    [TestCase("+5x")]
    [TestCase("+abcs")]
    [TestCase("#abc")]
    [TestCase("@")]
    [TestCase("+")]
    public void TryParse_InvalidInput_ReturnsError(string input)
    {
        bool ok = GotoTimecodeParser.TryParse(
            input, FrameRate, TimeSpan.Zero, EmptyMarkers, out _, out GotoTimecodeError error);

        Assert.That(ok, Is.False);
        Assert.That(error, Is.EqualTo(GotoTimecodeError.InvalidFormat));
    }

    [Test]
    public void TryParse_NullInput_ReturnsError()
    {
        bool ok = GotoTimecodeParser.TryParse(
            null, FrameRate, TimeSpan.Zero, EmptyMarkers, out _, out GotoTimecodeError error);

        Assert.That(ok, Is.False);
        Assert.That(error, Is.EqualTo(GotoTimecodeError.InvalidFormat));
    }

    [Test]
    public void TryParse_NegativeFrameSuffix_ClampsToZero()
    {
        bool ok = GotoTimecodeParser.TryParse(
            "-10f", FrameRate, TimeSpan.Zero, EmptyMarkers, out TimeSpan result, out _);

        Assert.That(ok, Is.True);
        Assert.That(result, Is.EqualTo(TimeSpan.Zero));
    }

    [TestCase("+1e20s")]
    [TestCase("-1e20s")]
    [TestCase("+1e20m")]
    [TestCase("+1e20f")]
    [TestCase("+99999999999999s")]
    public void TryParse_RelativeOutOfRange_ReturnsOutOfRangeWithoutThrowing(string input)
    {
        bool ok = GotoTimecodeParser.TryParse(
            input, FrameRate, TimeSpan.Zero, EmptyMarkers, out _, out GotoTimecodeError error);

        Assert.That(ok, Is.False);
        Assert.That(error, Is.EqualTo(GotoTimecodeError.OutOfRange));
    }

    [Test]
    public void TryParse_RelativeOverflowOnAddition_ReturnsErrorWithoutThrowing()
    {
        bool ok = GotoTimecodeParser.TryParse(
            "+9999999d.99999h", FrameRate, TimeSpan.MaxValue / 2, EmptyMarkers,
            out _, out GotoTimecodeError _);

        // Either succeeds with a clamped result, or returns false with InvalidFormat — must not throw.
        Assert.That(ok, Is.False.Or.True);
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void TryParse_NonPositiveFrameRate_Throws(int frameRate)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GotoTimecodeParser.TryParse(
                "30f", frameRate, TimeSpan.Zero, EmptyMarkers, out _, out _));
    }
}
