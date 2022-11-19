using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

using Beutl.Animation;

namespace Beutl.Views.AnimationVisualizer;

public abstract class AnimationVisualizer<T> : Control
{
    protected AnimationVisualizer(Animation<T> animation)
    {
        Animation = animation;
    }

    protected Animation<T> Animation { get; }

    protected TimeSpan CalculateDuration()
    {
        return Animation.CalculateDuration();
    }
}

public abstract class AnimationSpanVisualizer<T> : Control
{
    protected AnimationSpanVisualizer(Animation<T> animation, AnimationSpan<T> animationSpan)
    {
        Animation = animation;
        AnimationSpan = animationSpan;
    }

    protected Animation<T> Animation { get; }

    protected AnimationSpan<T> AnimationSpan { get; }

    protected TimeSpan CalculateDuration()
    {
        return Animation.CalculateDuration();
    }
}
