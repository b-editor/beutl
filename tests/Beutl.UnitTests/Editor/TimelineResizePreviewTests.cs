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
}
