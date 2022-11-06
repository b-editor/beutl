using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Styling;
using Avalonia.VisualTree;

using FluentAvalonia.UI.Media.Animation;

namespace BeUtl.Pages.ExtensionsPages;

public sealed class FadeNavigationTransitionInfo : NavigationTransitionInfo
{
    public async override void RunAnimation(Animatable ctrl)
    {
        var animation = new Avalonia.Animation.Animation
        {
            Easing = new SplineEasing(0.1, 0.9, 0.2, 1.0),
            Children =
            {
                new KeyFrame
                {
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 0.0)
                    },
                    Cue = new Cue(0d)
                },
                new KeyFrame
                {
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 1d)
                    },
                    Cue = new Cue(1d)
                }
            },
            Duration = TimeSpan.FromSeconds(0.67),
            FillMode = FillMode.Forward
        };

        await animation.RunAsync(ctrl, null);

        (ctrl as IVisual)!.Opacity = 1;
    }
}
