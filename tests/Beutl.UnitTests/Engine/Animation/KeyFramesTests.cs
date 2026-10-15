using Beutl.Animation;
using Moq;

namespace Beutl.UnitTests.Engine.Animation;

public class KeyFramesTests
{
    [Test]
    public void Changing_key_time_to_an_existing_time_keeps_the_collection_sorted()
    {
        var animation = new KeyFrameAnimation<float>();
        var first = new KeyFrame<float> { KeyTime = TimeSpan.FromSeconds(1), Value = 10 };
        var middle = new KeyFrame<float> { KeyTime = TimeSpan.FromSeconds(2), Value = 200 };
        var last = new KeyFrame<float> { KeyTime = TimeSpan.FromSeconds(3), Value = 30 };
        animation.KeyFrames.AddSorted(first);
        animation.KeyFrames.AddSorted(middle);
        animation.KeyFrames.AddSorted(last);

        first.KeyTime = last.KeyTime;
        Assert.That(animation.KeyFrames.Select(key => key.KeyTime), Is.Ordered);
        last.KeyTime = TimeSpan.FromSeconds(1);
        Assert.That(animation.KeyFrames, Is.EqualTo(new[] { last, middle, first }));
        Assert.That(animation.Interpolate(TimeSpan.FromSeconds(2)), Is.EqualTo(200));

        last.KeyTime = TimeSpan.FromSeconds(3);
        Assert.That(animation.KeyFrames.Select(key => key.KeyTime), Is.Ordered);
        first.KeyTime = TimeSpan.FromSeconds(1);
        Assert.That(animation.KeyFrames, Is.EqualTo(new[] { first, middle, last }));
        Assert.That(animation.Interpolate(TimeSpan.FromSeconds(2)), Is.EqualTo(200));
    }

    [Test]
    public void AddSorted_ShouldInsertKeyFrameAtCorrectPosition()
    {
        var keyFrames = new KeyFrames();
        var keyFrame1 = new Mock<IKeyFrame>();
        keyFrame1.Setup(k => k.KeyTime).Returns(TimeSpan.FromSeconds(1));
        var keyFrame2 = new Mock<IKeyFrame>();
        keyFrame2.Setup(k => k.KeyTime).Returns(TimeSpan.FromSeconds(3));
        var keyFrame3 = new Mock<IKeyFrame>();
        keyFrame3.Setup(k => k.KeyTime).Returns(TimeSpan.FromSeconds(2));

        keyFrames.AddSorted(keyFrame1.Object);
        keyFrames.AddSorted(keyFrame2.Object);
        keyFrames.AddSorted(keyFrame3.Object);

        Assert.That(keyFrames[0], Is.EqualTo(keyFrame1.Object));
        Assert.That(keyFrames[1], Is.EqualTo(keyFrame3.Object));
        Assert.That(keyFrames[2], Is.EqualTo(keyFrame2.Object));
    }

    [Test]
    public void IndexAtOrCount_ShouldReturnCorrectIndexOrCountForGivenTimeSpan()
    {
        var keyFrames = new KeyFrames();
        var keyFrame1 = new Mock<IKeyFrame>();
        keyFrame1.Setup(k => k.KeyTime).Returns(TimeSpan.FromSeconds(1));
        var keyFrame2 = new Mock<IKeyFrame>();
        keyFrame2.Setup(k => k.KeyTime).Returns(TimeSpan.FromSeconds(3));
        var keyFrame3 = new Mock<IKeyFrame>();
        keyFrame3.Setup(k => k.KeyTime).Returns(TimeSpan.FromSeconds(5));
        keyFrames.AddSorted(keyFrame1.Object);
        keyFrames.AddSorted(keyFrame2.Object);
        keyFrames.AddSorted(keyFrame3.Object);

        Assert.That(keyFrames.IndexAtOrCount(TimeSpan.FromSeconds(0.5)), Is.EqualTo(0));
        Assert.That(keyFrames.IndexAtOrCount(TimeSpan.FromSeconds(2)), Is.EqualTo(1));
        Assert.That(keyFrames.IndexAtOrCount(TimeSpan.FromSeconds(4)), Is.EqualTo(2));
        Assert.That(keyFrames.IndexAtOrCount(TimeSpan.FromSeconds(5)), Is.EqualTo(2));
        Assert.That(keyFrames.IndexAtOrCount(TimeSpan.FromSeconds(6)), Is.EqualTo(3));
    }
}
