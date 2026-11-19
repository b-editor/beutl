using Avalonia;
using Beutl.Helpers;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class WindowsAutoHideTaskbarTests
{
    [Test]
    public void BottomEdgeLeavesTwoPixelsFree()
    {
        var monitor = new PixelRect(0, 0, 1920, 1080);

        Assert.That(WindowsAutoHideTaskbar.Shrink(monitor, monitor, AutoHideEdges.Bottom),
            Is.EqualTo(new PixelRect(0, 0, 1920, 1078)));
    }

    [Test]
    public void LeftAndTopEdgesLeaveTwoPixelsFreeOnAnOffsetMonitor()
    {
        var monitor = new PixelRect(-1920, -1080, 1920, 1080);

        Assert.That(WindowsAutoHideTaskbar.Shrink(monitor, monitor, AutoHideEdges.Left | AutoHideEdges.Top),
            Is.EqualTo(new PixelRect(-1918, -1078, 1918, 1078)));
    }

    [Test]
    public void RightEdgeLeavesTwoPixelsFree()
    {
        var monitor = new PixelRect(0, 0, 1920, 1080);

        Assert.That(WindowsAutoHideTaskbar.Shrink(monitor, monitor, AutoHideEdges.Right),
            Is.EqualTo(new PixelRect(0, 0, 1918, 1080)));
    }

    [Test]
    public void NoAutoHideEdgeLeavesTheClientUnchanged()
    {
        var monitor = new PixelRect(0, 0, 1920, 1080);

        Assert.That(WindowsAutoHideTaskbar.Shrink(monitor, monitor, AutoHideEdges.None), Is.EqualTo(monitor));
    }

    [Test]
    public void AnEdgeAlreadyInsideTheMonitorIsUnchanged()
    {
        var monitor = new PixelRect(0, 0, 1920, 1080);
        var bottomWorkArea = new PixelRect(0, 0, 1920, 1040);
        var leftWorkArea = new PixelRect(40, 0, 1880, 1080);
        var topWorkArea = new PixelRect(0, 40, 1920, 1040);
        var rightWorkArea = new PixelRect(0, 0, 1880, 1080);

        Assert.Multiple(() =>
        {
            Assert.That(WindowsAutoHideTaskbar.Shrink(bottomWorkArea, monitor, AutoHideEdges.Bottom), Is.EqualTo(bottomWorkArea));
            Assert.That(WindowsAutoHideTaskbar.Shrink(leftWorkArea, monitor, AutoHideEdges.Left), Is.EqualTo(leftWorkArea));
            Assert.That(WindowsAutoHideTaskbar.Shrink(topWorkArea, monitor, AutoHideEdges.Top), Is.EqualTo(topWorkArea));
            Assert.That(WindowsAutoHideTaskbar.Shrink(rightWorkArea, monitor, AutoHideEdges.Right), Is.EqualTo(rightWorkArea));
        });
    }
}
