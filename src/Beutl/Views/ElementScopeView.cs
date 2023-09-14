using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Avalonia.Threading;

using Beutl.ViewModels;

namespace Beutl.Views;

public sealed class ElementScopeView : Rectangle
{
    private ElementScopeViewModel? _viewModel;

    static ElementScopeView()
    {
        HorizontalAlignmentProperty.OverrideDefaultValue<ElementScopeView>(HorizontalAlignment.Left);
        VerticalAlignmentProperty.OverrideDefaultValue<ElementScopeView>(VerticalAlignment.Top);
        ZIndexProperty.OverrideDefaultValue<ElementScopeView>(-1);
    }

    public ElementScopeView()
    {
        IObservable<ElementScopeViewModel?> dataContext = this.GetObservable(DataContextProperty)
            .Select(v => v as ElementScopeViewModel);

        Bind(MarginProperty, dataContext.Select(v => v?.Margin ?? Observable.Return((Thickness)default)).Switch());
        Bind(WidthProperty, dataContext.Select(v => v?.Width ?? Observable.Return(0d)).Switch());
        Bind(HeightProperty, dataContext.Select(v => v?.Height ?? Observable.Return(0d)).Switch());

        Bind(FillProperty, dataContext.Select(v => v?.Parent?.Color ?? Observable.Return(Colors.Transparent))
            .Switch()
            .Select(v => new ImmutableSolidColorBrush(v, 0.1)));
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ElementScopeViewModel viewModel)
        {
            _viewModel = viewModel;
            _viewModel.AnimationRequested = OnAnimationRequested;
        }
        else if (_viewModel != null)
        {
            _viewModel.AnimationRequested = (_, _) => Task.CompletedTask;
        }
    }

    private async Task OnAnimationRequested((Thickness Margin, double Width, double Height) args, CancellationToken token)
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var animation = new Avalonia.Animation.Animation
            {
                Easing = new SplineEasing(0.1, 0.9, 0.2, 1.0),
                Duration = TimeSpan.FromSeconds(0.25),
                FillMode = FillMode.Forward,
                Children =
                {
                    new KeyFrame()
                    {
                        Cue = new Cue(0),
                        Setters =
                        {
                            new Setter(MarginProperty, Margin),
                            new Setter(WidthProperty, Width),
                            new Setter(HeightProperty, Height),
                        }
                    },
                    new KeyFrame()
                    {
                        Cue = new Cue(1),
                        Setters =
                        {
                            new Setter(MarginProperty, args.Margin),
                            new Setter(WidthProperty, args.Width),
                            new Setter(HeightProperty, args.Height),
                        }
                    }
                }
            };

            await animation.RunAsync(this, token);
        });
    }
}
