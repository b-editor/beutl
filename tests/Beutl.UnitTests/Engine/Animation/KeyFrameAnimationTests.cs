using Beutl.Animation;
using Beutl.Serialization;
using Moq;

namespace Beutl.UnitTests.Engine.Animation;

public class KeyFrameAnimationTests
{
    [Test]
    public void ApplyAnimation_ShouldSetTargetValueUsingGlobalClock()
    {
        var property = TestAnimatable.TestProperty;
        var animation = new KeyFrameAnimation<int>(property);
        var target = new TestAnimatable { Test = 1 };
        var clock = new Mock<IClock>();
        var globalClock = new Mock<IClock>();
        clock.Setup(c => c.GlobalClock).Returns(globalClock.Object);
        globalClock.Setup(c => c.CurrentTime).Returns(TimeSpan.FromSeconds(1));
        bool eventRaised = false;
        target.PropertyChanged += (_, _) => eventRaised = true;

        animation.UseGlobalClock = true;
        animation.ApplyAnimation(target, clock.Object);

        Assert.That(eventRaised, Is.True);
    }

    [Test]
    public void ApplyAnimation_ShouldSetTargetValueUsingLocalClock()
    {
        var property = TestAnimatable.TestProperty;
        var animation = new KeyFrameAnimation<int>(property);
        var target = new TestAnimatable { Test = 1 };
        var clock = new Mock<IClock>();
        clock.Setup(c => c.CurrentTime).Returns(TimeSpan.FromSeconds(1));
        bool eventRaised = false;
        target.PropertyChanged += (_, _) => eventRaised = true;

        animation.UseGlobalClock = false;
        animation.ApplyAnimation(target, clock.Object);

        Assert.That(eventRaised, Is.True);
    }

