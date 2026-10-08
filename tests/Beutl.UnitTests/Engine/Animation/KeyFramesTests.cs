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
        animation.KeyFrames.Add(first, out _);
        animation.KeyFrames.Add(middle, out _);
        animation.KeyFrames.Add(last, out _);

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
    public void Add_ShouldInsertKeyFrameAtCorrectPositionAndReturnIndex()
    {
        var keyFrames = new KeyFrames();
        var keyFrame1 = new Mock<IKeyFrame>();
        keyFrame1.Setup(k => k.KeyTime).Returns(TimeSpan.FromSeconds(1));
        var keyFrame2 = new Mock<IKeyFrame>();
        keyFrame2.Setup(k => k.KeyTime).Returns(TimeSpan.FromSeconds(3));
        var keyFrame3 = new Mock<IKeyFrame>();
        keyFrame3.Setup(k => k.KeyTime).Returns(TimeSpan.FromSeconds(2));

        keyFrames.Add(keyFrame1.Object, out int index1);
        keyFrames.Add(keyFrame2.Object, out int index2);
        keyFrames.Add(keyFrame3.Object, out int index3);

        Assert.That(index1, Is.EqualTo(0));
        Assert.That(index2, Is.EqualTo(1));
        Assert.That(index3, Is.EqualTo(1));
        Assert.That(keyFrames[0], Is.EqualTo(keyFrame1.Object));
        Assert.That(keyFrames[1], Is.EqualTo(keyFrame3.Object));
        Assert.That(keyFrames[2], Is.EqualTo(keyFrame2.Object));
    }

    [Test]
    public void IndexAt_ShouldReturnCorrectIndexForGivenTimeSpan()
    {
        var keyFrames = new KeyFrames();
        var keyFrame1 = new Mock<IKeyFrame>();
        keyFrame1.Setup(k => k.KeyTime).Returns(TimeSpan.FromSeconds(1));
        var keyFrame2 = new Mock<IKeyFrame>();
        keyFrame2.Setup(k => k.KeyTime).Returns(TimeSpan.FromSeconds(3));
        var keyFrame3 = new Mock<IKeyFrame>();
        keyFrame3.Setup(k => k.KeyTime).Returns(TimeSpan.FromSeconds(5));
        keyFrames.Add(keyFrame1.Object, out _);
        keyFrames.Add(keyFrame2.Object, out _);
        keyFrames.Add(keyFrame3.Object, out _);

        Assert.That(keyFrames.IndexAt(TimeSpan.FromSeconds(0.5)), Is.EqualTo(0));
        Assert.That(keyFrames.IndexAt(TimeSpan.FromSeconds(2)), Is.EqualTo(1));
        Assert.That(keyFrames.IndexAt(TimeSpan.FromSeconds(4)), Is.EqualTo(2));
        Assert.That(keyFrames.IndexAt(TimeSpan.FromSeconds(5)), Is.EqualTo(2));
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
        keyFrames.Add(keyFrame1.Object, out _);
        keyFrames.Add(keyFrame2.Object, out _);
        keyFrames.Add(keyFrame3.Object, out _);

        Assert.That(keyFrames.IndexAtOrCount(TimeSpan.FromSeconds(0.5)), Is.EqualTo(0));
        Assert.That(keyFrames.IndexAtOrCount(TimeSpan.FromSeconds(2)), Is.EqualTo(1));
        Assert.That(keyFrames.IndexAtOrCount(TimeSpan.FromSeconds(4)), Is.EqualTo(2));
        Assert.That(keyFrames.IndexAtOrCount(TimeSpan.FromSeconds(5)), Is.EqualTo(2));
        Assert.That(keyFrames.IndexAtOrCount(TimeSpan.FromSeconds(6)), Is.EqualTo(3));
    }
}
