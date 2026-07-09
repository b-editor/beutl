using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.Views;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public class TimelineResizePreviewTests
{
    [Test]
    public void CalculateRightResizeX_RippleOff_ClampsToNextElement()
    {
        double x = ElementView.CalculateRightResizeX(
            pointerX: 70,
            afterStartX: 50,
            leftX: 0,
            originalDurationWidth: null,
            ripple: false);

        Assert.That(x, Is.EqualTo(50));
    }

    [Test]
    public void CalculateRightResizeX_RippleOn_AllowsPointerPastNextElement()
    {
        double x = ElementView.CalculateRightResizeX(
            pointerX: 70,
            afterStartX: 50,
            leftX: 0,
            originalDurationWidth: null,
            ripple: true);

        Assert.That(x, Is.EqualTo(70));
    }

    [Test]
    public void CalculateRightResizeX_RippleOn_StillClampsToOriginalDuration()
    {
        double x = ElementView.CalculateRightResizeX(
            pointerX: 90,
            afterStartX: 50,
            leftX: 10,
            originalDurationWidth: 40,
            ripple: true);

        Assert.That(x, Is.EqualTo(50));
    }

    [Test]
    public void CalculateLeftResizeX_RippleOff_ClampsToBeforeElement()
    {
        double x = ElementView.CalculateLeftResizeX(
            pointerX: 10,
            beforeEndX: 40,
            rippleFloorX: 0,
            ripple: false);

        Assert.That(x, Is.EqualTo(40));
    }

    [Test]
    public void CalculateLeftResizeX_RippleOn_AllowsPastBeforeDownToFloor()
    {
        double x = ElementView.CalculateLeftResizeX(
            pointerX: 10,
            beforeEndX: 40,
            rippleFloorX: 0,
            ripple: true);

        Assert.That(x, Is.EqualTo(10));
    }

    [Test]
    public void CalculateLeftResizeX_RippleOn_ClampsToFloor()
    {
        double x = ElementView.CalculateLeftResizeX(
            pointerX: -20,
            beforeEndX: 40,
            rippleFloorX: 0,
            ripple: true);

        Assert.That(x, Is.EqualTo(0));
    }

    [Test]
    public void CalculateLeftResizeX_NoBeforeElement_ReturnsPointer()
    {
        double x = ElementView.CalculateLeftResizeX(
            pointerX: 15,
            beforeEndX: null,
            rippleFloorX: null,
            ripple: true);

        Assert.That(x, Is.EqualTo(15));
    }

    [Test]
    public void ResolveRippleResizeBounds_RightEdge_PreservesModelStart()
    {
        // Off-frame model start (0.02s); the rounded start (0s) must not leak onto the untouched
        // left edge — a right-edge resize keeps the exact model start and takes only the new length.
        (TimeSpan start, TimeSpan length) = ElementViewModel.ResolveRippleResizeBounds(
            leftEdge: false,
            roundedStart: TimeSpan.Zero,
            roundedLength: TimeSpan.FromSeconds(5),
            modelStart: TimeSpan.FromSeconds(0.02),
            modelEnd: TimeSpan.FromSeconds(2));

        Assert.Multiple(() =>
        {
            Assert.That(start, Is.EqualTo(TimeSpan.FromSeconds(0.02)), "exact model start preserved");
            Assert.That(length, Is.EqualTo(TimeSpan.FromSeconds(5)), "dragged length applied");
        });
    }

    [Test]
    public void ResolveRippleResizeBounds_LeftEdge_PreservesModelEnd()
    {
        // Off-frame model end (2.02s); the left-edge resize takes the rounded start and derives the
        // length from the exact model end, so the untouched right edge carries no rounding delta.
        (TimeSpan start, TimeSpan length) = ElementViewModel.ResolveRippleResizeBounds(
            leftEdge: true,
            roundedStart: TimeSpan.FromSeconds(1),
            roundedLength: TimeSpan.FromSeconds(1.5),
            modelStart: TimeSpan.Zero,
            modelEnd: TimeSpan.FromSeconds(2.02));

        Assert.Multiple(() =>
        {
            Assert.That(start, Is.EqualTo(TimeSpan.FromSeconds(1)), "dragged start applied");
            Assert.That(start + length, Is.EqualTo(TimeSpan.FromSeconds(2.02)), "exact model end preserved");
        });
    }
}
