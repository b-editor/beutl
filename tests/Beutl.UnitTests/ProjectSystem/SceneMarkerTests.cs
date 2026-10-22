using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.UnitTests.ProjectSystem;

[TestFixture]
public class SceneMarkerTests
{
    [Test]
    public void DefaultConstructor_HasYellowColorAndEmptyStrings()
    {
        var marker = new SceneMarker();

        Assert.That(marker.Time, Is.EqualTo(MediaTime.Zero));
        Assert.That(marker.Color, Is.EqualTo(Colors.Yellow));
        Assert.That(marker.Note, Is.EqualTo(string.Empty));
        Assert.That(marker.Name, Is.EqualTo(string.Empty));
    }

    [Test]
    public void ParameterizedConstructor_AssignsValues()
    {
        var marker = new SceneMarker(
            MediaTime.FromSeconds(2),
            name: "intro",
            color: Colors.Red,
            note: "first marker");

        Assert.That(marker.Time, Is.EqualTo(MediaTime.FromSeconds(2)));
        Assert.That(marker.Name, Is.EqualTo("intro"));
        Assert.That(marker.Color, Is.EqualTo(Colors.Red));
        Assert.That(marker.Note, Is.EqualTo("first marker"));
    }

    [Test]
    public void ParameterizedConstructor_NullsBecomeDefaults()
    {
        var marker = new SceneMarker(MediaTime.FromSeconds(1));

        Assert.That(marker.Name, Is.EqualTo(string.Empty));
        Assert.That(marker.Color, Is.EqualTo(Colors.Yellow));
        Assert.That(marker.Note, Is.EqualTo(string.Empty));
    }

    [Test]
    public void Time_NegativeValueIsClampedToZero()
    {
        var marker = new SceneMarker { Time = MediaTime.FromSeconds(-1) };

        Assert.That(marker.Time, Is.EqualTo(MediaTime.Zero));
    }

    [Test]
    public void Time_PositiveValueIsAssigned()
    {
        var marker = new SceneMarker { Time = MediaTime.FromSeconds(3.5) };

        Assert.That(marker.Time, Is.EqualTo(MediaTime.FromSeconds(3.5)));
    }

    [Test]
    public void Note_AssigningNullKeepsEmptyString()
    {
        var marker = new SceneMarker { Note = "hello" };

        marker.Note = null!;

        Assert.That(marker.Note, Is.EqualTo(string.Empty));
    }

    [Test]
    public void Color_RaisesPropertyChanged()
    {
        var marker = new SceneMarker();
        var captured = new List<string?>();
        marker.PropertyChanged += (_, e) => captured.Add(e.PropertyName);

        marker.Color = Colors.Blue;

        Assert.That(captured, Does.Contain(nameof(SceneMarker.Color)));
        Assert.That(marker.Color, Is.EqualTo(Colors.Blue));
    }

    [Test]
    public void Time_RaisesPropertyChanged()
    {
        var marker = new SceneMarker();
        var captured = new List<string?>();
        marker.PropertyChanged += (_, e) => captured.Add(e.PropertyName);

        marker.Time = MediaTime.FromSeconds(1);

        Assert.That(captured, Does.Contain(nameof(SceneMarker.Time)));
    }
}
