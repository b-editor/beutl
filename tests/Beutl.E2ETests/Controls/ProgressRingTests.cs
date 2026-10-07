using System.Reactive.Subjects;
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
        Assert.Multiple(() =>
        {
            Assert.That(ring.StartAngle, Is.EqualTo(0));
            Assert.That(ring.EndAngle, Is.EqualTo(360));
            Assert.That(ring.ValueAngle, Is.EqualTo(0));
        });

        ring.Maximum = 200;
        ring.Value = 50;

        Assert.Multiple(() =>
        {
            Assert.That(ring.StartAngle, Is.EqualTo(0));
            Assert.That(ring.EndAngle, Is.EqualTo(360));
            Assert.That(ring.ValueAngle, Is.EqualTo(90));
        });
    }

    [AvaloniaTest]
    public void A_bound_angle_keeps_following_its_source_and_redraws_the_arc()
    {
        var ring = new ProgressRing { IsIndeterminate = false, Maximum = 200, Value = 50 };
        using var endAngle = new BehaviorSubject<double>(360);
        using IDisposable binding = ring.Bind(ProgressRing.EndAngleProperty, endAngle);

        endAngle.OnNext(180);
        endAngle.OnNext(120);

        Assert.Multiple(() =>
        {
            Assert.That(ring.EndAngle, Is.EqualTo(120), "The binding was replaced by a local value.");
            Assert.That(ring.ValueAngle, Is.EqualTo(30));
        });
    }
}
