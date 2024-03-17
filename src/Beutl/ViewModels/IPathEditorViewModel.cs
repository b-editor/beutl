using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.ViewModels.Editors;

using Reactive.Bindings;

namespace Beutl.ViewModels;

public interface IPathEditorViewModel
{
    EditViewModel EditViewModel { get; }

    IReadOnlyReactiveProperty<Element?> Element { get; }

    IReactiveProperty<PathSegment?> SelectedOperation { get; }

    IReadOnlyReactiveProperty<PathGeometry?> PathGeometry { get; }

    IReadOnlyReactiveProperty<PathFigure?> PathFigure { get; }

    IReactiveProperty<PathFigureEditorViewModel?> FigureContext { get; }

    IReactiveProperty<bool> Symmetry { get; }

    IReactiveProperty<bool> Asymmetry { get; }

    IReactiveProperty<bool> Separately { get; }
}
