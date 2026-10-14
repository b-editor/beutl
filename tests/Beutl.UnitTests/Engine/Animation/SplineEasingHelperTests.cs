using Beutl.Animation;
using Beutl.Animation.Easings;

namespace Beutl.UnitTests.Engine.Animation;

[TestFixture]
public class SplineEasingHelperTests
{
    // Each half is rescaled to the unit square, so mapping it back must trace the original curve on its side
    // of the split.
    [Test]
    public void SplitByT_HalvesReproduceTheOriginalCurve()
    {
        var src = new SplineEasing(0.42f, 0f, 0.58f, 1f);
        const float t = 0.4f;
        float splitValue = src.Ease(t);

        var (left, right) = SplineEasingHelper.SplitByT(src, t);

        Assert.Multiple(() =>
        {
            foreach (float x in new[] { 0.1f, 0.2f, 0.3f })
            {
                Assert.That(left.Ease(x / t) * splitValue, Is.EqualTo(src.Ease(x)).Within(2e-3), $"left at {x}");
            }

            foreach (float x in new[] { 0.5f, 0.7f, 0.9f })
            {
                Assert.That(
                    splitValue + (right.Ease((x - t) / (1 - t)) * (1 - splitValue)),
                    Is.EqualTo(src.Ease(x)).Within(2e-3),
                    $"right at {x}");
            }
        });
    }

    [TestCase(-1f)]
    [TestCase(2f)]
    public void SplitByT_OutOfRangeT_DoesNotThrow(float t)
    {
        var src = new SplineEasing();
        Assert.DoesNotThrow(() => SplineEasingHelper.SplitByT(src, t));
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
