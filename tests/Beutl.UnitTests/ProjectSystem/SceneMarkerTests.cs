using System.ComponentModel;
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

        Assert.That(marker.Time.Value, Is.EqualTo(MediaTime.Zero));
        Assert.That(marker.Color.Value, Is.EqualTo(Colors.Yellow));
        Assert.That(marker.Note.Value, Is.EqualTo(string.Empty));
        Assert.That(marker.Name.Value, Is.EqualTo(string.Empty));
    }

    [Test]
    public void ParameterizedConstructor_AssignsValues()
    {
        var marker = new SceneMarker(
            MediaTime.FromSeconds(2),
            name: "intro",
            color: Colors.Red,
            note: "first marker");

        Assert.That(marker.Time.Value, Is.EqualTo(MediaTime.FromSeconds(2)));
        Assert.That(marker.Name.Value, Is.EqualTo("intro"));
        Assert.That(marker.Color.Value, Is.EqualTo(Colors.Red));
        Assert.That(marker.Note.Value, Is.EqualTo("first marker"));
    }

    [Test]
    public void ParameterizedConstructor_NullsBecomeDefaults()
    {
        var marker = new SceneMarker(MediaTime.FromSeconds(1));

        Assert.That(marker.Name.Value, Is.EqualTo(string.Empty));
        Assert.That(marker.Color.Value, Is.EqualTo(Colors.Yellow));
        Assert.That(marker.Note.Value, Is.EqualTo(string.Empty));
    }

    [Test]
    public void Time_NegativeValueIsClampedToZero()
    {
        var marker = new SceneMarker { Time = { Value = MediaTime.FromSeconds(-1) } };

        Assert.That(marker.Time.Value, Is.EqualTo(MediaTime.Zero));
    }

    [Test]
    public void Time_PositiveValueIsAssigned()
    {
        var marker = new SceneMarker { Time = { Value = MediaTime.FromSeconds(3.5) } };

        Assert.That(marker.Time.Value, Is.EqualTo(MediaTime.FromSeconds(3.5)));
    }

    [Test]
    public void Note_AssigningNullKeepsEmptyString()
    {
        var marker = new SceneMarker { Note = { Value = "hello" } };

        marker.Note.Value = null!;

        Assert.That(marker.Note.Value, Is.EqualTo(string.Empty));
    }

    [Test]
    public void Color_RaisesPropertyChanged()
    {
        var marker = new SceneMarker();
        var captured = new List<string?>();
        ((INotifyPropertyChanged)marker.Color).PropertyChanged += (_, e) => captured.Add(e.PropertyName);

        marker.Color.Value = Colors.Blue;

        Assert.That(captured, Is.EqualTo(new[] { nameof(IProperty<Color>.Value) }));
        Assert.That(marker.Color.Value, Is.EqualTo(Colors.Blue));
    }

    [Test]
    public void Time_RaisesPropertyChanged()
    {
        var marker = new SceneMarker();
        var captured = new List<string?>();
        ((INotifyPropertyChanged)marker.Time).PropertyChanged += (_, e) => captured.Add(e.PropertyName);

        marker.Time.Value = MediaTime.FromSeconds(1);

        Assert.That(captured, Is.EqualTo(new[] { nameof(IProperty<MediaTime>.Value) }));
    }
}
