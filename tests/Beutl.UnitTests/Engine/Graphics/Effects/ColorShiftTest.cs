
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Effects;

public class ColorShiftTest
{
    [Test]
    public void ColorShift_ShouldHaveDefaultValues()
    {
        var colorShift = new ColorShift();

        Assert.That(colorShift.RedOffset, Is.EqualTo(new PixelPoint()));
        Assert.That(colorShift.GreenOffset, Is.EqualTo(new PixelPoint()));
        Assert.That(colorShift.BlueOffset, Is.EqualTo(new PixelPoint()));
        Assert.That(colorShift.AlphaOffset, Is.EqualTo(new PixelPoint()));
    }

    [Test]
    public void ColorShift_ShouldUpdateProperties()
    {
        var colorShift = new ColorShift();
        colorShift.RedOffset = new PixelPoint(10, 10);
        colorShift.GreenOffset = new PixelPoint(20, 20);
        colorShift.BlueOffset = new PixelPoint(30, 30);
        colorShift.AlphaOffset = new PixelPoint(40, 40);

        Assert.That(colorShift.RedOffset, Is.EqualTo(new PixelPoint(10, 10)));
        Assert.That(colorShift.GreenOffset, Is.EqualTo(new PixelPoint(20, 20)));
        Assert.That(colorShift.BlueOffset, Is.EqualTo(new PixelPoint(30, 30)));
        Assert.That(colorShift.AlphaOffset, Is.EqualTo(new PixelPoint(40, 40)));
    }

    [Test]
    public void ColorShift_ShouldApplyToContext()
    {
        var colorShift = new ColorShift();
        colorShift.RedOffset = new PixelPoint(10, 10);
        colorShift.GreenOffset = new PixelPoint(20, 20);
        colorShift.BlueOffset = new PixelPoint(30, 30);
        colorShift.AlphaOffset = new PixelPoint(40, 40);
        using var context = new FilterEffectContext(new Rect(0, 0, 100, 100));

        context.Apply(colorShift);

        Assert.That(context._items, Is.Not.Empty);
        Assert.That(context._items[0].FilterEffect, Is.EqualTo(colorShift));
        Assert.That(context._items[0].Item, Is.InstanceOf<IFEItem_Custom>());
    }
}
