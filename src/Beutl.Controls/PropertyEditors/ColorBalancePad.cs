using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Avalonia.Platform;

namespace Beutl.Controls.PropertyEditors;

// The disk of a color wheel. Hues sit around it where the vectorscope places them and fade to neutral in the middle,
// and the puck shows Chroma in units of the radius. A drag anywhere moves the puck by the drag, a tenth as far with
// Alt or Command, and a double click asks to center it. As a scrub of a property's header does, the drag hides the
// cursor and shows it again where the drag began, so that the cursor does not wander off the puck it moves.
public class ColorBalancePad : Control
{
    public static readonly StyledProperty<Vector> ChromaProperty =
        AvaloniaProperty.Register<ColorBalancePad, Vector>(nameof(Chroma));

    // The neutral color in the middle of the disk.
    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        Border.BackgroundProperty.AddOwner<ColorBalancePad>();

    public static readonly StyledProperty<IBrush?> PuckBrushProperty =
        AvaloniaProperty.Register<ColorBalancePad, IBrush?>(nameof(PuckBrush), Brushes.White);

    // The crosshair in the middle.
    public static readonly StyledProperty<IBrush?> GuideBrushProperty =
        AvaloniaProperty.Register<ColorBalancePad, IBrush?>(nameof(GuideBrush));

    public static readonly RoutedEvent<VectorEventArgs> DragStartedEvent =
        RoutedEvent.Register<ColorBalancePad, VectorEventArgs>(nameof(DragStarted), RoutingStrategies.Bubble);

    // Vector is the move since the last one, in units of the radius, with Y up.
    public static readonly RoutedEvent<VectorEventArgs> DragDeltaEvent =
        RoutedEvent.Register<ColorBalancePad, VectorEventArgs>(nameof(DragDelta), RoutingStrategies.Bubble);

    public static readonly RoutedEvent<VectorEventArgs> DragCompletedEvent =
        RoutedEvent.Register<ColorBalancePad, VectorEventArgs>(nameof(DragCompleted), RoutingStrategies.Bubble);

    public static readonly RoutedEvent<RoutedEventArgs> ResetRequestedEvent =
        RoutedEvent.Register<ColorBalancePad, RoutedEventArgs>(nameof(ResetRequested), RoutingStrategies.Bubble);

    private const double PuckRadius = 5;
    private static readonly IPen s_puckShadow = new ImmutablePen(new ImmutableSolidColorBrush(Colors.Black, 0.45), 3);
    // Pads of one size, center color and scaling draw the same disk, as the wheels of a row do, so they share it and
    // fill it once; the last pad to let go of a disk disposes it.
    private static readonly Dictionary<DiskKey, SharedDisk> s_disks = [];
    private DiskKey? _diskKey;
    private bool _dragging;
    private Point _dragPoint;

    static ColorBalancePad()
    {
        AffectsRender<ColorBalancePad>(ChromaProperty, BackgroundProperty, PuckBrushProperty, GuideBrushProperty);
        FocusableProperty.OverrideDefaultValue<ColorBalancePad>(false);
    }

    public Vector Chroma
    {
        get => GetValue(ChromaProperty);
        set => SetValue(ChromaProperty, value);
    }

    public IBrush? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    public IBrush? PuckBrush
    {
        get => GetValue(PuckBrushProperty);
        set => SetValue(PuckBrushProperty, value);
    }

    public IBrush? GuideBrush
    {
        get => GetValue(GuideBrushProperty);
        set => SetValue(GuideBrushProperty, value);
    }

    public bool IsDragging => _dragging;

    // Ends the drag in progress and gives the cursor back, for a host that ended the gesture before the release; the
    // release then does nothing.
    public void StopDrag() => EndDrag();

    public event EventHandler<VectorEventArgs>? DragStarted
    {
        add => AddHandler(DragStartedEvent, value);
        remove => RemoveHandler(DragStartedEvent, value);
    }

    public event EventHandler<VectorEventArgs>? DragDelta
    {
        add => AddHandler(DragDeltaEvent, value);
        remove => RemoveHandler(DragDeltaEvent, value);
    }

