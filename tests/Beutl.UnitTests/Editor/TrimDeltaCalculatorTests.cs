using Beutl.Editor.Components.Helpers;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public class TrimDeltaCalculatorTests
{
    // A snap function that pulls any time within one frame of a cut onto that cut.
    private static Func<TimeSpan, TimeSpan> SnapToCut(TimeSpan cut, TimeSpan tolerance)
    {
        return t => (t - cut).Duration() <= tolerance ? cut : t;
    }

    [Test]
    public void SnappedDelta_NullSnap_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => TrimDeltaCalculator.SnappedDelta(TimeSpan.Zero, TimeSpan.Zero, null!));
    }

    [Test]
    public void SnappedDelta_NoMoveClick_IsZero()
    {
        TimeSpan press = TimeSpan.FromSeconds(5);
        TimeSpan release = TimeSpan.FromSeconds(5);

        TimeSpan delta = TrimDeltaCalculator.SnappedDelta(press, release, t => t);

        Assert.That(delta, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void SnappedDelta_NoMoveClickNearCut_SnapsBothEndpointsToZero()
    {
        // Press and release are the same point, both just off a cut. Snapping only the release
        // would yield a spurious one-frame delta; snapping both endpoints identically cancels out.
        TimeSpan cut = TimeSpan.FromSeconds(5);
        TimeSpan point = cut + TimeSpan.FromMilliseconds(10);
        Func<TimeSpan, TimeSpan> snap = SnapToCut(cut, TimeSpan.FromMilliseconds(33));

        TimeSpan delta = TrimDeltaCalculator.SnappedDelta(point, point, snap);

        Assert.That(delta, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void SnappedDelta_ReleaseSnapsToCut_ReturnsSnappedDistance()
    {
        TimeSpan cut = TimeSpan.FromSeconds(5);
        TimeSpan press = TimeSpan.FromSeconds(4);
        TimeSpan release = cut + TimeSpan.FromMilliseconds(10);
        Func<TimeSpan, TimeSpan> snap = SnapToCut(cut, TimeSpan.FromMilliseconds(33));

        TimeSpan delta = TrimDeltaCalculator.SnappedDelta(press, release, snap);

        Assert.That(delta, Is.EqualTo(cut - press));
    }

    [Test]
    public void SnappedDelta_NegativeMovement_IsNegative()
    {
        TimeSpan press = TimeSpan.FromSeconds(5);
        TimeSpan release = TimeSpan.FromSeconds(3);

        TimeSpan delta = TrimDeltaCalculator.SnappedDelta(press, release, t => t);

        Assert.That(delta, Is.EqualTo(TimeSpan.FromSeconds(-2)));
    }
}
