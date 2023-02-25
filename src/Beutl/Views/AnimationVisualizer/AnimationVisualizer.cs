using Avalonia.Controls;

using Beutl.Animation;

namespace Beutl.Views.AnimationVisualizer;

public abstract class AnimationVisualizer<T> : Control
{
    protected AnimationVisualizer(IAnimation<T> animation)
    {
        Animation = animation;
    }

    protected IAnimation<T> Animation { get; }

    protected TimeSpan CalculateDuration()
    {
        return Animation.CalculateDuration();
    }
}

public abstract class AnimationSpanVisualizer<T> : Control
{
    protected AnimationSpanVisualizer(KeyFrameAnimation<T> animation, KeyFrame<T> keyframe)
    {
        Animation = animation;
        KeyFrame = keyframe;
    }

    protected KeyFrameAnimation<T> Animation { get; }

    protected KeyFrame<T> KeyFrame { get; }

    protected T Interpolate(float progress)
    {
        IKeyFrame? prev = Animation.GetPreviousAndNextKeyFrame(KeyFrame).Previous;

        T prevValue = prev is KeyFrame<T> prev2 ? prev2.Value : KeyFrame<T>.s_animator.DefaultValue();
        T nextValue = KeyFrame.Value;

        float ease = KeyFrame.Easing.Ease(progress);
        return KeyFrame<T>.s_animator.Interpolate(ease, prevValue, nextValue);
    }

    protected TimeSpan CalculateDuration()
    {
        return Animation.CalculateDuration();
    }

    protected TimeSpan CalculateKeyFrameLength()
    {
        IKeyFrame? prev = Animation.GetPreviousAndNextKeyFrame(KeyFrame).Previous;
        TimeSpan prevTime = prev?.KeyTime ?? TimeSpan.Zero;
        return KeyFrame.KeyTime - prevTime;
    }
}
