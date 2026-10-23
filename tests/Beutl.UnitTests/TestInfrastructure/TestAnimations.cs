using Beutl.Animation;
using Beutl.Animation.Easings;

namespace Beutl.UnitTests.TestInfrastructure;

public static class TestAnimations
{
    /// <summary>A linear animation from <paramref name="from"/> at zero to <paramref name="to"/> at <paramref name="duration"/>.</summary>
    public static KeyFrameAnimation<T> Linear<T>(T from, T to, MediaTime duration, bool useGlobalClock = false)
    {
        var animation = new KeyFrameAnimation<T> { UseGlobalClock = { Value = useGlobalClock } };
        animation.KeyFrames.Add(new KeyFrame<T> { KeyTime = { Value = MediaTime.Zero }, Value = { Value = from }, Easing = { Value = new LinearEasing() } });
        animation.KeyFrames.Add(new KeyFrame<T> { KeyTime = { Value = duration }, Value = { Value = to }, Easing = { Value = new LinearEasing() } });
        return animation;
    }
}
