using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

using Beutl.Media;
using Beutl.ViewModels.Editors;

using static Beutl.Views.Editors.PropertiesEditor;

namespace Beutl.Views.Editors;

public sealed partial class PenEditor : UserControl
{
    private static readonly CrossFade s_transition = new(TimeSpan.FromMilliseconds(250));

    private CancellationTokenSource? _lastTransitionCts1;
    private CancellationTokenSource? _lastTransitionCts2;

    public PenEditor()
    {
        Resources["ViewModelToViewConverter"] = ViewModelToViewConverter.Instance;
        InitializeComponent();
        expandToggle.GetObservable(ToggleButton.IsCheckedProperty)
            .Subscribe(async v =>
            {
                _lastTransitionCts1?.Cancel();
                _lastTransitionCts1 = new CancellationTokenSource();
                CancellationToken localToken = _lastTransitionCts1.Token;

                if (v == true)
                {
                    await s_transition.Start(null, content, localToken);
                }
                else
                {
                    await s_transition.Start(content, null, localToken);
                }
            });

        expandMinorProps.GetObservable(ToggleButton.IsCheckedProperty)
            .Subscribe(async v =>
            {
                _lastTransitionCts2?.Cancel();
                _lastTransitionCts2 = new CancellationTokenSource();
                CancellationToken localToken = _lastTransitionCts2.Token;

                if (v == true)
                {
                    await s_transition.Start(null, minorProps, localToken);
                }
                else
                {
                    await s_transition.Start(minorProps, null, localToken);
                }
            });
    }

    private void InitializeClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PenEditorViewModel viewModel)
        {
            viewModel.SetValue(viewModel.Value.Value, new Pen());
            expandToggle.IsChecked = true;
        }
    }

    private void DeleteClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PenEditorViewModel viewModel)
        {
            viewModel.SetValue(viewModel.Value.Value, null);
        }
    }

    private void Menu_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            button.ContextFlyout?.ShowAt(button);
        }
    }
}
