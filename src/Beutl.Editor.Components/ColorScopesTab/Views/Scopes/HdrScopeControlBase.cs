using Avalonia;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Beutl.Editor.Components.ColorScopesTab.Views.Scopes;

public abstract class HdrScopeControlBase : ScopeControlBase
{
    public static readonly DirectProperty<HdrScopeControlBase, float> HdrRangeProperty =
        AvaloniaProperty.RegisterDirect<HdrScopeControlBase, float>(
            nameof(HdrRange), o => o.HdrRange, (o, v) => o.HdrRange = v, 1.0f);

    private float _hdrRange = 1.0f;

    // Drag state
    private bool _isDragging;
    private Point _dragStartPosition;
    private float _dragStartHdrRange;
    private Cursor? _previousCursor;

    // Overlay state
    private string? _overlayText;
    private CancellationTokenSource? _overlayHideCts;

    private const float DragSensitivity = 1.005f;
    private const float WheelSensitivity = 1.15f;

    static HdrScopeControlBase()
    {
        AffectsRender<HdrScopeControlBase>(HdrRangeProperty);
        HdrRangeProperty.Changed.AddClassHandler<HdrScopeControlBase>((o, _) => o.Refresh());
    }

    public float HdrRange
    {
        get => _hdrRange;
        set => SetAndRaise(HdrRangeProperty, ref _hdrRange, Math.Max(value, 0.01f));
    }

    protected abstract Orientation DragAxis { get; }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        PointerPoint point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
            return;

        if (e.ClickCount == 2)
        {
            HdrRange = 1.0f;
            ShowOverlay();
            e.Handled = true;
            return;
        }

        _dragStartPosition = point.Position;
        _dragStartHdrRange = HdrRange;
        _isDragging = true;

        e.Pointer.Capture(this);

        _previousCursor = Cursor;
        Cursor = DragAxis == Orientation.Vertical
            ? new Cursor(StandardCursorType.SizeNorthSouth)
            : new Cursor(StandardCursorType.SizeWestEast);

        ShowOverlay();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (!_isDragging)
            return;

        Point current = e.GetPosition(this);
        float delta = DragAxis == Orientation.Vertical
            ? -(float)(current.Y - _dragStartPosition.Y)
            : (float)(current.X - _dragStartPosition.X);

        HdrRange = _dragStartHdrRange * MathF.Pow(DragSensitivity, delta);
        UpdateOverlayText();
        InvalidateVisual();

        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (!_isDragging)
            return;

        _isDragging = false;
        e.Pointer.Capture(null);

        Cursor = _previousCursor;
        _previousCursor = null;

        ScheduleOverlayHide();
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);

        if (_isDragging)
        {
            _isDragging = false;
            Cursor = _previousCursor;
            _previousCursor = null;
            ScheduleOverlayHide();
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        if (e.Delta.Y == 0)
            return;

        HdrRange *= MathF.Pow(WheelSensitivity, (float)e.Delta.Y);
        ShowOverlay();
        e.Handled = true;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        RenderHdrOverlayText(context);
    }

    protected void RenderHdrOverlayText(DrawingContext context)
    {
        if (_overlayText == null)
            return;

        var bounds = Bounds;

        var formattedText = new FormattedText(
            _overlayText,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            DefaultTypeface,
            12,
            Brushes.White);

        double padding = 4;
        double textWidth = formattedText.Width;
        double textHeight = formattedText.Height;
        double bgWidth = textWidth + padding * 2;
        double bgHeight = textHeight + padding * 2;
        double x = bounds.Width - bgWidth - 4;
        double y = 4;

        // Background
        context.FillRectangle(
            new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)),
            new Rect(x, y, bgWidth, bgHeight),
            4);

        // Text
        context.DrawText(formattedText, new Point(x + padding, y + padding));
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _overlayHideCts?.Cancel();
        _overlayHideCts?.Dispose();
        _overlayHideCts = null;
    }

    private void ShowOverlay()
    {
        UpdateOverlayText();
        InvalidateVisual();
        ScheduleOverlayHide();
    }

    private void UpdateOverlayText()
    {
        _overlayText = $"HDR: {HdrRange:F2}x";
    }

    private async void ScheduleOverlayHide()
    {
        _overlayHideCts?.Cancel();
        _overlayHideCts?.Dispose();
        _overlayHideCts = new CancellationTokenSource();
        var ct = _overlayHideCts.Token;

        try
        {
            await Task.Delay(1500, ct);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _overlayText = null;
                InvalidateVisual();
            });
        }
        catch (OperationCanceledException)
        {
        }
    }
}
