using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Beutl.Controls;
using Beutl.Engine;

namespace Beutl.Views;

internal sealed partial class TransformHandlesOverlay
{
    private static readonly IPen s_editingHandleStroke = new ImmutablePen(Brushes.White, 1.5);
    private static readonly IPen s_editingGuideStroke = new ImmutablePen(
        new ImmutableSolidColorBrush(Color.FromArgb(0xD0, 0x48, 0xA9, 0xFF)), 1);

    private IEditingHandleProvider? _editingHandleProvider;
    private IReadOnlyList<EditingHandle> _editingHandles = [];
    private IReadOnlyList<EditingGuideLine> _editingGuides = [];

    // While the handles of an object inside the drawable show, such as an effect's, they are what is edited, so the
    // drawable's own handles are neither drawn nor pressed. A drawable's own editing handles show with them.
    private bool ShowsTransformHandles => _editingHandles.Count == 0 || ReferenceEquals(_editingHandleProvider, _drawable);

    public void UpdateEditingHandles(IEditingHandleProvider? provider, MediaTime time)
    {
        EditingHandle[] handles = provider?.GetEditingHandles(time, _localBounds).ToArray() ?? [];
        EditingGuideLine[] guides = provider?.GetEditingGuides(time, _localBounds).ToArray() ?? [];
        if (ReferenceEquals(_editingHandleProvider, provider) && _editingHandles.SequenceEqual(handles)
            && _editingGuides.SequenceEqual(guides)) return;
        _editingHandleProvider = provider;
        _editingHandles = handles;
        _editingGuides = guides;
        HdrOverlay.InvalidateVisual(this);
    }

    private void ClearEditingHandles()
    {
        _editingHandleProvider = null;
        _editingHandles = [];
        _editingGuides = [];
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
        foreach (EditingGuideLine guide in _editingGuides)
            context.DrawLine(s_editingGuideStroke, LocalToImage(guide.Start.X, guide.Start.Y),
                LocalToImage(guide.End.X, guide.End.Y));

        foreach (EditingHandle handle in _editingHandles)
        {
            Point position = LocalToImage(handle.Position.X, handle.Position.Y);
            var fill = handle.Color is { } color ? new ImmutableSolidColorBrush(color.ToAvaColor()) : s_accentFill;
            context.DrawEllipse(fill, s_editingHandleStroke, position, 4.5, 4.5);
        }
    }
}
