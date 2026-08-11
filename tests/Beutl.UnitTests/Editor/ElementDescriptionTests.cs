using Beutl.Editor.Models;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.ProjectSystem;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public sealed class ElementDescriptionTests
{
    [Test]
    public void Constructor_RequiresExactlyOneDiscriminatedSource()
    {
        var source = new ElementSource.File("/tmp/x.mp4");
        var description = new ElementDescription(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2),
            3,
            source);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(description.Source, Is.SameAs(source));
            Assert.That(description.Start, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(description.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(description.Layer, Is.EqualTo(3));
            Assert.That(description.Name, Is.EqualTo(string.Empty));
            Assert.That(description.Position, Is.Null);
            Assert.That(description.ProvenanceUpdate.Kind, Is.EqualTo(GenerationProvenanceUpdateKind.Preserve));
            Assert.That(
                typeof(ElementDescription).GetProperties().Select(property => property.Name),
                Does.Not.Contain("FileName")
                    .And.Not.Contain("EngineObjectFactory")
                    .And.Not.Contain("ElementFactory"));
        }

        Assert.That(
            () => new ElementDescription(TimeSpan.Zero, TimeSpan.Zero, 0, null!),
            Throws.ArgumentNullException);
    }

    [Test]
    public void ElementTemplateLengthOverride_IsExplicitlyNullable()
    {
        var templateSource = new ElementSource.ElementTemplate(() => new());

        var preserved = new ElementDescription(TimeSpan.Zero, null, 0, templateSource);
        var zeroLengthOverride = new ElementDescription(TimeSpan.Zero, TimeSpan.Zero, 0, templateSource);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(preserved.Length, Is.Null);
            Assert.That(zeroLengthOverride.Length, Is.EqualTo(TimeSpan.Zero));
            Assert.That(
                () => new ElementDescription(
                    TimeSpan.Zero,
                    null,
                    0,
                    new ElementSource.EngineObject(() => new SourceBackdrop())),
                Throws.ArgumentException);
        }
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

    [Test]
    public void Equality_IsBasedOnAllRequestFields()
    {
        var source = new ElementSource.EngineObject(() => new SourceBackdrop());
        var first = new ElementDescription(TimeSpan.Zero, TimeSpan.FromSeconds(1), 0, source, "x");
        var equivalent = new ElementDescription(TimeSpan.Zero, TimeSpan.FromSeconds(1), 0, source, "x");
        var different = new ElementDescription(TimeSpan.Zero, TimeSpan.FromSeconds(1), 0, source, "y");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.EqualTo(equivalent));
            Assert.That(first, Is.Not.EqualTo(different));
        }
    }

    private static ElementDescription CreateDescription(string name) =>
        new(
            TimeSpan.Zero,
            TimeSpan.FromSeconds(1),
            0,
            new ElementSource.EngineObject(() => new SourceBackdrop()),
            name);
}
