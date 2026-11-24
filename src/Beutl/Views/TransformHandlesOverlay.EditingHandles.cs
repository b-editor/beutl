using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Beutl.Controls;
using Beutl.Engine;
using Beutl.Graphics.Effects;

namespace Beutl.Views;

internal sealed partial class TransformHandlesOverlay
{
    private static readonly IPen s_editingHandleStroke = new ImmutablePen(Brushes.White, 1.5);
    private static readonly IPen s_editingGuideStroke = new ImmutablePen(
        new ImmutableSolidColorBrush(Color.FromArgb(0xD0, 0x48, 0xA9, 0xFF)), 1);
    private static readonly IBrush s_pinFill = new ImmutableSolidColorBrush(Color.FromRgb(26, 51, 84));
    private static readonly Typeface s_pinTypeface = new(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);

    private IEditingHandleProvider? _editingHandleProvider;
    private IReadOnlyList<EditingHandle> _editingHandles = [];
    private IReadOnlyList<EditingGuideLine> _editingGuides = [];
    private int[] _pinNumbers = [];
    private string? _selectedPuppetPinId;
    private double _editingHandleZoom = 1;

    // While the handles of an object inside the drawable show, such as an effect's, they are what is edited, so the
    // drawable's own handles are neither drawn nor pressed. A drawable's own editing handles show with them.
    private bool ShowsTransformHandles => _editingHandleProvider is not Puppet
        && (_editingHandles.Count == 0 || ReferenceEquals(_editingHandleProvider, _drawable));

    public void UpdateEditingHandles(IEditingHandleProvider? provider, MediaTime time)
    {
        EditingHandle[] handles = provider?.GetEditingHandles(time, _localBounds).ToArray() ?? [];
        EditingGuideLine[] guides = provider?.GetEditingGuides(time, _localBounds).ToArray() ?? [];
        if (ReferenceEquals(_editingHandleProvider, provider) && _editingHandles.SequenceEqual(handles)
            && _editingGuides.SequenceEqual(guides)) return;
        _editingHandleProvider = provider;
        _editingHandles = handles;
        _editingGuides = guides;
        _pinNumbers = [];
        if (provider is Puppet puppet)
        {
            _pinNumbers = new int[handles.Length];
            int handleIndex = 0;
            for (int i = 0; i < puppet.Pins.Count && handleIndex < handles.Length; i++)
            {
                if (puppet.Pins[i] is { } pin && handles[handleIndex].Id == pin.Id.Value.ToString())
                    _pinNumbers[handleIndex++] = i + 1;
            }
        }
        HdrOverlay.InvalidateVisual(this);
    }

    public void SetSelectedPuppetPin(string? id)
    {
        if (_selectedPuppetPinId == id) return;

        _selectedPuppetPinId = id;
        if (_editingHandleProvider is Puppet) HdrOverlay.InvalidateVisual(this);
    }

    public void SetEditingHandleZoom(double scale)
    {
        double zoom = double.IsFinite(scale) && scale > 0 ? scale : 1;
        if (_editingHandleZoom == zoom) return;

        _editingHandleZoom = zoom;
        if (_editingHandleProvider is Puppet) HdrOverlay.InvalidateVisual(this);
    }

    private void ClearEditingHandles()
    {
        _editingHandleProvider = null;
        _editingHandles = [];
        _editingGuides = [];
        _pinNumbers = [];
    }

    public (IEditingHandleProvider Provider, EditingHandle Handle)? HitTestEditingHandle(Point point)
    {
        if (!HasShape || _editingHandleProvider is null) return null;
        EditingHandle? nearest = null;
        bool puppet = _editingHandleProvider is Puppet;
        double unit = 1 / _editingHandleZoom;
        double distance = puppet ? 13 * unit : 8;
        for (int i = 0; i < _editingHandles.Count; i++)
        {
            EditingHandle handle = _editingHandles[i];
            double candidate = Point.Distance(point, LocalToImage(handle.Position.X, handle.Position.Y));
            if (puppet && handle.Id == _selectedPuppetPinId
                && candidate <= PinRadius(PinLabel(i, true, unit), true, unit) + 1.75 * unit)
                return (_editingHandleProvider, handle);

            if (candidate <= distance)
            {
                nearest = handle;
                distance = candidate;
            }
        }
        return nearest is { } found ? (_editingHandleProvider, found) : null;
    }

    private void RenderEditingHandles(DrawingContext context)
    {
        if (_editingHandleProvider is Puppet)
        {
            RenderPuppetPins(context);
            return;
        }

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

    private void RenderPuppetPins(DrawingContext context)
    {
        double unit = 1 / _editingHandleZoom;
        var normalStroke = new ImmutablePen(Brushes.White, 1.25 * unit);
        var selectedStroke = new ImmutablePen(s_accentFill, 2 * unit);
        int selected = -1;
        for (int i = 0; i < _editingHandles.Count; i++)
        {
            if (_editingHandles[i].Id == _selectedPuppetPinId)
                selected = i;
            else
                DrawPin(i, false);
        }
        if (selected >= 0) DrawPin(selected, true);

        void DrawPin(int index, bool active)
        {
            EditingHandle handle = _editingHandles[index];
            Point position = LocalToImage(handle.Position.X, handle.Position.Y);
            FormattedText text = PinLabel(index, active, unit);
            double radius = PinRadius(text, active, unit);
            if (active)
                context.DrawEllipse(Brushes.Black, null, position, radius + 1.75 * unit, radius + 1.75 * unit);
            context.DrawEllipse(active ? Brushes.White : s_pinFill, active ? selectedStroke : normalStroke,
                position, radius, radius);
            context.DrawText(text, new Point(position.X - text.Width / 2, position.Y - text.Height / 2));
        }
    }

    private FormattedText PinLabel(int index, bool active, double unit)
        => new(_pinNumbers[index].ToString(CultureInfo.CurrentCulture), CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, s_pinTypeface, 10 * unit, active ? s_pinFill : Brushes.White);

    private static double PinRadius(FormattedText text, bool active, double unit)
        => Math.Max((active ? 10 : 8.5) * unit, text.Width / 2 + 2.5 * unit);
}
