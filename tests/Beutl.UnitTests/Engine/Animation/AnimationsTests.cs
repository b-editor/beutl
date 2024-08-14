using Beutl.Animation;
using Beutl.Media;
using Moq;

namespace Beutl.UnitTests.Engine.Animation;

public class AnimationsTests
{
    [Test]
    public void Animations_AddItem_ShouldRaiseInvalidatedEvent()
    {
        var animations = new Animations();
        var animation = new Mock<IAnimation>();
        bool eventRaised = false;

        animations.Invalidated += (sender, args) => eventRaised = true;

        animations.Add(animation.Object);

        Assert.That(eventRaised, Is.True);
    }

    [Test]
    public void Animations_RemoveItem_ShouldRaiseInvalidatedEvent()
    {
        var animations = new Animations();
        var animation = new Mock<IAnimation>();
        bool eventRaised = false;

        animations.Add(animation.Object);
        animations.Invalidated += (sender, args) => eventRaised = true;

        animations.Remove(animation.Object);

        Assert.That(eventRaised, Is.True);
    }

    [Test]
    public void Animations_ReplaceItem_ShouldRaiseInvalidatedEvent()
    {
        var animations = new Animations();
        var oldAnimation = new Mock<IAnimation>();
        var newAnimation = new Mock<IAnimation>();
        bool eventRaised = false;

        animations.Add(oldAnimation.Object);
        animations.Invalidated += (sender, args) => eventRaised = true;

        animations[0] = newAnimation.Object;

        Assert.That(eventRaised, Is.True);
    }

    [Test]
    public void Animations_Reset_ShouldRaiseInvalidatedEvent()
    {
        var animations = new Animations();
        var animation = new Mock<IAnimation>();
        bool eventRaised = false;

        animations.Add(animation.Object);
        animations.Invalidated += (sender, args) => eventRaised = true;

        animations.Clear();

        Assert.That(eventRaised, Is.True);
    }

    [Test]
    public void Animations_MoveItem_ShouldRaiseInvalidatedEvent()
    {
        var animations = new Animations();
        var animation1 = new Mock<IAnimation>();
        var animation2 = new Mock<IAnimation>();
        bool eventRaised = false;

        animations.Add(animation1.Object);
        animations.Add(animation2.Object);
        animations.Invalidated += (sender, args) => eventRaised = true;

        animations.Move(0, 1);

        Assert.That(eventRaised, Is.True);
    }

    [Test]
    public void ItemInvalidated_ShouldRaiseInvalidatedEvent()
    {
        var animations = new Animations();
        var animation1 = new Mock<IAnimation>();
        var eventArgs = new RenderInvalidatedEventArgs(animation1.Object);
        bool eventRaised = false;

        animations.Add(animation1.Object);
        animations.Invalidated += (sender, args) => eventRaised = true;
        animation1.Raise(o => o.Invalidated -= null, eventArgs);

        Assert.That(eventRaised, Is.True);
    }
}
