using System.ComponentModel.DataAnnotations;
using Beutl.AgentToolkit.Schema;
using Beutl.Engine;
using Beutl.Graphics.Shapes;
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
        TypeDescriptor fake = schema.Types.Single(type => type.Type == typeof(FakeExtensionDrawable).FullName);

        Assert.Multiple(() =>
        {
            Assert.That(schema.SchemaVersion, Is.EqualTo(Beutl.AgentToolkit.Common.SchemaVersion.Current));
            Assert.That(textBlock.BaseFields.Any(field => field.Name == nameof(CoreObject.Id)), Is.True);
            Assert.That(size.ValueType, Is.EqualTo(typeof(float).FullName));
            Assert.That(size.Range, Is.Not.Null);
            Assert.That(size.Default, Is.EqualTo(12f));
            Assert.That(size.Animatable, Is.True);
            Assert.That(fake.Category, Is.EqualTo("Fake.Extension.Drawable"));
            Assert.That(fake.Properties.Single(property => property.Name == nameof(FakeExtensionDrawable.Amount)).Range, Is.Not.Null);
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
