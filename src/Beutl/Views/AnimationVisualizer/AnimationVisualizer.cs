using Avalonia;
using Avalonia.Controls;

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
        TimeSpan ts = TimeSpan.Zero;
        foreach (AnimationSpan<T> item in Animation.Children.GetMarshal().Value)
        {
            ts += item.Duration;
        }

        return ts;
    }
}
