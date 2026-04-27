using Beutl.Animation.Easings;

namespace Beutl.UnitTests.Engine.Animation;

public class HoldEasingTests
{
    [Test]
    public void Ease_BeforeEnd_ReturnsZero()
    {
        var easing = new HoldEasing();

        Assert.That(easing.Ease(0f), Is.EqualTo(0f));
        Assert.That(easing.Ease(0.5f), Is.EqualTo(0f));
        Assert.That(easing.Ease(0.999f), Is.EqualTo(0f));
    }

    [Test]
    public void Ease_AtEnd_ReturnsOne()
    {
        var easing = new HoldEasing();

        Assert.That(easing.Ease(1f), Is.EqualTo(1f));
    }

    [Test]
    public void Ease_BeyondEnd_ReturnsOne()
    {
        var easing = new HoldEasing();

        Assert.That(easing.Ease(1.5f), Is.EqualTo(1f));
    }
}
