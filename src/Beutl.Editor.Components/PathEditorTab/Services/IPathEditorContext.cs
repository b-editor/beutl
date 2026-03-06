using Beutl.Editor.Components.PropertyEditors.Services;
using Beutl.Media;
using Beutl.ProjectSystem;

using Reactive.Bindings;

namespace Beutl.Editor.Components.PathEditorTab.Services;

public interface IPathEditorContext
{
    IEditorContext EditorContext { get; }

    IReadOnlyReactiveProperty<Element?> Element { get; }

    IReactiveProperty<PathSegment?> SelectedOperation { get; }

    IReadOnlyReactiveProperty<PathGeometry?> PathGeometry { get; }

    IReadOnlyReactiveProperty<PathFigure?> PathFigure { get; }

    IReactiveProperty<IPathFigureEditorContext?> FigureContext { get; }

    IReactiveProperty<bool> Symmetry { get; }

    IReactiveProperty<bool> Asymmetry { get; }

    IReactiveProperty<bool> Separately { get; }
}
