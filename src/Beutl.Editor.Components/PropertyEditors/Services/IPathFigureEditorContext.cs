using Beutl.Media;
using Reactive.Bindings;

namespace Beutl.Editor.Components.PropertyEditors.Services;

public interface IPathFigureEditorContext
{
    ReadOnlyReactiveProperty<PathFigure> Value { get; }

    IGeometryEditorContext? GetParentContext();

    void ExpandForEditing();

    void CollapseEditedOperations();

    void ExpandOperationForSegment(PathSegment segment);

    void InvalidateFrameCache();

    int GetSegmentIndex(PathSegment segment);

    void RemoveSegment(int index);

    void AddSegment(PathSegment segment);
}
