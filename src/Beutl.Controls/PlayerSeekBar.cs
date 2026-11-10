using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Immutable;

using Beutl.Configuration;

namespace Beutl.Controls;

/// <summary>
/// The player's seek bar, whose value, minimum and maximum are in seconds. It draws its track, with a mark for each
/// marker, under the thumb. Its tip tells the time under the pointer, and over a mark the marker's name and time, and a
/// press on a mark moves to the marker's time exactly.
/// </summary>
public sealed class PlayerSeekBar : Slider
{
    public static readonly StyledProperty<IReadOnlyList<PlayerMarkerEntry>?> MarkersProperty =
        AvaloniaProperty.Register<PlayerSeekBar, IReadOnlyList<PlayerMarkerEntry>?>(nameof(Markers));

    public static readonly StyledProperty<Rational> FrameRateProperty =
        AvaloniaProperty.Register<PlayerSeekBar, Rational>(
            nameof(FrameRate), new Rational(30), validate: rate => rate is { Numerator: > 0, Denominator: > 0 });

    public static readonly StyledProperty<TimeDisplayFormat> TimeDisplayFormatProperty =
        AvaloniaProperty.Register<PlayerSeekBar, TimeDisplayFormat>(nameof(TimeDisplayFormat));

    private const double TrackThickness = 4;
    private const double MarkWidth = 2;
    private const double MarkHeight = 8;

    // A pointer no farther than this from a mark's center points at its marker.
    private const double MarkReach = 4;

    private readonly TextBlock _tipName = new() { FontWeight = FontWeight.SemiBold, IsVisible = false };
    private readonly TextBlock _tipTime = new() { FontFeatures = new FontFeatureCollection { FontFeature.Parse("+tnum") } };
    private Mark[] _marks = [];
    private PlayerMarkerEntry? _heldMarker;
    private double? _pointerX;
    private bool _reopenTip;

    static PlayerSeekBar()
    {
        AffectsRender<PlayerSeekBar>(
            ValueProperty, MinimumProperty, MaximumProperty, MarkersProperty, BackgroundProperty, ForegroundProperty);
    }

