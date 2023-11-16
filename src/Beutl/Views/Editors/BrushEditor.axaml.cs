using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

using Beutl.Media;
using Beutl.ViewModels.Editors;

using FluentAvalonia.UI.Controls;

namespace Beutl.Views.Editors;

public sealed partial class BrushEditor : UserControl
{
    private static readonly CrossFade s_transition = new(TimeSpan.FromMilliseconds(250));

    private CancellationTokenSource? _lastTransitionCts;

    public BrushEditor()
    {
        InitializeComponent();
        expandToggle.GetObservable(ToggleButton.IsCheckedProperty)
            .Subscribe(async v =>
            {
                _lastTransitionCts?.Cancel();
                _lastTransitionCts = new CancellationTokenSource();
                CancellationToken localToken = _lastTransitionCts.Token;

                if (v == true)
                {
                    await s_transition.Start(null, content, localToken);
                }
                else
                {
                    await s_transition.Start(content, null, localToken);
                }
            });
    }

    private void Menu_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            button.ContextFlyout?.ShowAt(button);
        }
    }

    private void ChangeBrushType(object? sender, RoutedEventArgs e)
    {
        if (DataContext is BrushEditorViewModel viewModel
            && sender is RadioMenuFlyoutItem { Tag: string tag })
        {
            IBrush? newBrush = tag switch
            {
                "Solid" => new SolidColorBrush(),
                "LinearGradient" => new LinearGradientBrush(),
                "ConicGradient" => new ConicGradientBrush(),
                "RadialGradient" => new RadialGradientBrush(),
                "PerlinNoise" => new PerlinNoiseBrush(),
                _ => null
            };

            viewModel.SetValue(viewModel.Value.Value, newBrush);
            expandToggle.IsChecked = true;
        }
    }
}
