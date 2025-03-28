using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Beutl.Graphics.Transformation;
using Beutl.Services;
using Beutl.ViewModels.Editors;
using FluentAvalonia.UI.Controls;

namespace Beutl.Views.Editors;

public partial class DisplacementMapTransformEditor : UserControl
{
    private static readonly CrossFade s_transition = new(TimeSpan.FromMilliseconds(250));
    private CancellationTokenSource? _lastTransitionCts;

    private static FAMenuFlyout? s_flyout;
    private static EventHandler<RoutedEventArgs>? s_handler;

    public DisplacementMapTransformEditor()
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

    private void Tag_Click(object? sender, RoutedEventArgs e)
    {
        var target = sender as Button ?? expandToggle;

        expandToggle.ContextFlyout?.ShowAt(target);
    }

    private void TransformTypeClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: string type }) return;
        if (DataContext is not DisplacementMapTransformEditorViewModel { IsDisposed: false } viewModel) return;

        viewModel.ChangeType(type switch
        {
            "Translate" => DispMapTransformType.Translate,
            "Rotation" => DispMapTransformType.Rotation,
            "Scale" => DispMapTransformType.Scale,
            _ => DispMapTransformType.Null
        });
    }

    private void SetNullClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DisplacementMapTransformEditorViewModel { IsDisposed: false } viewModel)
        {
            viewModel.SetNull();
        }
    }
}
