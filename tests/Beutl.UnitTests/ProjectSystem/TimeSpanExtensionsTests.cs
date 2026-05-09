namespace Beutl.UnitTests.ProjectSystem;

[TestFixture]
public class TimeSpanExtensionsTests
{
    [TestCase(0.0, 30, 0.0)]
    [TestCase(1.0, 30, 30.0)]
    [TestCase(0.5, 60, 30.0)]
    [TestCase(0.04166, 24, 0.99984)]
    public void ToFrameNumber_Double_ReturnsSecondsTimesRate(double seconds, double rate, double expected)
    {
        TimeSpan ts = TimeSpan.FromSeconds(seconds);

        double frame = ts.ToFrameNumber(rate);

        Assert.That(frame, Is.EqualTo(expected).Within(1e-9));
    }

    [TestCase(0.0, 30, 0.0)]
    [TestCase(1.0, 30, 30.0)]
    [TestCase(2.0, 60, 120.0)]
    public void ToFrameNumber_Int_ReturnsSecondsTimesRate(double seconds, int rate, double expected)
    {
        TimeSpan ts = TimeSpan.FromSeconds(seconds);

        double frame = ts.ToFrameNumber(rate);

        Assert.That(frame, Is.EqualTo(expected).Within(1e-9));
    }

    [Test]
    public void ToTimeSpan_Double_ReturnsFractionalSeconds()
    {
        TimeSpan ts = 30.0.ToTimeSpan(60.0);

        Assert.That(ts, Is.EqualTo(TimeSpan.FromSeconds(0.5)));
    }

    [Test]
    public void ToTimeSpan_Int_ReturnsExactTickQuotient()
    {
        TimeSpan ts = 30.ToTimeSpan(60);

        Assert.That(ts.Ticks, Is.EqualTo(TimeSpan.TicksPerSecond * 30 / 60));
    }

    [Test]
    public void ToTimeSpan_Int_AvoidsFloatingPointError()
    {
        TimeSpan ts = 1.ToTimeSpan(3);

        Assert.That(ts.Ticks, Is.EqualTo(TimeSpan.TicksPerSecond / 3));
    }

    [Test]
    public void RoundToRate_RoundsUpAtSeventyPercentOfFrame()
    {
        // 0.7 frames at 10 fps -> rounds to 1 frame (AwayFromZero)
        TimeSpan ts = TimeSpan.FromSeconds(0.07);

        TimeSpan rounded = ts.RoundToRate(10.0);

        Assert.That(rounded.TotalSeconds, Is.EqualTo(0.1).Within(1e-9));
    }

    [Test]
    public void RoundToRate_RoundsDownBelowHalfFrame()
    {
        // 0.2 frames at 10 fps -> rounds to 0 frames
        TimeSpan ts = TimeSpan.FromSeconds(0.02);

        TimeSpan rounded = ts.RoundToRate(10.0);

        Assert.That(rounded, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void FloorToRate_DropsFractionalFrame()
    {
        // 1.4 frames at 10 fps -> 1 frame
        TimeSpan ts = TimeSpan.FromSeconds(0.14);

        TimeSpan floored = ts.FloorToRate(10.0);

        Assert.That(floored.TotalSeconds, Is.EqualTo(0.1).Within(1e-9));
    }

    [Test]
    public void FloorToRate_ZeroReturnsZero()
    {
        Assert.That(TimeSpan.Zero.FloorToRate(30.0), Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void RoundToRate_ZeroReturnsZero()
    {
        Assert.That(TimeSpan.Zero.RoundToRate(30.0), Is.EqualTo(TimeSpan.Zero));
    }
}