    [Test]
    public void GetAnimatedValue_ShouldReturnInterpolatedValueUsingGlobalClock()
    {
        var property = TestAnimatable.TestProperty;
        var animation = new KeyFrameAnimation<int>(property);
        var clock = new Mock<IClock>();
        var globalClock = new Mock<IClock>();
        clock.Setup(c => c.GlobalClock).Returns(globalClock.Object);
        globalClock.Setup(c => c.CurrentTime).Returns(TimeSpan.FromSeconds(1));

        animation.UseGlobalClock = true;
        var result = animation.GetAnimatedValue(clock.Object);

        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public void GetAnimatedValue_ShouldReturnInterpolatedValueUsingLocalClock()
    {
        var property = TestAnimatable.TestProperty;
        var animation = new KeyFrameAnimation<int>(property);
        var clock = new Mock<IClock>();
        clock.Setup(c => c.CurrentTime).Returns(TimeSpan.FromSeconds(1));

        animation.UseGlobalClock = false;
        var result = animation.GetAnimatedValue(clock.Object);

        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public void Interpolate_ShouldReturnNextValueWhenNoPreviousKeyFrame()
    {
        var property = TestAnimatable.TestProperty;
        var animation = new KeyFrameAnimation<int>(property);
        var nextKeyFrame = new KeyFrame<int>() { KeyTime = TimeSpan.FromSeconds(1), Value = 10 };
        animation.KeyFrames.Add(nextKeyFrame);

        var result = animation.Interpolate(TimeSpan.FromSeconds(1));

        Assert.That(result, Is.EqualTo(10));
    }

    [Test]
    public void Interpolate_ShouldReturnDefaultValueWhenNoKeyFrames()
    {
        var property = TestAnimatable.TestProperty;
        var animation = new KeyFrameAnimation<int>(property);

        var result = animation.Interpolate(TimeSpan.FromSeconds(1));

        Assert.That(result, Is.EqualTo(default(int)));
    }

    [Test]
    public void Interpolate_ShouldReturnNextKeyFrameValueWhenKeyTimeIsSame()
    {
        var property = TestAnimatable.TestProperty;
        var animation = new KeyFrameAnimation<int>(property);
        var keyFrame = new KeyFrame<int> { KeyTime = TimeSpan.FromSeconds(0), Value = 15 };
        animation.KeyFrames.Add(keyFrame);

        var result = animation.Interpolate(TimeSpan.FromSeconds(0));

        Assert.That(result, Is.EqualTo(15));
    }

    [Test]
    public void Interpolate_ShouldReturnLastKeyFrameValueWhenTimeIsAfterLastKeyFrame()
    {
        var property = TestAnimatable.TestProperty;
        var animation = new KeyFrameAnimation<int>(property);
        var keyFrame1 = new KeyFrame<int> { KeyTime = TimeSpan.FromSeconds(1), Value = 0 };
        var keyFrame2 = new KeyFrame<int> { KeyTime = TimeSpan.FromSeconds(2), Value = 10 };
        animation.KeyFrames.Add(keyFrame1);
        animation.KeyFrames.Add(keyFrame2);

        var result = animation.Interpolate(TimeSpan.FromSeconds(3));

        Assert.That(result, Is.EqualTo(10));
    }

    [Test]
    public void Property_Set_ShouldUpdateKeyFramesProperty()
    {
        var property = TestAnimatable.TestProperty;
        var keyFrame1 = new KeyFrame<int> { KeyTime = TimeSpan.FromSeconds(1), Value = 10 };
        var keyFrame2 = new KeyFrame<int> { KeyTime = TimeSpan.FromSeconds(2), Value = 15 };
        var animation = new KeyFrameAnimation<int> { KeyFrames = { keyFrame1, keyFrame2 } };

        Assert.That(keyFrame1.Property, Is.Null);
        Assert.That(keyFrame2.Property, Is.Null);

        animation.Property = property;

        Assert.That(keyFrame1.Property, Is.EqualTo(property));
        Assert.That(keyFrame2.Property, Is.EqualTo(property));
    }

    [Test]
    public void Property_Set_ShouldClampKeyFrameValuesWithinRange()
    {
        var property = TestAnimatable.TestProperty;
        var keyFrame1 = new KeyFrame<int> { KeyTime = TimeSpan.FromSeconds(1), Value = -5 };
        var keyFrame2 = new KeyFrame<int> { KeyTime = TimeSpan.FromSeconds(2), Value = 20 };
        var animation = new KeyFrameAnimation<int> { KeyFrames = { keyFrame1, keyFrame2 } };

        animation.Property = property;

        Assert.That(keyFrame1.Value, Is.EqualTo(0));
        Assert.That(keyFrame2.Value, Is.EqualTo(15));
    }

    [Test]
    public void Serialize_SerializesCorrectly()
    {
        var mockContext = new Mock<ICoreSerializationContext>();
        var property = TestAnimatable.TestProperty;
        var keyFrame1 = new KeyFrame<int> { KeyTime = TimeSpan.FromSeconds(1), Value = 0 };
        var keyFrame2 = new KeyFrame<int> { KeyTime = TimeSpan.FromSeconds(2), Value = 10 };
        var animation = new KeyFrameAnimation<int>(property) { KeyFrames = { keyFrame1, keyFrame2 } };

        animation.Serialize(mockContext.Object);

        mockContext.Verify(c =>
            c.SetValue(nameof(KeyFrameAnimation.KeyFrames), It.IsAny<KeyFrames>()), Times.Once);
        mockContext.Verify(c =>
            c.SetValue(nameof(KeyFrameAnimation.Property), It.IsAny<CoreProperty>()), Times.Once);
    }

    [Test]
    public void Deserialize_DeserializesCorrectly()
    {
        var mockContext = new Mock<ICoreSerializationContext>();
        var property = TestAnimatable.TestProperty;
        var keyFrame1 = new KeyFrame<int> { KeyTime = TimeSpan.FromSeconds(1), Value = 0 };
        var keyFrame2 = new KeyFrame<int> { KeyTime = TimeSpan.FromSeconds(2), Value = 10 };
        var animation = new KeyFrameAnimation<int>(property) { KeyFrames = { keyFrame1, keyFrame2 } };

        mockContext.Setup(c => c.GetValue<KeyFrames>(nameof(KeyFrameAnimation.KeyFrames)))
            .Returns(animation.KeyFrames);
        mockContext.Setup(c => c.GetValue<CoreProperty>(nameof(KeyFrameAnimation.Property)))
            .Returns(property);

        animation.Deserialize(mockContext.Object);

        mockContext.Verify(c =>
            c.GetValue<KeyFrames>(nameof(KeyFrameAnimation.KeyFrames)), Times.Once);
        mockContext.Verify(c =>
            c.GetValue<CoreProperty>(nameof(KeyFrameAnimation.Property)), Times.Once);
    }

    [Test]
    public void GetPreviousAndNextKeyFrame_ShouldReturnNullForBothWhenKeyFramesIsEmpty()
    {
        var animation = new KeyFrameAnimation<int>(TestAnimatable.TestProperty);

        var result = animation.GetPreviousAndNextKeyFrame(new KeyFrame<int>());

        Assert.That(result.Previous, Is.Null);
        Assert.That(result.Next, Is.Null);
    }

    [Test]
    public void GetPreviousAndNextKeyFrame_ShouldReturnNullForPreviousAndNextWhenKeyFrameIsFirst()
    {
        var animation = new KeyFrameAnimation<int>(TestAnimatable.TestProperty);
        var keyFrame = new KeyFrame<int> { KeyTime = TimeSpan.FromSeconds(1) };
        animation.KeyFrames.Add(keyFrame);

        var result = animation.GetPreviousAndNextKeyFrame(keyFrame);

        Assert.That(result.Previous, Is.Null);
        Assert.That(result.Next, Is.Null);
    }

    [Test]
    public void GetPreviousAndNextKeyFrame_ShouldReturnPreviousAndNullForNextWhenKeyFrameIsLast()
    {
        var animation = new KeyFrameAnimation<int>(TestAnimatable.TestProperty);
        var keyFrame1 = new KeyFrame<int> { KeyTime = TimeSpan.FromSeconds(1) };
        var keyFrame2 = new KeyFrame<int> { KeyTime = TimeSpan.FromSeconds(2) };
        animation.KeyFrames.Add(keyFrame1);
        animation.KeyFrames.Add(keyFrame2);

        var result = animation.GetPreviousAndNextKeyFrame(keyFrame2);

        Assert.That(result.Previous, Is.EqualTo(keyFrame1));
        Assert.That(result.Next, Is.Null);
    }

    [Test]
    public void GetPreviousAndNextKeyFrame_ShouldReturnPreviousAndNextWhenKeyFrameIsInMiddle()
    {
        var animation = new KeyFrameAnimation<int>(TestAnimatable.TestProperty);
        var keyFrame1 = new KeyFrame<int> { KeyTime = TimeSpan.FromSeconds(1) };
        var keyFrame2 = new KeyFrame<int> { KeyTime = TimeSpan.FromSeconds(2) };
        var keyFrame3 = new KeyFrame<int> { KeyTime = TimeSpan.FromSeconds(3) };
        animation.KeyFrames.Add(keyFrame1);
        animation.KeyFrames.Add(keyFrame2);
        animation.KeyFrames.Add(keyFrame3);

        var result = animation.GetPreviousAndNextKeyFrame(keyFrame2);

        Assert.That(result.Previous, Is.EqualTo(keyFrame1));
        Assert.That(result.Next, Is.EqualTo(keyFrame3));
    }

    [Test]
    public void KeyTimeIncrease_ShouldReorderKeyFrames()
    {
        var property = TestAnimatable.TestProperty;
        var animation = new KeyFrameAnimation<int>(property);
        var keyFrame1 = new KeyFrame<int> { KeyTime = TimeSpan.FromSeconds(1) };
        var keyFrame2 = new KeyFrame<int> { KeyTime = TimeSpan.FromSeconds(2) };
        var keyFrame3 = new KeyFrame<int> { KeyTime = TimeSpan.FromSeconds(3) };
        animation.KeyFrames.Add(keyFrame1);
        animation.KeyFrames.Add(keyFrame2);
        animation.KeyFrames.Add(keyFrame3);

        keyFrame2.KeyTime = TimeSpan.FromSeconds(4);

        Assert.That(animation.KeyFrames[0], Is.EqualTo(keyFrame1));
        Assert.That(animation.KeyFrames[1], Is.EqualTo(keyFrame3));
        Assert.That(animation.KeyFrames[2], Is.EqualTo(keyFrame2));
    }

    [Test]
    public void KeyTimeDecreased_ShouldReorderKeyFrames()
    {
        var property = TestAnimatable.TestProperty;
        var animation = new KeyFrameAnimation<int>(property);
        var keyFrame1 = new KeyFrame<int> { KeyTime = TimeSpan.FromSeconds(1) };
        var keyFrame2 = new KeyFrame<int> { KeyTime = TimeSpan.FromSeconds(3) };
        var keyFrame3 = new KeyFrame<int> { KeyTime = TimeSpan.FromSeconds(4) };
        animation.KeyFrames.Add(keyFrame1);
        animation.KeyFrames.Add(keyFrame2);
        animation.KeyFrames.Add(keyFrame3);

        keyFrame3.KeyTime = TimeSpan.FromSeconds(0);

        Assert.That(animation.KeyFrames[0], Is.EqualTo(keyFrame3));
        Assert.That(animation.KeyFrames[1], Is.EqualTo(keyFrame1));
        Assert.That(animation.KeyFrames[2], Is.EqualTo(keyFrame2));
    }

    [Test]
    public void ChangingKeyFrameValue_ShouldTriggerKeyFrameAnimationInvalidated()
    {
        var property = TestAnimatable.TestProperty;
        var animation = new KeyFrameAnimation<int>(property);
        var keyFrame = new KeyFrame<int> { KeyTime = TimeSpan.FromSeconds(1) };
        animation.KeyFrames.Add(keyFrame);

        bool invalidatedTriggered = false;
        animation.Invalidated += (sender, e) => invalidatedTriggered = true;

        keyFrame.Value = 10;

        Assert.That(invalidatedTriggered, Is.True);
    }

    [Test]
    public void ChangingKeyFrameValueAfterRemoval_ShouldNotTriggerKeyFrameAnimationInvalidated()
    {
        var property = TestAnimatable.TestProperty;
        var animation = new KeyFrameAnimation<int>(property);
        var keyFrame = new KeyFrame<int> { KeyTime = TimeSpan.FromSeconds(1) };
        animation.KeyFrames.Add(keyFrame);
        animation.KeyFrames.Remove(keyFrame);

        bool invalidatedTriggered = false;
        animation.Invalidated += (sender, e) => invalidatedTriggered = true;

        keyFrame.Value = 10;

        Assert.That(invalidatedTriggered, Is.False);
    }

    [Test]
    public void Duration_ShouldReturnZeroWhenNoKeyFrames()
    {
        var animation = new KeyFrameAnimation<int>(TestAnimatable.TestProperty);

        var duration = animation.Duration;

        Assert.That(duration, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void Duration_ShouldReturnKeyTimeOfLastKeyFrame()
    {
        var animation = new KeyFrameAnimation<int>(TestAnimatable.TestProperty);
        var keyFrame1 = new KeyFrame<int> { KeyTime = TimeSpan.FromSeconds(1) };
        var keyFrame2 = new KeyFrame<int> { KeyTime = TimeSpan.FromSeconds(3) };
        animation.KeyFrames.Add(keyFrame1);
        animation.KeyFrames.Add(keyFrame2);

        var duration = animation.Duration;

        Assert.That(duration, Is.EqualTo(TimeSpan.FromSeconds(3)));
    }
}
