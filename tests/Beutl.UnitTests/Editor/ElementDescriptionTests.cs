using Beutl.Editor.Models;
using Beutl.Engine;
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
        // Cast to object so NUnit compares the delegate reference instead of invoking it.
        Assert.That((object?)desc.EngineObjectFactory, Is.Null);
        Assert.That(desc.FileName, Is.Null);
        Assert.That(desc.Position, Is.EqualTo(default(Point)));
    }

    [Test]
    public void With_AllProperties()
    {
        Func<EngineObject> factory = () => new SourceBackdrop();
        var desc = new ElementDescription(
            TimeSpan.FromSeconds(0),
            TimeSpan.FromSeconds(5),
            Layer: 2,
            Name: "title",
            EngineObjectFactory: factory,
            FileName: "/tmp/x.mp4",
            Position: new Point(10, 20));

        Assert.That(desc.Name, Is.EqualTo("title"));
        // Cast to object so NUnit compares the delegate reference instead of invoking it.
        Assert.That((object?)desc.EngineObjectFactory, Is.SameAs(factory));
        Assert.That(desc.FileName, Is.EqualTo("/tmp/x.mp4"));
        Assert.That(desc.Position, Is.EqualTo(new Point(10, 20)));
    }

    [Test]
    public void EngineObjectFactory_ProducesConfiguredObject()
    {
        // Mirrors the "Add Adjustment Layer" caller: the factory both constructs and configures.
        var desc = new ElementDescription(
            TimeSpan.Zero, TimeSpan.FromSeconds(5), 0,
            EngineObjectFactory: () => new SourceBackdrop { Clear = { CurrentValue = true } });

        EngineObject produced = desc.EngineObjectFactory!();

        Assert.That(produced, Is.TypeOf<SourceBackdrop>());
        Assert.That(((SourceBackdrop)produced).Clear.CurrentValue, Is.True);
    }

    [Test]
    public void ResolveName_ReturnsExplicitName_WhenSet()
    {
        var desc = new ElementDescription(TimeSpan.Zero, TimeSpan.FromSeconds(1), 0, Name: "Adjustment Layer");

        // The explicit name wins even though the fallback type has a different localized name.
        Assert.That(desc.ResolveName(typeof(SourceBackdrop)), Is.EqualTo("Adjustment Layer"));
    }

    [Test]
    public void ResolveName_FallsBackToLocalizedTypeName_WhenNameEmpty()
    {
        var desc = new ElementDescription(TimeSpan.Zero, TimeSpan.FromSeconds(1), 0);

        Assert.That(
            desc.ResolveName(typeof(SourceBackdrop)),
            Is.EqualTo(TypeDisplayHelpers.GetLocalizedName(typeof(SourceBackdrop))));
    }

    [Test]
    public void ResolveName_TreatsWhitespaceNameAsExplicit()
    {
        // Pins IsNullOrEmpty (not IsNullOrWhiteSpace) semantics: a whitespace name is kept verbatim.
        var desc = new ElementDescription(TimeSpan.Zero, TimeSpan.FromSeconds(1), 0, Name: " ");

        Assert.That(desc.ResolveName(typeof(SourceBackdrop)), Is.EqualTo(" "));
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
