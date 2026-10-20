using System.Numerics;
using Beutl.ProjectSystem;

namespace Beutl.UnitTests.ProjectSystem;

[TestFixture]
public class TimelineOptionsTests
{
    [Test]
    public void DefaultConstructor_AssignsBaseDefaults()
    {
        var options = new TimelineOptions();

        Assert.That(options.Scale, Is.EqualTo(1f));
        Assert.That(options.Offset, Is.EqualTo(Vector2.Zero));
        Assert.That(options.MaxLayerCount, Is.EqualTo(50));
        Assert.That(options.BpmGrid, Is.EqualTo(new BpmGridOptions()));
    }

    [Test]
    public void Initializer_WithScaleAndOffset_DefaultsLayerCountToFifty()
    {
        var options = new TimelineOptions { Scale = 0.5f, Offset = new Vector2(10, 20) };

        Assert.That(options.Scale, Is.EqualTo(0.5f));
        Assert.That(options.Offset, Is.EqualTo(new Vector2(10, 20)));
        Assert.That(options.MaxLayerCount, Is.EqualTo(50));
    }

    [Test]
    public void Initializer_AcceptsCustomMaxLayerCount()
    {
        var options = new TimelineOptions { MaxLayerCount = 200 };

        Assert.That(options.MaxLayerCount, Is.EqualTo(200));
    }

    [Test]
    public void ScaleInit_ClampsValuesAboveTwo()
    {
        var options = new TimelineOptions { Scale = 5f };

        Assert.That(options.Scale, Is.EqualTo(2f));
    }

    [Test]
    public void ScaleInit_KeepsValuesAtOrBelowTwo()
    {
        var below = new TimelineOptions { Scale = 1.5f };
        var equal = new TimelineOptions { Scale = 2f };

        Assert.That(below.Scale, Is.EqualTo(1.5f));
        Assert.That(equal.Scale, Is.EqualTo(2f));
    }

    [Test]
    public void OffsetInit_ClampsNegativeComponentsToZero()
    {
        var options = new TimelineOptions { Offset = new Vector2(-5, -10) };

        Assert.That(options.Offset, Is.EqualTo(Vector2.Zero));
    }

    [Test]
    public void OffsetInit_KeepsComponentwiseMaxAgainstZero()
    {
        var options = new TimelineOptions { Offset = new Vector2(-3, 7) };

        Assert.That(options.Offset, Is.EqualTo(new Vector2(0, 7)));
    }

    [Test]
    public void OffsetInit_PositiveValuesPreserved()
    {
        var options = new TimelineOptions { Offset = new Vector2(11, 22) };

        Assert.That(options.Offset, Is.EqualTo(new Vector2(11, 22)));
    }
}

[TestFixture]
public class BpmGridOptionsTests
{
    [Test]
    public void DefaultConstructor_AssignsBaselineValues()
    {
        var options = new BpmGridOptions();

        Assert.That(options.Bpm, Is.EqualTo(120.0));
        Assert.That(options.Subdivisions, Is.EqualTo(4));
        Assert.That(options.Offset, Is.EqualTo(TimeSpan.Zero));
        Assert.That(options.IsEnabled, Is.False);
    }

    [Test]
    public void Constructor_AssignsAllValues()
    {
        var options = new BpmGridOptions(140.0, 8, TimeSpan.FromSeconds(0.5), true);

        Assert.That(options.Bpm, Is.EqualTo(140.0));
        Assert.That(options.Subdivisions, Is.EqualTo(8));
        Assert.That(options.Offset, Is.EqualTo(TimeSpan.FromSeconds(0.5)));
        Assert.That(options.IsEnabled, Is.True);
    }
}
