using Beutl.Animation;
using Beutl.Animation.Easings;

namespace Beutl.UnitTests.Engine.Animation;

[TestFixture]
public class SplineEasingHelperTests
{
    [Test]
    public void SplitByT_ReturnsTwoEasings()
    {
        var src = new SplineEasing(0.25f, 0.1f, 0.25f, 1f);

        var (left, right) = SplineEasingHelper.SplitByT(src, 0.5f);

        Assert.That(left, Is.Not.Null);
        Assert.That(right, Is.Not.Null);
    }

    [Test]
    public void SplitByT_ClampsBelowZero()
    {
        var src = new SplineEasing();
        Assert.DoesNotThrow(() => SplineEasingHelper.SplitByT(src, -1f));
    }

    [Test]
    public void SplitByT_ClampsAboveOne()
    {
        var src = new SplineEasing();
        Assert.DoesNotThrow(() => SplineEasingHelper.SplitByT(src, 2f));
    }

    [Test]
    public void SplitByT_LeftAndRightControlPointsValid()
    {
        var src = new SplineEasing(0.42f, 0f, 0.58f, 1f);

        var (left, right) = SplineEasingHelper.SplitByT(src, 0.5f);

        Assert.That(left.X1, Is.InRange(0f, 1f));
        Assert.That(left.X2, Is.InRange(0f, 1f));
        Assert.That(right.X1, Is.InRange(0f, 1f));
        Assert.That(right.X2, Is.InRange(0f, 1f));
    }

    [Test]
    public void Remove_NonNumberType_FallsBackToRemoveAt()
    {
        var animation = new KeyFrameAnimation<bool>();
        animation.KeyFrames.Add(new KeyFrame<bool> { KeyTime = TimeSpan.FromSeconds(0), Value = false });
        animation.KeyFrames.Add(new KeyFrame<bool> { KeyTime = TimeSpan.FromSeconds(1), Value = true });

        SplineEasingHelper.Remove(animation, 1);

        Assert.That(animation.KeyFrames, Has.Count.EqualTo(1));
    }

    [Test]
    public void Move_NonNumberType_OnlyAdjustsKeyTime()
    {
        var animation = new KeyFrameAnimation<bool>();
        var kf = new KeyFrame<bool> { KeyTime = TimeSpan.FromSeconds(1), Value = true };
        animation.KeyFrames.Add(new KeyFrame<bool> { KeyTime = TimeSpan.FromSeconds(0), Value = false });
        animation.KeyFrames.Add(kf);

        SplineEasingHelper.Move(animation, kf, TimeSpan.FromSeconds(2));

        Assert.That(kf.KeyTime, Is.EqualTo(TimeSpan.FromSeconds(2)));
    }
}
