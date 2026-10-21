using Beutl.Graphics.Effects;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.FilterEffects;

[TestFixture]
public sealed class FilterEffectDefaultColorTests
{
    [Test]
    public void KeyingAndLightingAdd_DefaultToOpaqueBlack()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new ChromaKey().Color.Value, Is.EqualTo(Colors.Black));
            Assert.That(new ColorKey().Color.Value, Is.EqualTo(Colors.Black));
            Assert.That(new Lighting().Add.Value, Is.EqualTo(Colors.Black));
        });
    }

    [Test]
    public void Shadows_DefaultToOpaqueWhite()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new DropShadow().Color.Value, Is.EqualTo(Colors.White));
            Assert.That(new InnerShadow().Color.Value, Is.EqualTo(Colors.White));
        });
    }
}
