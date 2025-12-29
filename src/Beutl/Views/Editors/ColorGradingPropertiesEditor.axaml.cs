using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Beutl.ProjectSystem;
using Beutl.Services.PrimitiveImpls;
using Beutl.ViewModels;
using Beutl.ViewModels.Editors;
using Beutl.ViewModels.Tools;
using Microsoft.Extensions.DependencyInjection;
using static Beutl.Views.Editors.PropertiesEditor;

namespace Beutl.Views.Editors;

public sealed partial class ColorGradingPropertiesEditor : UserControl
{
    private static readonly CrossFade s_transition = new(TimeSpan.FromMilliseconds(250));
    private CancellationTokenSource? _lastTransitionCts;

    public ColorGradingPropertiesEditor()
    {
        Resources["ViewModelToViewConverter"] = ViewModelToViewConverter.Instance;
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
            context.GetService<EditViewModel>() is { } editViewModel &&
            context.TryGetColorGrading() is { } colorGrading)
        {
            var toolTab = editViewModel.FindToolTab<ColorGradingTabViewModel>() ??
                          new ColorGradingTabViewModel(editViewModel);

            toolTab.Effect.Value = colorGrading;

            editViewModel.OpenToolTab(toolTab);
        }
    }
}
