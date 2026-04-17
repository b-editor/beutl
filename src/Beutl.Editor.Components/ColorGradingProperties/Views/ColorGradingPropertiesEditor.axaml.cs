using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Beutl.Controls.Converters;
using Beutl.Editor.Components.ColorGradingProperties.ViewModels;
using Beutl.Editor.Components.ColorGradingTab.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Editor.Components.ColorGradingProperties.Views;

public sealed partial class ColorGradingPropertiesEditor : UserControl
{
    private static readonly CrossFade s_transition = new(TimeSpan.FromMilliseconds(250));
    private CancellationTokenSource? _lastTransitionCts;

    public ColorGradingPropertiesEditor()
    {
        Resources["ViewModelToViewConverter"] = PropertyEditorContextToViewConverter.Instance;
        InitializeComponent();

        expandProps.GetObservable(ToggleButton.IsCheckedProperty)
            .Subscribe(async v =>
            {
                _lastTransitionCts?.Cancel();
                _lastTransitionCts = new CancellationTokenSource();
                CancellationToken localToken = _lastTransitionCts.Token;

                if (v == true)
                {
                    await s_transition.Start(null, propsItemsControl, localToken);
                }
                else
                {
                    await s_transition.Start(propsItemsControl, null, localToken);
                }
            });
    }

    private void OpenTabClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ColorGradingPropertiesViewModel context &&
            context.GetService<IEditorContext>() is { } editorContext &&
            context.TryGetColorGrading() is { } colorGrading)
        {
            var toolTab = editorContext.FindToolTab<ColorGradingTabViewModel>() ??
                          new ColorGradingTabViewModel(editorContext);

            toolTab.Effect.Value = colorGrading;

            editorContext.OpenToolTab(toolTab);
        }
    }

}
