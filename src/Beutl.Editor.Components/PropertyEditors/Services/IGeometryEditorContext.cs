using Beutl.Media;
using Reactive.Bindings;

namespace Beutl.Editor.Components.PropertyEditors.Services;

public interface IGeometryEditorContext : IServiceProvider
{
    ReadOnlyReactiveProperty<Geometry?> Value { get; }

    void ExpandForEditing();

    IPathFigureEditorContext? FindPathFigureContext(PathFigure figure);
}
