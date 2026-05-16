using Beutl.Animation;
using Beutl.Engine;
using Beutl.Media;

namespace Beutl.UnitTests.Engine;

internal sealed partial class KeyFrameAnimationTestRoot : EngineObject, IHierarchicalRoot
{
    public event EventHandler<IHierarchical>? DescendantAttached;
    public event EventHandler<IHierarchical>? DescendantDetached;

    public void OnDescendantAttached(IHierarchical descendant)
        => DescendantAttached?.Invoke(this, descendant);

    public void OnDescendantDetached(IHierarchical descendant)
        => DescendantDetached?.Invoke(this, descendant);
}

[TestFixture]
public class KeyFrameAnimationTests
{
    [Test]
    public void ToLocalTime_NoParent_ReturnsInputAsIs()
    {
        var animation = new KeyFrameAnimation<float> { UseGlobalClock = false };

        var input = TimeSpan.FromSeconds(7);
        Assert.That(animation.ToLocalTime(input), Is.EqualTo(input));
    }

    [Test]
    public void ToLocalTime_UseGlobalClockTrue_ReturnsInputAsIs()
    {
        var owner = new KeyFrameAnimationTestRoot
        {
            TimeRange = new TimeRange(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1))
        };
        var animation = new KeyFrameAnimation<float> { UseGlobalClock = true };
        ((IModifiableHierarchical)owner).AddChild(animation);

        var input = TimeSpan.FromSeconds(7);
        Assert.That(animation.ToLocalTime(input), Is.EqualTo(input));
    }

    [Test]
    public void ToLocalTime_UseGlobalClockFalse_SubtractsParentTimeRangeStart()
    {
        var owner = new KeyFrameAnimationTestRoot
        {
            TimeRange = new TimeRange(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1))
        };
        var animation = new KeyFrameAnimation<float> { UseGlobalClock = false };
        ((IModifiableHierarchical)owner).AddChild(animation);

        Assert.That(
            animation.ToLocalTime(TimeSpan.FromSeconds(7)),
            Is.EqualTo(TimeSpan.FromSeconds(2)));
    }

    [Test]
    public void ToLocalTime_DetachingParent_FallsBackToInput()
    {
        var owner = new KeyFrameAnimationTestRoot
        {
            TimeRange = new TimeRange(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1))
        };
        var animation = new KeyFrameAnimation<float> { UseGlobalClock = false };
        ((IModifiableHierarchical)owner).AddChild(animation);
        ((IModifiableHierarchical)owner).RemoveChild(animation);

        var input = TimeSpan.FromSeconds(7);
        Assert.That(animation.ToLocalTime(input), Is.EqualTo(input));
    }
}
