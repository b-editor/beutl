using Beutl.Animation;
using Beutl.Animation.Easings;

namespace Beutl.UnitTests.TestInfrastructure;

public static class TestAnimations
{
    /// <summary>A linear animation from <paramref name="from"/> at zero to <paramref name="to"/> at <paramref name="duration"/>.</summary>
    public static KeyFrameAnimation<T> Linear<T>(T from, T to, MediaTime duration, bool useGlobalClock = false)
    {
        var animation = new KeyFrameAnimation<T> { UseGlobalClock = useGlobalClock };
        animation.KeyFrames.Add(new KeyFrame<T> { KeyTime = MediaTime.Zero, Value = from, Easing = new LinearEasing() });
        animation.KeyFrames.Add(new KeyFrame<T> { KeyTime = duration, Value = to, Easing = new LinearEasing() });
        return animation;
    }
}
