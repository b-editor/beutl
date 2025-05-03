using Beutl.Animation;
using Beutl.Media;
using Beutl.Serialization;
using Moq;

namespace Beutl.UnitTests.Engine.Animation;

public class AnimatableTests
{
    [Test]
    public void Animatable_InitializesCorrectly()
    {
        var animatable = new TestAnimatable();
        Assert.That(animatable.Animations, Is.Not.Null);
        Assert.That(animatable.Animations, Is.Empty);
    }

    [Test]
    public void ApplyAnimations_AppliesAllAnimations()
    {
        var animatable = new TestAnimatable();
        var mockClock = new Mock<IClock>();
        var mockAnimation = new Mock<IAnimation>();
        animatable.Animations.Add(mockAnimation.Object);

        animatable.ApplyAnimations(mockClock.Object);

        mockAnimation.Verify(a => a.ApplyAnimation(animatable, mockClock.Object), Times.Once);
    }

    [Test]
    public void Serialize_SerializesCorrectly()
    {
        var animatable = new TestAnimatable();
        var mockContext = new Mock<ICoreSerializationContext>();
        var mockAnimation = new Mock<IAnimation>();
        mockAnimation.SetupGet(a => a.Property).Returns(TestAnimatable.TestProperty);
        animatable.Animations.Add(mockAnimation.Object);

        animatable.Serialize(mockContext.Object);

        mockContext.Verify(c =>
            c.SetValue(nameof(Animatable.Animations), It.IsAny<Dictionary<string, IAnimation>>()), Times.Once);
    }

    [Test]
    public void Deserialize_DeserializesCorrectly()
    {
        var animatable = new TestAnimatable();
        var mockContext = new Mock<ICoreSerializationContext>();
        var mockAnimation1 = new Mock<KeyFrameAnimation>();
        var animations = new Dictionary<string, IAnimation> { { "Test1", mockAnimation1.Object } };

        mockContext.Setup(c => c.GetValue<Dictionary<string, IAnimation>>(nameof(Animatable.Animations)))
            .Returns(animations);

        animatable.Deserialize(mockContext.Object);

        Assert.That(animatable.Animations.Count, Is.EqualTo(1));
        Assert.That(animatable.Animations.First(), Is.EqualTo(mockAnimation1.Object));
    }

    [Test]
    public void AnimationInvalidated_EventIsHandledCorrectly()
    {
        var animatable = new TestAnimatable();
        bool eventRaised = false;
        EventHandler<RenderInvalidatedEventArgs> handler = (sender, args) => eventRaised = true;

        animatable.AnimationInvalidated += handler;
        animatable.Animations.Add(Mock.Of<IAnimation>());

        Assert.That(eventRaised, Is.True);

        eventRaised = false;
        animatable.AnimationInvalidated -= handler;
        animatable.Animations.Clear();

        Assert.That(eventRaised, Is.False);
    }
}
