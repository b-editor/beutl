using System.Diagnostics.CodeAnalysis;
using Avalonia.VisualTree;
using Avalonia.Xaml.Interactivity;
using Beutl.Editor.Components.GraphEditorTab.ViewModels;

using Path = Avalonia.Controls.Shapes.Path;

namespace Beutl.Editor.Components.GraphEditorTab.Views;

// A behavior on a path that a keyframe of the graph editor draws.
public abstract class GraphEditorPathBehavior : Behavior<Path>
{
    private GraphEditorView? _view;

    protected bool TryGetValues(
        [NotNullWhen(true)] out GraphEditorView? view,
        [NotNullWhen(true)] out GraphEditorViewModel? viewModel,
        [NotNullWhen(true)] out GraphEditorKeyFrameViewModel? keyFrameViewModel)
    {
        view = _view ??= AssociatedObject.FindAncestorOfType<GraphEditorView>();
        viewModel = view?.DataContext as GraphEditorViewModel;
        keyFrameViewModel = AssociatedObject?.DataContext as GraphEditorKeyFrameViewModel;
        return view != null && viewModel != null && keyFrameViewModel != null;
    }
}
