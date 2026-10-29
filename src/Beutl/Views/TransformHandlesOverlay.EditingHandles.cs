using Avalonia;
using Avalonia.Media;
using Beutl.Controls;
using Beutl.Engine;

namespace Beutl.Views;

internal sealed partial class TransformHandlesOverlay
{
    private static readonly Color DefaultEditingHandleColor = Color.FromUInt32(0xff1e90ff);

    private IEditingHandleProvider? _editingHandleProvider;
    private IReadOnlyList<EditingHandle> _editingHandles = [];

    public void UpdateEditingHandles(IEditingHandleProvider? provider, MediaTime time)
    {
        _editingHandleProvider = provider;
        _editingHandles = provider?.GetEditingHandles(time).ToArray() ?? [];
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
            var fill = new SolidColorBrush(handle.Color?.ToAvaColor() ?? DefaultEditingHandleColor);
            context.DrawEllipse(fill, new Pen(Brushes.White, 1.5), position, 4.5, 4.5);
        }
    }
}