    public PlayerSeekBar()
    {
        // The name goes above the time, so the time stays where it was when the pointer reaches a mark.
        ToolTip.SetTip(this, new StackPanel { Children = { _tipName, _tipTime } });
        ToolTip.AddToolTipOpeningHandler(this, OnToolTipOpening);

        // The slider has moved the value to the pointer by the time these run.
        AddHandler(PointerPressedEvent, OnPointerPressedOnBar, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerMovedEvent, OnPointerMovedOnBar, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnPointerReleasedOnBar, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    /// <summary>Gets or sets the markers to mark on the bar. Those without a time, or outside the range, get none.</summary>
    public IReadOnlyList<PlayerMarkerEntry>? Markers
    {
        get => GetValue(MarkersProperty);
        set => SetValue(MarkersProperty, value);
    }

    /// <summary>Gets or sets the rate that the tip counts frames at. It must be positive.</summary>
    public Rational FrameRate
    {
        get => GetValue(FrameRateProperty);
        set => SetValue(FrameRateProperty, value);
    }

    /// <summary>Gets or sets how the tip writes times, which is as the player writes the current time.</summary>
    public TimeDisplayFormat TimeDisplayFormat
    {
        get => GetValue(TimeDisplayFormatProperty);
        set => SetValue(TimeDisplayFormatProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Track is not { } track)
            return;

        Rect box = track.Bounds;
        var rail = new Rect(box.X, box.Center.Y - (TrackThickness / 2), box.Width, TrackThickness);
        context.DrawRectangle(Background, null, new RoundedRect(rail, TrackThickness / 2));
        if (!TryGetX(Value, out double thumbX))
            return;

        // The played part ends under the middle of the thumb.
        context.DrawRectangle(Foreground, null, new RoundedRect(rail.WithWidth(thumbX - box.X), TrackThickness / 2));

        // Whole device pixels keep a mark this thin sharp.
        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        double markTop = box.Center.Y - (MarkHeight / 2);
        foreach (Mark mark in _marks)
        {
            if (TryGetMarkX(mark, out double x))
            {
                double left = Math.Round((x - (MarkWidth / 2)) * scaling) / scaling;
                context.DrawRectangle(mark.Brush, null, new RoundedRect(new Rect(left, markTop, MarkWidth, MarkHeight), MarkWidth / 2));
            }
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MarkersProperty)
        {
            IReadOnlyList<PlayerMarkerEntry> entries = change.GetNewValue<IReadOnlyList<PlayerMarkerEntry>?>() ?? [];
            _marks = [.. entries
                .Where(entry => entry.Time.HasValue)
                .Select(entry => new Mark(entry, entry.Time!.Value.ToSeconds(), new ImmutableSolidColorBrush(entry.Color)))];
            _heldMarker = null;
            RefreshTip();
        }
        else if (change.Property == MinimumProperty
            || change.Property == MaximumProperty
            || change.Property == FrameRateProperty
            || change.Property == TimeDisplayFormatProperty)
        {
            RefreshTip();
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        // A press hands the pointer to the part pressed, and the bar hears the pointer leave then and during the drag.
        // Only a pointer that nothing holds has left the bar.
        if (e.Pointer.Captured is null)
        {
            _pointerX = null;
            _reopenTip = false;
        }
    }

    // Where the thumb's center lies at a value. The thumb stays on the track, so its center runs half its width
    // inside either end. Slider maps a press back to a value the same way.
    private bool TryGetX(double value, out double x)
    {
        x = 0;
        if (Track is not { } track)
            return false;

        double thumb = track.Thumb?.Bounds.Width ?? 0;
        double length = track.Bounds.Width - thumb;
        double range = Maximum - Minimum;
        if (!(length > 0) || !(range > 0))
            return false;

        x = track.Bounds.X + (thumb / 2) + (length * (value - Minimum) / range);
        return true;
    }

    private bool TryGetMarkX(Mark mark, out double x)
    {
        x = 0;
        return mark.Value >= Minimum && mark.Value <= Maximum && TryGetX(mark.Value, out x);
    }

    // The value a press at x moves to.
    private double ValueAt(double x)
    {
        if (Track is not { } track)
            return Value;

        double thumb = track.Thumb?.Bounds.Width ?? 0;
        double length = track.Bounds.Width - thumb;
        if (!(length > 0))
            return Minimum;

        double ratio = Math.Clamp((x - track.Bounds.X - (thumb / 2)) / length, 0, 1);
        return Minimum + (ratio * (Maximum - Minimum));
    }

    // The marker that a pointer at x points at: the one whose mark is nearest, within reach.
    private PlayerMarkerEntry? FindMarker(double x)
    {
        PlayerMarkerEntry? found = null;
        double nearest = double.PositiveInfinity;
        foreach (Mark mark in _marks)
        {
            if (!TryGetMarkX(mark, out double markX))
                continue;

            double distance = Math.Abs(markX - x);
            if (distance <= MarkReach && distance < nearest)
            {
                found = mark.Entry;
                nearest = distance;
            }
        }

        return found;
    }

    private bool IsWithinReach(PlayerMarkerEntry marker, double x)
    {
        foreach (Mark mark in _marks)
        {
            if (mark.Entry == marker)
                return TryGetMarkX(mark, out double markX) && Math.Abs(markX - x) <= MarkReach;
        }

        return false;
    }

    // The tip stands over the pointer, or over the mark it points at, and tells the time there.
    private void UpdateTip(double x)
    {
        PlayerMarkerEntry? marker = FindMarker(x);
        double value = marker?.Time?.ToSeconds() ?? ValueAt(x);
        if (!double.IsFinite(value))
            return;

        _tipName.Text = marker?.Name;
        _tipName.IsVisible = !string.IsNullOrEmpty(marker?.Name);
        _tipTime.Text = TimeDisplayFormatter.Format(marker?.Time ?? MediaTime.FromSeconds(value), FrameRate, TimeDisplayFormat);
        double anchor = marker != null && TryGetX(value, out double markX) ? markX : x;
        ToolTip.SetHorizontalOffset(this, anchor - (Bounds.Width / 2));
    }

    private void RefreshTip()
    {
        if (_pointerX is { } x)
            UpdateTip(x);
    }

    private void OnPointerPressedOnBar(object? sender, PointerPressedEventArgs e)
    {
        // The tooltip service closes the tip on every press, and opens it again only when the pointer comes back over
        // the bar.
        _reopenTip = true;
        PointerPoint point = e.GetCurrentPoint(this);
        if (point.Properties.IsLeftButtonPressed && FindMarker(point.Position.X) is { Time: { } time } marker)
        {
            _heldMarker = marker;
            SetCurrentValue(ValueProperty, time.ToSeconds());
        }
    }

    private void OnPointerMovedOnBar(object? sender, PointerEventArgs e)
    {
        PointerPoint point = e.GetCurrentPoint(this);
        double x = point.Position.X;
        if (point.Properties.IsLeftButtonPressed)
        {
            // A press on a mark keeps its marker's time until the pointer leaves the mark, so a click that slips a
            // pixel still lands on the marker.
            if (_heldMarker is { Time: { } time } held && IsDragging && IsWithinReach(held, x))
                SetCurrentValue(ValueProperty, time.ToSeconds());
            else
                _heldMarker = null;

            return;
        }

        _heldMarker = null;
        _pointerX = x;
        UpdateTip(x);
        if (_reopenTip)
        {
            _reopenTip = false;
            ToolTip.SetIsOpen(this, true);
        }
    }

    private void OnPointerReleasedOnBar(object? sender, PointerReleasedEventArgs e)
    {
        _heldMarker = null;
    }

    // The tip waits while the bar is dragged, when the frame and the current time follow the pointer, and while
    // another control holds the pointer, when the bar does not hear where it is. It opens at the next move instead.
    private void OnToolTipOpening(object? sender, CancelRoutedEventArgs e)
    {
        if (IsDragging || _pointerX is null)
        {
            e.Cancel = true;
            _reopenTip = true;
        }
    }

    private readonly record struct Mark(PlayerMarkerEntry Entry, double Value, IImmutableBrush Brush);
}
