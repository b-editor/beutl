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
            Assert.That(new ChromaKey().Color.CurrentValue, Is.EqualTo(Colors.Black));
            Assert.That(new ColorKey().Color.CurrentValue, Is.EqualTo(Colors.Black));
            Assert.That(new Lighting().Add.CurrentValue, Is.EqualTo(Colors.Black));
        });
    }

    [Test]
    public void Shadows_DefaultToOpaqueWhite()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new DropShadow().Color.CurrentValue, Is.EqualTo(Colors.White));
            Assert.That(new InnerShadow().Color.CurrentValue, Is.EqualTo(Colors.White));
        });
    }
}
