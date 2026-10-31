using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Beutl.Controls;
using Beutl.Engine;

namespace Beutl.Views;

internal sealed partial class TransformHandlesOverlay
{
    private static readonly IPen s_editingHandleStroke = new ImmutablePen(Brushes.White, 1.5);

    private IEditingHandleProvider? _editingHandleProvider;
    private IReadOnlyList<EditingHandle> _editingHandles = [];

    public void UpdateEditingHandles(IEditingHandleProvider? provider, MediaTime time)
    {
        EditingHandle[] handles = provider?.GetEditingHandles(time).ToArray() ?? [];
        if (ReferenceEquals(_editingHandleProvider, provider) && _editingHandles.SequenceEqual(handles)) return;
        _editingHandleProvider = provider;
        _editingHandles = handles;
        InvalidateVisual();
    }

    private void ClearEditingHandles()
    {
        _editingHandleProvider = null;
        _editingHandles = [];
    }

    public (IEditingHandleProvider Provider, EditingHandle Handle)? HitTestEditingHandle(Point point)
    {
        if (!HasShape || _editingHandleProvider is null) return null;
        EditingHandle? nearest = null;
        double distance = 8;
        foreach (EditingHandle handle in _editingHandles)
        {
            double candidate = Point.Distance(point, LocalToImage(handle.Position.X, handle.Position.Y));
            if (candidate <= distance) { nearest = handle; distance = candidate; }
        }
        return nearest is { } found ? (_editingHandleProvider, found) : null;
    }

    private void RenderEditingHandles(DrawingContext context)
    {
        foreach (EditingHandle handle in _editingHandles)
        {
            Point position = LocalToImage(handle.Position.X, handle.Position.Y);
            var fill = handle.Color is { } color ? new ImmutableSolidColorBrush(color.ToAvaColor()) : s_accentFill;
            context.DrawEllipse(fill, s_editingHandleStroke, position, 4.5, 4.5);
        }
    }
}
