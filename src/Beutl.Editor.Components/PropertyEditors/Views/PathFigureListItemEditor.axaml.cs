using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

using Beutl.Editor.Components.PathEditorTab.ViewModels;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Media;
using Beutl.Services;

using FluentAvalonia.UI.Controls;

using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Editor.Components.PropertyEditors.Views;

public partial class PathFigureListItemEditor : UserControl, IListItemEditor
{
    public PathFigureListItemEditor()
    {
        InitializeComponent();
        ExpandTransitionHelper.Attach(reorderHandle, content);
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
        if (DataContext is PathFigureEditorViewModel { IsDisposed: false } viewModel
            && viewModel.GetService<PathEditorViewModel>() is { } pathEditor)
        {
            pathEditor.StartEdit(viewModel);
        }
    }

    private void EditInTabClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PathFigureEditorViewModel { IsDisposed: false } viewModel
            && viewModel.GetService<IEditorContext>() is { } editorContext)
        {
            PathFigure? figure = viewModel.Value.Value;
            var context = PathEditorTabViewModel.FindReusable(editorContext, figure)
                ?? new PathEditorTabViewModel(editorContext);

            // A pinned tab edits through editors of its own, so the figure tells whether it is already open.
            if (context.IsEditing(figure))
            {
                if (context.IsSelected.Value)
                {
                    context.FinishEdit();
                }
            }
            else
            {
                context.StartOrFinishEdit(viewModel);
            }

            editorContext.OpenToolTab(context);
        }
    }

    private void Edit_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PathFigureEditorViewModel { IsDisposed: false } viewModel
            && viewModel.GetService<PathEditorViewModel>() is { } pathEditor
            && sender is ToggleButton btn)
        {
            btn.IsChecked = btn.IsChecked != true;

            if (viewModel.EditingPath.Value)
            {
                pathEditor.StartEdit(viewModel);
            }
            else
            {
                btn.ContextFlyout?.ShowAt(btn);
            }
        }
    }

    private void AddClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PathFigureEditorViewModel { IsDisposed: false } viewModel
            && sender is FAMenuFlyoutItem item)
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
                NotificationService.ShowError(Strings.Error, ex.Message);
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
