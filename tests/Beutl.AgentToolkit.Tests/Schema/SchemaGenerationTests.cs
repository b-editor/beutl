using System.ComponentModel.DataAnnotations;
using Beutl.AgentToolkit.Schema;
using Beutl.Animation.Easings;
using Beutl.Audio.Effects;
using Beutl.Engine;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.Services;

namespace Beutl.AgentToolkit.Tests.Schema;

public sealed class SchemaGenerationTests
{
    [Test]
    public void Schema_contains_builtin_parameters_and_fake_extension_type()
    {
        LibraryService.Current.Register<FakeExtensionDrawable>("Fake.Extension.Drawable", "Fake Extension");

        var generator = new SchemaGenerator();
        CapabilitySchema schema = generator.Generate();

        TypeDescriptor textBlock = schema.Types.Single(type => type.Type == typeof(TextBlock).FullName);
        PropertyDescriptor size = textBlock.Properties.Single(property => property.Name == nameof(TextBlock.Size));
        TypeDescriptor linearGradient = schema.Types.Single(type => type.Type == typeof(LinearGradientBrush).FullName);
        PropertyDescriptor gradientStops = linearGradient.Properties.Single(property => property.Name == nameof(GradientBrush.GradientStops));
        TypeDescriptor filterGroup = schema.Types.Single(type => type.Type == typeof(FilterEffectGroup).FullName);
        PropertyDescriptor effectChildren = filterGroup.Properties.Single(property => property.Name == nameof(FilterEffectGroup.Children));
        TypeDescriptor audioEffectGroup = schema.Types.Single(type => type.Type == typeof(AudioEffectGroup).FullName);
        PropertyDescriptor audioEffectChildren = audioEffectGroup.Properties.Single(property => property.Name == nameof(AudioEffectGroup.Children));
        TypeDescriptor transformGroup = schema.Types.Single(type => type.Type == typeof(TransformGroup).FullName);
        PropertyDescriptor transformChildren = transformGroup.Properties.Single(property => property.Name == nameof(TransformGroup.Children));
        TypeDescriptor rectGeometry = schema.Types.Single(type => type.Type == typeof(RectGeometry).FullName);
        TypeDescriptor pen = schema.Types.Single(type => type.Type == typeof(Pen).FullName);
        TypeDescriptor gradientStop = schema.Types.Single(type => type.Type == typeof(GradientStop).FullName);
        TypeDescriptor linearEasing = schema.Types.Single(type => type.Type == typeof(LinearEasing).FullName);
        TypeDescriptor nodeGraphDrawable = schema.Types.Single(type => type.Type == typeof(NodeGraphDrawable).FullName);
        TypeDescriptor nodeGraphFilterEffect = schema.Types.Single(type => type.Type == typeof(NodeGraphFilterEffect).FullName);
        TypeDescriptor graphModel = schema.Types.Single(type => type.Type == typeof(GraphModel).FullName);
        TypeDescriptor fake = schema.Types.Single(type => type.Type == typeof(FakeExtensionDrawable).FullName);
        string emptySceneExample = schema.Examples.Single(example => example.Name == "create-empty-scene-motion-graphics").Patch.ToJsonString();
        string brushEffectExample = schema.Examples.Single(example => example.Name == "apply-gradient-fill-and-effect-chain").Patch.ToJsonString();
        CapabilitySchema audioSchema = generator.Generate(categoryFilter: "AudioEffect");
        CapabilitySchema brushSchema = generator.Generate(categoryFilter: "Brush");
        CapabilitySchema textBlockSchema = generator.Generate(typeFilter: nameof(TextBlock));
        CapabilitySchema freshTextBlockSchema = generator.Generate(typeFilter: nameof(TextBlock));
        textBlockSchema.Examples.Single(example => example.Name == "create-empty-scene-motion-graphics").Patch["Duration"] = "00:00:01";

        Assert.Multiple(() =>
        {
            Assert.That(schema.SchemaVersion, Is.EqualTo(Beutl.AgentToolkit.Common.SchemaVersion.Current));
            Assert.That(textBlock.BaseFields.Any(field => field.Name == nameof(CoreObject.Id)), Is.True);
            Assert.That(size.ValueType, Is.EqualTo(typeof(float).FullName));
            Assert.That(size.Range, Is.Not.Null);
            Assert.That(size.Default, Is.EqualTo(12f));
            Assert.That(size.Animatable, Is.True);
            Assert.That(linearGradient.Category, Is.EqualTo(KnownLibraryItemFormats.Brush));
            Assert.That(gradientStops.ElementType, Is.EqualTo(typeof(GradientStop).FullName));
            Assert.That(effectChildren.ElementType, Is.EqualTo(typeof(FilterEffect).FullName));
            Assert.That(audioEffectGroup.Category, Is.EqualTo(KnownLibraryItemFormats.AudioEffect));
            Assert.That(audioEffectChildren.ElementType, Is.EqualTo(typeof(AudioEffect).FullName));
            Assert.That(transformGroup.Category, Is.EqualTo(KnownLibraryItemFormats.Transform));
            Assert.That(transformChildren.ElementType, Is.EqualTo(typeof(Transform).FullName));
            Assert.That(rectGeometry.Category, Is.EqualTo(KnownLibraryItemFormats.Geometry));
            Assert.That(pen.Category, Is.EqualTo(KnownLibraryItemFormats.Pen));
            Assert.That(gradientStop.Properties.Single(property => property.Name == nameof(GradientStop.Color)).Converter, Does.Contain("ColorJsonConverter"));
            Assert.That(linearEasing.Category, Is.EqualTo(KnownLibraryItemFormats.Easing));
            Assert.That(nodeGraphDrawable.Category, Is.EqualTo(KnownLibraryItemFormats.Drawable));
            Assert.That(nodeGraphFilterEffect.Category, Is.EqualTo(KnownLibraryItemFormats.FilterEffect));
            Assert.That(graphModel.Category, Is.EqualTo(KnownLibraryItemFormats.EngineObject));
            Assert.That(fake.Category, Is.EqualTo("Fake.Extension.Drawable"));
            Assert.That(fake.Properties.Single(property => property.Name == nameof(FakeExtensionDrawable.Amount)).Range, Is.Not.Null);
            Assert.That(schema.Examples.Single(example => example.Name == "animate-float-property-keyframes").Patch.ToJsonString(), Does.Contain("KeyFrames"));
            Assert.That(schema.Examples.Single(example => example.Name == "animate-float-property-keyframes").Patch.ToJsonString(), Does.Contain("KeyFrameAnimation"));
            Assert.That(emptySceneExample, Does.Contain("TextBlock"));
            Assert.That(emptySceneExample, Does.Contain("BEUTL MOTION"));
            Assert.That(emptySceneExample, Does.Contain("LinearGradientBrush"));
            Assert.That(emptySceneExample, Does.Contain("FilterEffectGroup"));
            Assert.That(brushEffectExample, Does.Contain("LinearGradientBrush"));
            Assert.That(brushEffectExample, Does.Contain("GradientStops"));
            Assert.That(brushEffectExample, Does.Contain("FilterEffect"));
            Assert.That(brushEffectExample, Does.Contain("Blur"));
            Assert.That(audioSchema.Types.Any(type => type.Type == typeof(DelayEffect).FullName), Is.True);
            Assert.That(audioSchema.Examples, Is.Empty);
            Assert.That(brushSchema.Examples.Select(example => example.Name), Is.EquivalentTo(new[] { "create-empty-scene-motion-graphics", "apply-gradient-fill-and-effect-chain" }));
            Assert.That(textBlockSchema.Examples.Select(example => example.Name), Does.Contain("create-empty-scene-motion-graphics"));
            Assert.That(freshTextBlockSchema.Examples.Single(example => example.Name == "create-empty-scene-motion-graphics").Patch["Duration"]!.GetValue<string>(), Is.EqualTo("00:00:08"));
        });
    }

    private sealed class FakeExtensionDrawable : EngineObject
    {
        public FakeExtensionDrawable()
        {
            ScanProperties<FakeExtensionDrawable>();
        }

        [System.ComponentModel.DataAnnotations.Range(0, 1)]
        public IProperty<float> Amount { get; } = Property.CreateAnimatable(0.5f);
    }
}
