using Beutl.Editor.Components.Helpers;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public class TrimDeltaCalculatorTests
{
    // A snap function that pulls any time within one frame of a cut onto that cut.
    private static Func<MediaTime, MediaTime> SnapToCut(MediaTime cut, MediaTime tolerance)
    {
        return t => new MediaTime(Math.Abs((t - cut).Flicks)) <= tolerance ? cut : t;
    }

    [Test]
    public void SnappedDelta_NullSnap_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => TrimDeltaCalculator.SnappedDelta(MediaTime.Zero, MediaTime.Zero, null!));
    }

    [Test]
    public void SnappedDelta_NoMoveClick_IsZero()
    {
        MediaTime press = MediaTime.FromSeconds(5);
        MediaTime release = MediaTime.FromSeconds(5);

        MediaTime delta = TrimDeltaCalculator.SnappedDelta(press, release, t => t);

        Assert.That(delta, Is.EqualTo(MediaTime.Zero));
    }

    [Test]
    public void SnappedDelta_NoMoveClickNearCut_SnapsBothEndpointsToZero()
    {
        // Press and release are the same point, both just off a cut. Snapping only the release
        // would yield a spurious one-frame delta; snapping both endpoints identically cancels out.
        MediaTime cut = MediaTime.FromSeconds(5);
        MediaTime point = cut + MediaTime.FromSeconds(0.01);
        Func<MediaTime, MediaTime> snap = SnapToCut(cut, MediaTime.FromSeconds(0.033));

        MediaTime delta = TrimDeltaCalculator.SnappedDelta(point, point, snap);

        Assert.That(delta, Is.EqualTo(MediaTime.Zero));
    }

    [Test]
    public void SnappedDelta_ReleaseSnapsToCut_ReturnsSnappedDistance()
    {
        MediaTime cut = MediaTime.FromSeconds(5);
        MediaTime press = MediaTime.FromSeconds(4);
        MediaTime release = cut + MediaTime.FromSeconds(0.01);
        Func<MediaTime, MediaTime> snap = SnapToCut(cut, MediaTime.FromSeconds(0.033));

        MediaTime delta = TrimDeltaCalculator.SnappedDelta(press, release, snap);

        Assert.That(delta, Is.EqualTo(cut - press));
    }

    [Test]
    public void SnappedDelta_NegativeMovement_IsNegative()
    {
        MediaTime press = MediaTime.FromSeconds(5);
        MediaTime release = MediaTime.FromSeconds(3);

        MediaTime delta = TrimDeltaCalculator.SnappedDelta(press, release, t => t);

        Assert.That(delta, Is.EqualTo(MediaTime.FromSeconds(-2)));
    }
}
