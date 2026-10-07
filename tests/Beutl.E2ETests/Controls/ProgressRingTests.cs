using Avalonia.Headless.NUnit;
using Beutl.Controls;

namespace Beutl.E2ETests.Controls;

[TestFixture]
public class ProgressRingTests
{
    [AvaloniaTest]
    public void Changing_Maximum_keeps_the_angles_and_the_progress_arc()
    {
        var ring = new ProgressRing { IsIndeterminate = false };

        ring.Maximum = 200;
        ring.Value = 50;

        Assert.Multiple(() =>
        {
            Assert.That(ring.StartAngle, Is.EqualTo(0));
            Assert.That(ring.EndAngle, Is.EqualTo(360));
            Assert.That(ring.ValueAngle, Is.EqualTo(90));
        });
    }
}
