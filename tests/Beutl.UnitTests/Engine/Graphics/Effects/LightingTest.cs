
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Effects;

public class LightingTest
{
    [Test]
    public void Lighting_ShouldHaveDefaultValues()
    {
        var lighting = new Lighting();

        Assert.That(lighting.Multiply, Is.EqualTo(Colors.White));
        Assert.That(lighting.Add, Is.EqualTo((Color)default));
    }

    [Test]
    public void Lighting_ShouldUpdateProperties()
    {
        var lighting = new Lighting();
        lighting.Multiply = Colors.Red;
        lighting.Add = Colors.Blue;

        Assert.That(lighting.Multiply, Is.EqualTo(Colors.Red));
        Assert.That(lighting.Add, Is.EqualTo(Colors.Blue));
    }

    [Test]
    public void Lighting_ShouldApplyToContext()
    {
        var lighting = new Lighting();
        lighting.Multiply = Colors.Red;
        lighting.Add = Colors.Blue;
        using var context = new FilterEffectContext(new Rect(0, 0, 100, 100));

        context.Apply(lighting);

        Assert.That(context._items, Is.Not.Empty);
        Assert.That(context._items[0].FilterEffect, Is.EqualTo(lighting));
        Assert.That(context._items[0].Item, Is.InstanceOf<IFEItem_Skia>());
    }
}
