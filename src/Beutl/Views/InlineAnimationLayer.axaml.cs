using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;

using Beutl.ViewModels;

namespace Beutl.Views;

public partial class InlineAnimationLayer : UserControl
{
    public InlineAnimationLayer()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is InlineAnimationLayerViewModel viewModel)
        {
            viewModel.AnimationRequested = async (margin, token) =>
            {
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    var animation = new Avalonia.Animation.Animation
                    {
                        Easing = new SplineEasing(0.1, 0.9, 0.2, 1.0),
                        Duration = TimeSpan.FromSeconds(0.67),
                        FillMode = FillMode.Forward,
                        Children =
                        {
                            new KeyFrame()
                            {
                                Cue = new Cue(0),
                                Setters =
                                {
                                    new Setter(MarginProperty, Margin)
                                }
                            },
                            new KeyFrame()
                            {
                                Cue = new Cue(1),
                                Setters =
                                {
                                    new Setter(MarginProperty, margin)
                                }
                            }
                        }
                    };

                    await animation.RunAsync(this, null, token);
                });
            };
        }
    }
}