    public event EventHandler<VectorEventArgs>? DragCompleted
    {
        add => AddHandler(DragCompletedEvent, value);
        remove => RemoveHandler(DragCompletedEvent, value);
    }

    public event EventHandler<RoutedEventArgs>? ResetRequested
    {
        add => AddHandler(ResetRequestedEvent, value);
        remove => RemoveHandler(ResetRequestedEvent, value);
    }

    // The disk, as large as the bounds allow and centered in them.
    internal Rect DiskBounds
    {
        get
        {
            double side = Math.Min(Bounds.Width, Bounds.Height);
            return new Rect((Bounds.Width - side) / 2, (Bounds.Height - side) / 2, side, side);
        }
    }

    // How far the puck travels from the middle for a chroma of 1; it stays inside the disk.
    private double TravelRadius => Math.Max(DiskBounds.Width / 2 - PuckRadius - 1, 1);

    // Where the puck is drawn for a chroma; beyond 1 it stays on the edge, in the chroma's direction.
    internal Point GetPuckPosition(Vector chroma)
    {
        double length = chroma.Length;
        if (length > 1)
            chroma /= length;

        Point center = DiskBounds.Center;
        return new Point(center.X + (chroma.X * TravelRadius), center.Y - (chroma.Y * TravelRadius));
    }

