using Beutl.Animation;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Editor.Services.Captions;
using Beutl.Engine.Expressions;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;

namespace Beutl.UnitTests.Editor.Services.Captions;

[TestFixture]
public class CaptionTemplateTests
{
    private static readonly CaptionTemplateProviderId s_testProvider = new("beutl.tests");

    [Test]
    public void DefaultTemplate_CreatesTextDescriptionAndAppliesContextPlacement()
    {
        CaptionTemplateContribution template = CaptionTemplateDefaults.CreateDefaultText("Default");
        var cue = new CaptionCue(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1.2), "Hello");
        var context = new CaptionElementContext(
            layer: 3,
            elementName: "Caption",
            defaultPosition: new Point(0, 240));

        ElementDescription description = template.CreateElements(cue, context).Single();
        TextBlock text = CreateTextBlock(description);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(template.Name, Is.EqualTo("Default"));
            Assert.That(description.Start, Is.EqualTo(cue.Start));
            Assert.That(description.Length, Is.EqualTo(TimeSpan.FromSeconds(0.5)));
            Assert.That(description.Layer, Is.EqualTo(3));
            Assert.That(description.Name, Is.EqualTo("Caption"));
            Assert.That(description.Position, Is.EqualTo(new Point(0, 240)));
            Assert.That(text.Text.CurrentValue, Is.EqualTo("Hello"));
            Assert.That(text.Size.CurrentValue, Is.EqualTo(48));
            Assert.That(text.AlignmentX.CurrentValue, Is.EqualTo(AlignmentX.Center));
            Assert.That(text.AlignmentY.CurrentValue, Is.EqualTo(AlignmentY.Center));
        }
    }

    [Test]
    public void TextBlockAdapter_PreservesStyleAndAuthoredPlacementWhileReplacingText()
    {
        var source = new TextBlock
        {
            Text = { CurrentValue = "Placeholder" },
            Size = { CurrentValue = 72 },
            FontWeight = { CurrentValue = FontWeight.Bold },
            Pen =
            {
                CurrentValue = new Pen
                {
                    Brush = { CurrentValue = Brushes.Black },
                    Thickness = { CurrentValue = 4 },
                }
            },
            Transform = { CurrentValue = new TranslateTransform(120, 300) },
            FilterEffect = { CurrentValue = new Blur { Sigma = { CurrentValue = new Size(2, 3) } } },
        };
        source.Opacity.Animation = new KeyFrameAnimation<float>();
        source.Text.Expression = Expression.Create<string?>("\"Expression text\"");
        ObjectTemplateItem item = ObjectTemplateItem.CreateFromInstance(source, "Outlined caption");
        CaptionTemplateContribution template = TextBlockCaptionTemplateAdapter.TryCreate(item)!;
        var context = new CaptionElementContext(4, "Caption", new Point(0, 200));

        ElementDescription firstDescription = template.CreateElements(
            new CaptionCue(TimeSpan.Zero, TimeSpan.FromSeconds(1), "First"),
            context).Single();
        ElementDescription secondDescription = template.CreateElements(
            new CaptionCue(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "Second"),
            context).Single();
        TextBlock first = CreateTextBlock(firstDescription);
        TextBlock second = CreateTextBlock(secondDescription);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(template.Name, Is.EqualTo("Outlined caption"));
            Assert.That(firstDescription.Position, Is.Null);
            Assert.That(first.Text.CurrentValue, Is.EqualTo("First"));
            Assert.That(second.Text.CurrentValue, Is.EqualTo("Second"));
            Assert.That(first.Text.Expression, Is.Null);
            Assert.That(first.Size.CurrentValue, Is.EqualTo(72));
            Assert.That(first.FontWeight.CurrentValue, Is.EqualTo(FontWeight.Bold));
            Assert.That(first.Pen.CurrentValue?.Thickness.CurrentValue, Is.EqualTo(4));
            Assert.That(first.Transform.CurrentValue, Is.TypeOf<TranslateTransform>());
            Assert.That(((TranslateTransform)first.Transform.CurrentValue!).X.CurrentValue, Is.EqualTo(120));
            Assert.That(((TranslateTransform)first.Transform.CurrentValue!).Y.CurrentValue, Is.EqualTo(300));
            Assert.That(first.FilterEffect.CurrentValue, Is.TypeOf<Blur>());
            Assert.That(((Blur)first.FilterEffect.CurrentValue!).Sigma.CurrentValue, Is.EqualTo(new Size(2, 3)));
            Assert.That(first.Opacity.Animation, Is.Not.Null);
            Assert.That(first.Id, Is.Not.EqualTo(source.Id));
            Assert.That(second.Id, Is.Not.EqualTo(first.Id));
        }
    }

    [Test]
    public void TextBlockAdapter_NonTextTemplateIsNotAdapted()
    {
        ObjectTemplateItem item = ObjectTemplateItem.CreateFromInstance(new EllipseShape(), "Ellipse");

        CaptionTemplateContribution? result = TextBlockCaptionTemplateAdapter.TryCreate(item);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void CustomFactory_ReceivesCueAndCanReturnMultipleElementRequests()
    {
        var factory = new MultiElementFactory();
        var template = new CaptionTemplateContribution(
            new CaptionTemplateId("beutl.tests.bilingual"),
            s_testProvider,
            "Bilingual",
            factory,
            DefaultCaptionPlacementPolicy.Instance);
        var cue = new CaptionCue(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), "Hello");
        var context = new CaptionElementContext(6, "Caption", new Point(0, 180));

        IReadOnlyList<ElementDescription> descriptions = template.CreateElements(cue, context);

        Assert.Multiple(() =>
        {
            Assert.That(factory.ReceivedCue, Is.SameAs(cue));
            Assert.That(descriptions, Has.Count.EqualTo(2));
            Assert.That(descriptions.Select(item => item.Layer), Is.EqualTo(new[] { 6, 7 }));
            Assert.That(descriptions[0].Position, Is.EqualTo(new Point(20, 30)));
            Assert.That(descriptions[1].Position, Is.EqualTo(context.DefaultPosition));
            Assert.That(
                descriptions.Select(item => CreateTextBlock(item).Text.CurrentValue),
                Is.EqualTo(new[] { "Hello", "Hello translation" }));
        });
    }

    [Test]
    public void DefaultPlacement_PreservesAnExplicitOrigin()
    {
        var template = new CaptionTemplateContribution(
            new CaptionTemplateId("beutl.tests.origin"),
            s_testProvider,
            "Origin",
            new ExplicitOriginFactory(),
            DefaultCaptionPlacementPolicy.Instance);
        var context = new CaptionElementContext(0, "Caption", new Point(100, 200));

        ElementDescription description = template.CreateElements(
            new CaptionCue(TimeSpan.Zero, TimeSpan.FromSeconds(1), "Text"),
            context).Single();

        Assert.That(description.Position, Is.EqualTo(new Point(0, 0)));
    }

    [Test]
    public void CreateElements_RejectsNullFactoryAndPlacementResults()
    {
        var cue = new CaptionCue(TimeSpan.Zero, TimeSpan.FromSeconds(1), "Text");
        var context = new CaptionElementContext(0, "Caption");
        var nullFactoryTemplate = new CaptionTemplateContribution(
            new CaptionTemplateId("beutl.tests.null-factory"),
            s_testProvider,
            "Null factory item",
            new NullItemFactory(),
            PreserveCaptionPlacementPolicy.Instance);
        var nullPlacementTemplate = new CaptionTemplateContribution(
            new CaptionTemplateId("beutl.tests.null-placement"),
            s_testProvider,
            "Null placement item",
            DefaultTextCaptionElementFactory.Instance,
            new NullPlacementPolicy());

        Assert.Multiple(() =>
        {
            Assert.That(
                () => nullFactoryTemplate.CreateElements(cue, context),
                Throws.InvalidOperationException.With.Message.Contains("null element description"));
            Assert.That(
                () => nullPlacementTemplate.CreateElements(cue, context),
                Throws.InvalidOperationException.With.Message.Contains("placement returned null"));
        });
    }

    private static TextBlock CreateTextBlock(ElementDescription description) =>
        (TextBlock)((ElementSource.EngineObject)description.Source).Factory();

    private sealed class MultiElementFactory : ICaptionElementFactory
    {
        public CaptionCue? ReceivedCue { get; private set; }

        public IReadOnlyList<ElementDescription> CreateElements(
            CaptionCue cue,
            CaptionElementContext context)
        {
            ReceivedCue = cue;
            return
            [
                context.CreateDescription(
                    cue,
                    () => new TextBlock { Text = { CurrentValue = cue.Text } },
                    position: new Point(20, 30)),
                context.CreateDescription(
                    cue,
                    () => new TextBlock { Text = { CurrentValue = cue.Text + " translation" } },
                    layerOffset: 1),
            ];
        }
    }

    private sealed class ExplicitOriginFactory : ICaptionElementFactory
    {
        public IReadOnlyList<ElementDescription> CreateElements(
            CaptionCue cue,
            CaptionElementContext context)
            =>
            [
                context.CreateDescription(
                    cue,
                    () => new TextBlock(),
                    position: new Point(0, 0)),
            ];
    }

    private sealed class NullItemFactory : ICaptionElementFactory
    {
        public IReadOnlyList<ElementDescription> CreateElements(
            CaptionCue cue,
            CaptionElementContext context)
            => [null!];
    }

    private sealed class NullPlacementPolicy : ICaptionPlacementPolicy
    {
        public ElementDescription Place(
            CaptionCue cue,
            CaptionElementContext context,
            ElementDescription description,
            int elementIndex)
            => null!;
    }
}
