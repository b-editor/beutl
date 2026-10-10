using Beutl.Media;
using Reactive.Bindings;

namespace Beutl.Editor.Components.PropertyEditors.Services;

public interface IGeometryEditorContext : IServiceProvider
{
    ReadOnlyReactiveProperty<Geometry?> Value { get; }

    void ExpandForEditing();

    IPathFigureEditorContext? FindPathFigureContext(PathFigure figure);

    /// <summary>
    /// Creates another editor for the same geometry, which no view shows and which outlives this one.
    /// </summary>
    /// <param name="services">Accepted by the new editor in place of this editor's parent.</param>
    /// <returns>
    /// The new editor, which the caller disposes through <see cref="IDisposable"/>, or
    /// <see langword="null"/> when none can be created.
    /// </returns>
    IGeometryEditorContext? CreateDetached(IPropertyEditorContextVisitor services) => null;
}
