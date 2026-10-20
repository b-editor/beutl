using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.ProjectSystem;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public sealed class ElementDescriptionTests
{
    [Test]
    public void Constructor_RequiresExactlyOneDiscriminatedSource()
    {
        Assert.That(
            () => new ElementDescription(TimeSpan.Zero, TimeSpan.Zero, 0, null!),
            Throws.ArgumentNullException);
    }

    [Test]
    public void EngineObjectSource_ProducesConfiguredObject()
    {
        var description = new ElementDescription(
            TimeSpan.Zero,
            TimeSpan.FromSeconds(5),
            0,
            new ElementSource.EngineObject(
                () => new SourceBackdrop { Clear = { CurrentValue = true } }));

        var source = (ElementSource.EngineObject)description.Source;
        EngineObject produced = source.Factory();

        Assert.That(produced, Is.TypeOf<SourceBackdrop>());
        Assert.That(((SourceBackdrop)produced).Clear.CurrentValue, Is.True);
    }

    [Test]
    public void ResolveName_ReturnsExplicitName_WhenSet()
    {
        var description = CreateDescription("Adjustment Layer");

        Assert.That(description.ResolveName(typeof(SourceBackdrop)), Is.EqualTo("Adjustment Layer"));
    }

    [Test]
    public void ResolveName_FallsBackToLocalizedTypeName_WhenNameEmpty()
    {
        ElementDescription description = CreateDescription(string.Empty);

        Assert.That(
            description.ResolveName(typeof(SourceBackdrop)),
            Is.EqualTo(TypeDisplayHelpers.GetLocalizedName(typeof(SourceBackdrop))));
    }

    [Test]
    public void ResolveName_TreatsWhitespaceNameAsExplicit()
    {
        ElementDescription description = CreateDescription(" ");

        Assert.That(description.ResolveName(typeof(SourceBackdrop)), Is.EqualTo(" "));
    }

    private static ElementDescription CreateDescription(string name) =>
        new(
            TimeSpan.Zero,
            TimeSpan.FromSeconds(1),
            0,
            new ElementSource.EngineObject(() => new SourceBackdrop()),
            name);
}
