using Avalonia;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Avalonia.Threading;

using Beutl.Editor.Components.TimelineTab.ViewModels;

namespace Beutl.Editor.Components.TimelineTab.Views;

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

        Bind(MarginProperty, dataContext.Select(v => v?.Margin ?? Observable.ReturnThenNever((Thickness)default)).Switch());
        Bind(WidthProperty, dataContext.Select(v => v?.Width ?? Observable.ReturnThenNever(0d)).Switch());
        Bind(HeightProperty, dataContext.Select(v => v?.Height ?? Observable.ReturnThenNever(0d)).Switch());

        Bind(FillProperty, dataContext.Select(v => v?.Parent?.Color ?? Observable.ReturnThenNever(Colors.Transparent))
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
            var animation = TimelineSettleAnimation.Create(
                [
                    new Setter(MarginProperty, Margin),
                    new Setter(WidthProperty, Width),
                    new Setter(HeightProperty, Height),
                ],
                [
                    new Setter(MarginProperty, args.Margin),
                    new Setter(WidthProperty, args.Width),
                    new Setter(HeightProperty, args.Height),
                ]);

            await animation.RunAsync(this, token);
        });
    }
}
