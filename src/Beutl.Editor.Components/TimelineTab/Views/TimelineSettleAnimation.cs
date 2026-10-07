using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Styling;

namespace Beutl.Editor.Components.TimelineTab.Views;

// Clips, inline layers, scopes and the scroll position all settle on the same curve.
internal static class TimelineSettleAnimation
{
    public static Avalonia.Animation.Animation Create(Setter[] from, Setter[] to)
    {
        return Create(TimeSpan.FromSeconds(0.25), FillMode.Forward, from, to);
    }

    public static Avalonia.Animation.Animation Create(TimeSpan duration, FillMode fillMode, Setter[] from, Setter[] to)
    {
        var start = new KeyFrame { Cue = new Cue(0) };
        foreach (Setter setter in from)
        {
            start.Setters.Add(setter);
        }

        var end = new KeyFrame { Cue = new Cue(1) };
        foreach (Setter setter in to)
        {
            end.Setters.Add(setter);
        }

        return new Avalonia.Animation.Animation
        {
            Easing = new SplineEasing(0.1, 0.9, 0.2, 1.0),
            Duration = duration,
            FillMode = fillMode,
            Children = { start, end }
        };
    }
}
