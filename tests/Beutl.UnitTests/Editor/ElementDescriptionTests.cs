using Beutl.Editor.Services;
using Beutl.Graphics;

namespace Beutl.UnitTests.Editor;

public class ElementDescriptionTests
{
    [Test]
    public void Default_HasExpectedDefaults()
    {
        var desc = new ElementDescription(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), 3);
        Assert.That(desc.Start, Is.EqualTo(TimeSpan.FromSeconds(1)));
        Assert.That(desc.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
        Assert.That(desc.Layer, Is.EqualTo(3));
        Assert.That(desc.Name, Is.EqualTo(string.Empty));
        Assert.That(desc.InitialObject, Is.Null);
        Assert.That(desc.FileName, Is.Null);
        Assert.That(desc.Position, Is.EqualTo(default(Point)));
    }

    [Test]
    public void With_AllProperties()
    {
        var desc = new ElementDescription(
            TimeSpan.FromSeconds(0),
            TimeSpan.FromSeconds(5),
            Layer: 2,
            Name: "title",
            InitialObject: typeof(string),
            FileName: "/tmp/x.mp4",
            Position: new Point(10, 20)
        );

        Assert.That(desc.Name, Is.EqualTo("title"));
        Assert.That(desc.InitialObject, Is.EqualTo(typeof(string)));
        Assert.That(desc.FileName, Is.EqualTo("/tmp/x.mp4"));
        Assert.That(desc.Position, Is.EqualTo(new Point(10, 20)));
    }

    [Test]
    public void Equality_BasedOnAllFields()
    {
        var a = new ElementDescription(TimeSpan.Zero, TimeSpan.FromSeconds(1), 0, "x");
        var b = new ElementDescription(TimeSpan.Zero, TimeSpan.FromSeconds(1), 0, "x");
        Assert.That(a, Is.EqualTo(b));

        var c = new ElementDescription(TimeSpan.Zero, TimeSpan.FromSeconds(1), 0, "y");
        Assert.That(a, Is.Not.EqualTo(c));
    }
}
