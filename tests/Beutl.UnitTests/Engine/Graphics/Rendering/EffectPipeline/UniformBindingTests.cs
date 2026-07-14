using Beutl.Graphics.Effects;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

[TestFixture]
public class UniformBindingTests
{
    [Test]
    public void FloatArrayUniform_DirectConstructionRejectsNullValues()
    {
        Assert.That(
            () => new FloatArrayUniform("weights", null!),
            Throws.TypeOf<ArgumentNullException>()
                .With.Property(nameof(ArgumentNullException.ParamName)).EqualTo("Values"));
    }

    [Test]
    public void Add_RejectsEmptyPluginUniformName()
    {
        var builder = new UniformBindingBuilder();

        Assert.That(
            () => builder.Add(new FloatUniform("", 1f)),
            Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void Builder_RejectsDuplicateUniformNamesAcrossTypedAndPluginBindings()
    {
        var builder = new UniformBindingBuilder().Float("amount", 1f);

        Assert.That(
            () => builder.Add(new FloatUniform("amount", 2f)),
            Throws.TypeOf<ArgumentException>().With.Message.Contains("Duplicate uniform"));
    }
}
