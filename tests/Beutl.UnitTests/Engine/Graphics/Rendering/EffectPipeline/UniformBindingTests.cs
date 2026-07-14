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
}
