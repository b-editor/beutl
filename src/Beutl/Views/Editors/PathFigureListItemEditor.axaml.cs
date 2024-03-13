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

public partial class PathFigureListItemEditor : UserControl, IListItemEditor
{
    private static readonly CrossFade s_transition = new(TimeSpan.FromMilliseconds(250));

    private CancellationTokenSource? _lastTransitionCts;

    public PathFigureListItemEditor()
    {
        InitializeComponent();
        reorderHandle.GetObservable(ToggleButton.IsCheckedProperty)
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

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        (DataContext as PathFigureEditorViewModel)?.RecreatePreviewPath();
    }

    private void Tag_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
        {
            btn.ContextFlyout?.ShowAt(btn);
        }
    }

    private void EditInFrameClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PathFigureEditorViewModel viewModel
            && viewModel.GetService<EditViewModel>() is { } editViewModel)
        {
            editViewModel?.Player.PathEditor.StartEdit(viewModel);
        }
    }

    private void EditInTabClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PathFigureEditorViewModel viewModel
            && viewModel.GetService<EditViewModel>() is { } editViewModel)
        {
            var context = editViewModel.FindToolTab<PathEditorTabViewModel>()
                ?? new PathEditorTabViewModel(editViewModel);

            // 既に編集中でタブが選択されている場合、編集を終了
            if (context.FigureContext.Value == viewModel)
            {
                if (context.IsSelected.Value)
                {
                    context.StartOrFinishEdit(viewModel);
                }
            }
            else
            {
                context.StartOrFinishEdit(viewModel);
            }

            editViewModel.OpenToolTab(context);
        }
    }

    private void Edit_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PathFigureEditorViewModel viewModel
            && viewModel.GetService<EditViewModel>() is { } editViewModel
            && sender is ToggleButton btn)
        {
            btn.IsChecked = btn.IsChecked != true;

            if (viewModel.EditingPath.Value)
            {
                editViewModel?.Player.PathEditor.StartEdit(viewModel);
            }
            else
            {
                btn.ContextFlyout?.ShowAt(btn);
            }
        }
    }

    private void AddClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PathFigureEditorViewModel viewModel
            && sender is MenuFlyoutItem item)
        {
            try
            {
                Type? type = item.Tag switch
                {
                    "Arc" => typeof(ArcSegment),
                    "Conic" => typeof(ConicSegment),
                    "Cubic" => typeof(CubicBezierSegment),
                    "Line" => typeof(LineSegment),
                    "Quad" => typeof(QuadraticBezierSegment),
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

    public Control? ReorderHandle => reorderHandle;

    public event EventHandler? DeleteRequested;

    private void DeleteClick(object? sender, RoutedEventArgs e)
    {
        DeleteRequested?.Invoke(this, EventArgs.Empty);
    }
}
