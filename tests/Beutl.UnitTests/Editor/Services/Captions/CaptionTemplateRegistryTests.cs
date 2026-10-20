namespace Beutl.UnitTests.Editor.Services.Captions;

using Beutl.Editor.Services;
using Beutl.Editor.Services.Captions;
using Beutl.Extensibility;
using Beutl.Graphics;

[TestFixture]
public sealed class CaptionTemplateRegistryTests
{
    private static readonly CaptionTemplateProviderId s_provider = new("beutl.tests");
    private static readonly DefaultTextCaptionElementFactory s_textFactory =
        new(Beutl.Media.FontFamily.Default);

    [Test]
    public void DataOnlyPlacementCannotReplaceTheFactoryCreatedSource()
    {
        var source = new ElementSource.EngineObject(() => new Beutl.Graphics.Shapes.TextBlock());
        CaptionTemplate template = CreateTemplate(
            new CaptionTemplateId("beutl.tests.source-preservation"),
            "Source preservation",
            new FixedSourceFactory(source),
            new FixedPlacementPolicy(new Point(12, 34), layer: 9));
        var registry = new CaptionTemplateRegistry([template]);

        ElementDescription description = registry.GetTemplate(template.Descriptor.Id)
            .CreateElements(
                new CaptionCue(TimeSpan.Zero, TimeSpan.FromSeconds(1), "text"),
                new CaptionElementContext(0, "Caption"))
            .Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(description.Source, Is.SameAs(source));
            Assert.That(description.Position, Is.EqualTo(new Point(12, 34)));
            Assert.That(description.Layer, Is.EqualTo(9));
            Assert.That(
                typeof(CaptionElementPlacement).GetProperties()
                    .Select(property => property.PropertyType),
                Does.Not.Contain(typeof(ElementSource)));
        }
    }

    [Test]
    public void Templates_AreOrderedByExplicitOrderThenStableIdentifier()
    {
        var registry = new CaptionTemplateRegistry(
        [
            CreateText("beutl.tests.zulu", "Zulu", order: 10),
            CreateText("beutl.tests.beta", "Beta"),
            CreateText("beutl.tests.alpha", "Alpha"),
        ]);

        Assert.That(
            registry.Templates.Select(template => template.Name),
            Is.EqualTo(new[] { "Alpha", "Beta", "Zulu" }));
    }

    [Test]
    public void Catalog_TemplateListObserverFailureDoesNotInterruptRefresh()
    {
        CaptionCatalog catalog = Compose("Default", []);
        catalog.Templates.Templates.CollectionChanged += (_, _) =>
            throw new InvalidOperationException("Observer failure");
        ObjectTemplateItem item = ObjectTemplateItem.CreateFromInstance(
            new Beutl.Graphics.Shapes.TextBlock(),
            "Safe");

        Assert.DoesNotThrow(() => catalog.RefreshObjectTemplates([item]));
        Assert.That(
            catalog.Templates.Templates.Select(template => template.Name),
            Does.Contain("Safe"));
    }

    [Test]
    public void Catalog_RefreshObjectTemplatesUpdatesTheSharedMetadataView()
    {
        CaptionCatalog catalog = Compose("Default", []);
        ObjectTemplateItem item = ObjectTemplateItem.CreateFromInstance(
            new Beutl.Graphics.Shapes.TextBlock(),
            "Saved caption");

        catalog.RefreshObjectTemplates([item]);
        Assert.That(
            catalog.Templates.Templates.Select(template => template.Name),
            Does.Contain("Saved caption"));

        catalog.RefreshObjectTemplates([]);
        Assert.That(
            catalog.Templates.Templates.Select(template => template.Name),
            Does.Not.Contain("Saved caption"));
    }

    [Test]
    public void Catalog_ObjectTemplatesRemainAlphabeticalAcrossComposeAndRefresh()
    {
        ObjectTemplateItem zulu = ObjectTemplateItem.CreateFromInstance(
            new Beutl.Graphics.Shapes.TextBlock(),
            "Zulu");
        ObjectTemplateItem alpha = ObjectTemplateItem.CreateFromInstance(
            new Beutl.Graphics.Shapes.TextBlock(),
            "Alpha");
        CaptionCatalog catalog = Compose("Default", [zulu, alpha]);

        Assert.That(
            catalog.Templates.Templates.Select(template => template.Name),
            Is.EqualTo(new[] { "Default", "Alpha", "Zulu" }));

        ObjectTemplateItem beta = ObjectTemplateItem.CreateFromInstance(
            new Beutl.Graphics.Shapes.TextBlock(),
            "Beta");
        catalog.RefreshObjectTemplates([zulu, beta, alpha]);

        Assert.That(
            catalog.Templates.Templates.Select(template => template.Name),
            Is.EqualTo(new[] { "Default", "Alpha", "Beta", "Zulu" }));
    }

    // None of these tests creates elements from the default template, so it gets a test factory.
    private static CaptionCatalog Compose(string defaultTemplateName, IEnumerable<ObjectTemplateItem> objectTemplates)
        => CaptionCatalog.ComposeWithDefaultElementFactory(
            defaultTemplateName,
            objectTemplates,
            new TaggedFactory(defaultTemplateName));

    private static CaptionTemplate CreateText(string id, string name, int order = 0)
        => CreateTemplate(
            new CaptionTemplateId(id),
            name,
            s_textFactory,
            DefaultCaptionPlacementPolicy.Instance,
            order);

    internal static CaptionTemplate CreateTemplate(
        CaptionTemplateId id,
        string name,
        ICaptionElementFactory factory,
        ICaptionPlacementPolicy placement,
        int order = 0)
        => new(new CaptionTemplateDescriptor(id, s_provider, name, order), factory, placement);

    private sealed class TaggedFactory(string name) : ICaptionElementFactory
    {
        public IReadOnlyList<ElementDescription> CreateElements(
            CaptionCue cue,
            CaptionElementContext context)
            => [context.CreateDescription(cue, () => new Beutl.Graphics.Shapes.TextBlock(), name: name)];
    }

    private sealed class FixedSourceFactory(ElementSource source) : ICaptionElementFactory
    {
        public IReadOnlyList<ElementDescription> CreateElements(
            CaptionCue cue,
            CaptionElementContext context)
            => [new ElementDescription(cue.Start, cue.End - cue.Start, context.Layer, source)];
    }

    private sealed class FixedPlacementPolicy(Point position, int layer) : ICaptionPlacementPolicy
    {
        public CaptionElementPlacement Place(
            CaptionCue cue,
            CaptionElementContext context,
            CaptionElementPlacement placement,
            int elementIndex)
            => placement with { Position = position, Layer = layer };
    }
}
