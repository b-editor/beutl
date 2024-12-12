
using Beutl.Graphics;
using Beutl.Graphics.Effects;

namespace Beutl.UnitTests.Engine.Graphics.Effects;

public class ThresholdTest
{
    [Test]
    public void Threshold_ShouldHaveDefaultValues()
    {
        var threshold = new Threshold();

        Assert.That(threshold.Value, Is.EqualTo(50));
        Assert.That(threshold.Strength, Is.EqualTo(100));
    }

    [Test]
    public void Threshold_ShouldUpdateProperties()
    {
        var threshold = new Threshold();
        threshold.Value = 75;
        threshold.Strength = 50;

        Assert.That(threshold.Value, Is.EqualTo(75));
        Assert.That(threshold.Strength, Is.EqualTo(50));
    }

    [Test]
    public void Threshold_ShouldApplyToContext()
    {
        var threshold = new Threshold();
        threshold.Value = 75;
        threshold.Strength = 50;
        using var context = new FilterEffectContext(new Rect(0, 0, 100, 100));

        context.Apply(threshold);

        // 適用結果の検証
        Assert.That(context._items, Is.Not.Empty);
        Assert.That(context._items[0].FilterEffect, Is.EqualTo(threshold));
        Assert.That(context._items[0].Item, Is.InstanceOf<IFEItem_Skia>());
        Assert.That(context._items[1].FilterEffect, Is.EqualTo(threshold));
        Assert.That(context._items[1].Item, Is.InstanceOf<IFEItem_Skia>());
    }
}
