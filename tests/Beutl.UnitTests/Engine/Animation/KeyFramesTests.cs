using Beutl.Animation;
using Moq;

namespace Beutl.UnitTests.Engine.Animation;

public class KeyFramesTests
{

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
