using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

using Beutl.Media;
using Beutl.Services;
using Beutl.ViewModels;
using Beutl.ViewModels.Editors;

using FluentAvalonia.UI.Controls;

using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Views.Editors;

public partial class GeometryEditor : UserControl
{
    private static readonly CrossFade s_transition = new(TimeSpan.FromMilliseconds(250));

    private CancellationTokenSource? _lastTransitionCts;

    public GeometryEditor()
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
        if (DataContext is GeometryEditorViewModel viewModel)
        {
            if (viewModel.IsGroup.Value)
            {
                if (sender is Button btn)
                {
                    btn.ContextFlyout?.ShowAt(btn);
                }
            }
            else
            {
                //expandToggle.ContextFlyout?.ShowAt(expandToggle);
            }
        }
    }

    private void Edit_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is GeometryEditorViewModel viewModel)
        {
            EditViewModel? editViewModel = viewModel.GetService<EditViewModel>();
            editViewModel?.Player.PathEditor.StartEdit(viewModel);
        }
    }

    private void AddClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is GeometryEditorViewModel viewModel
            && sender is MenuFlyoutItem item)
        {
            if (viewModel.IsGroup.Value)
            {
                try
                {
                    Type? type = item.Tag switch
                    {
                        "Arc" => typeof(ArcOperation),
                        "Close" => typeof(CloseOperation),
                        "Conic" => typeof(ConicOperation),
                        "CubicBezier" => typeof(CubicBezierOperation),
                        "Line" => typeof(LineOperation),
                        "Move" => typeof(MoveOperation),
                        "QuadraticBezier" => typeof(QuadraticBezierOperation),
                        _ => null,
                    };
                    if (type != null)
                    {
                        viewModel.AddItem(type);
                    }
                }
                catch (Exception ex)
                {
                    NotificationService.ShowError("Error", ex.Message);
                }
            }
        }
    }

    private void ChangeFilterTypeClick(object? sender, RoutedEventArgs e)
    {
    }

    private void SetNullClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is GeometryEditorViewModel viewModel)
        {
            viewModel.SetNull();
        }
    }
}