    // The pad fills the space it is given; the layout sizes it.
    protected override Size MeasureOverride(Size availableSize) => default;

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        ReleaseDisk();
    }

    public override void Render(DrawingContext context)
    {
        Rect disk = DiskBounds;
        if (disk.Width < 8)
            return;

        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        Color center = (Background as ISolidColorBrush)?.Color ?? Color.FromRgb(40, 40, 40);
        if (GetDisk(disk.Width, scaling, center) is { } bitmap)
            context.DrawImage(bitmap, disk);

        if (GuideBrush is { } guide)
        {
            var pen = new ImmutablePen(guide.ToImmutable(), 1);
            double arm = Math.Max(4, disk.Width * 0.06);
            Point middle = disk.Center;
            context.DrawLine(pen, new Point(middle.X - arm, middle.Y), new Point(middle.X + arm, middle.Y));
            context.DrawLine(pen, new Point(middle.X, middle.Y - arm), new Point(middle.X, middle.Y + arm));
        }

        Vector chroma = Chroma;
        Point puck = GetPuckPosition(chroma);
        IImmutableBrush puckBrush = (PuckBrush ?? Brushes.White).ToImmutable();
        if (chroma.Length > 0.01)
            context.DrawLine(new ImmutablePen(puckBrush, 1), disk.Center, puck);

        Color hue = ColorBalance.HueColor(chroma);
        double strength = Math.Min(chroma.Length, 1);
        var fill = new ImmutableSolidColorBrush(Mix(center, hue, strength));
        context.DrawEllipse(fill, s_puckShadow, puck, PuckRadius, PuckRadius);
        context.DrawEllipse(null, new ImmutablePen(puckBrush, 1.5), puck, PuckRadius, PuckRadius);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        PointerPoint point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed || !IsEffectivelyEnabled)
            return;

        e.Handled = true;
        if (e.ClickCount == 2)
        {
            RaiseEvent(new RoutedEventArgs(ResetRequestedEvent));
            return;
        }

        _dragging = true;
        _dragPoint = point.Position;
        e.Pointer.Capture(this);
        PointerLockHelper.Pressed(this, _dragPoint);
        RaiseEvent(new VectorEventArgs { RoutedEvent = DragStartedEvent, Vector = default });
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging)
            return;

        // A move with the button up means the release never reached the pad.
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            EndDrag();
            return;
        }

        Point move = PointerLockHelper.Moved(this, e.GetPosition(this), ref _dragPoint);
        double factor = NumberEditorHelper.IsFineModifier(e.KeyModifiers) ? 0.1 : 1;
        var delta = new Vector(move.X, -move.Y) * (factor / TravelRadius);
        e.Handled = true;
        if (delta != default)
            RaiseEvent(new VectorEventArgs { RoutedEvent = DragDeltaEvent, Vector = delta });
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragging && e.InitialPressMouseButton == MouseButton.Left)
        {
            e.Handled = true;
            EndDrag();
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        EndDrag();
    }

    private void EndDrag()
    {
        if (!_dragging)
            return;

        _dragging = false;
        PointerLockHelper.Released();
        RaiseEvent(new VectorEventArgs { RoutedEvent = DragCompletedEvent, Vector = default });
    }

    private WriteableBitmap? GetDisk(double side, double scaling, Color center)
    {
        int pixels = (int)Math.Ceiling(side * scaling);
        if (pixels < 2)
            return null;

        var key = new DiskKey(pixels, center, scaling);
        if (_diskKey == key)
            return s_disks[key].Bitmap;

        ReleaseDisk();
        if (!s_disks.TryGetValue(key, out SharedDisk? disk))
        {
            // At 96 DPI the bitmap's size is its pixel size, which DrawImage maps whole onto the disk; a scaled DPI
            // would shrink the source rectangle to a corner of it.
            var bitmap = new WriteableBitmap(new PixelSize(pixels, pixels), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            using (ILockedFramebuffer buffer = bitmap.Lock())
            {
                FillDisk(buffer, pixels, center, scaling);
            }

            s_disks[key] = disk = new SharedDisk(bitmap);
        }

        disk.Users++;
        _diskKey = key;
        return disk.Bitmap;
    }

    private void ReleaseDisk()
    {
        if (_diskKey is not { } key)
            return;

        _diskKey = null;
        SharedDisk disk = s_disks[key];
        if (--disk.Users == 0)
        {
            s_disks.Remove(key);
            disk.Bitmap.Dispose();
        }
    }

    // The rim shows each hue in full; inside, a faint tint of it grows from the neutral middle.
    private static unsafe void FillDisk(ILockedFramebuffer buffer, int pixels, Color center, double scaling)
    {
        double half = pixels / 2.0;
        double rim = Math.Max(2.5 * scaling, pixels * 0.025);
        for (int y = 0; y < pixels; y++)
        {
            uint* row = (uint*)(buffer.Address + (y * buffer.RowBytes));
            double ny = (half - (y + 0.5)) / half;
            for (int x = 0; x < pixels; x++)
            {
                double nx = ((x + 0.5) - half) / half;
                double r = Math.Sqrt((nx * nx) + (ny * ny));
                double fromEdge = (1 - r) * half;
                double coverage = Math.Clamp(fromEdge + 0.5, 0, 1);
                if (coverage <= 0)
                {
                    row[x] = 0;
                    continue;
                }

                Color hue = ColorBalance.HueColor(new Vector(nx, ny));
                double inner = 0.06 + (0.3 * r * r);
                double tint = inner + ((1 - inner) * Math.Clamp(rim - fromEdge + 0.5, 0, 1));
                Color color = Mix(center, hue, tint);
                row[x] = Premultiply(color, coverage);
            }
        }
    }

    private static Color Mix(Color from, Color to, double amount)
    {
        return Color.FromArgb(
            255,
            (byte)Math.Round(from.R + ((to.R - from.R) * amount)),
            (byte)Math.Round(from.G + ((to.G - from.G) * amount)),
            (byte)Math.Round(from.B + ((to.B - from.B) * amount)));
    }

    private static uint Premultiply(Color color, double alpha)
    {
        uint a = (uint)Math.Round(alpha * 255);
        uint r = (uint)Math.Round(color.R * alpha);
        uint g = (uint)Math.Round(color.G * alpha);
        uint b = (uint)Math.Round(color.B * alpha);
        return (a << 24) | (r << 16) | (g << 8) | b;
    }

    private readonly record struct DiskKey(int Pixels, Color Center, double Scaling);

    private sealed class SharedDisk(WriteableBitmap bitmap)
    {
        public WriteableBitmap Bitmap { get; } = bitmap;

        public int Users { get; set; }
    }
}
