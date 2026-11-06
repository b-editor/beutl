using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Beutl.Editor.Components.Helpers;

// The frame and handles of the panner and equalizer canvases, so both audio canvases look alike. The
// brushes come from the theme (AudioCanvasResources.axaml), so the canvases follow the light, dark and
// high-contrast themes, a theme an extension adds, and the accent color.
public abstract class AudioCanvas : Control
{
    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        Border.BackgroundProperty.AddOwner<AudioCanvas>();

    public static readonly StyledProperty<IBrush?> BorderBrushProperty =
        Border.BorderBrushProperty.AddOwner<AudioCanvas>();

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<AudioCanvas>();

    public static readonly StyledProperty<IBrush?> LineBrushProperty =
        AvaloniaProperty.Register<AudioCanvas, IBrush?>(nameof(LineBrush));

    public static readonly StyledProperty<IBrush?> HandleBrushProperty =
        AvaloniaProperty.Register<AudioCanvas, IBrush?>(nameof(HandleBrush));

    public static readonly StyledProperty<IBrush?> AccentBrushProperty =
        AvaloniaProperty.Register<AudioCanvas, IBrush?>(nameof(AccentBrush));

    private Pen? _borderPen;
    private Pen? _linePen;
    private Pen? _outlinePen;

    static AudioCanvas()
    {
        AffectsRender<AudioCanvas>(BackgroundProperty, BorderBrushProperty, ForegroundProperty,
            LineBrushProperty, HandleBrushProperty, AccentBrushProperty);
    }

    public IBrush? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    // The outline of the frame.
    public IBrush? BorderBrush
    {
        get => GetValue(BorderBrushProperty);
        set => SetValue(BorderBrushProperty, value);
    }

    // The labels and the outline of the handles.
    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    // The axes and guides inside the frame.
    public IBrush? LineBrush
    {
        get => GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    // The speakers and the handles that are not active.
    public IBrush? HandleBrush
    {
        get => GetValue(HandleBrushProperty);
        set => SetValue(HandleBrushProperty, value);
    }

    // The active handle.
    public IBrush? AccentBrush
    {
        get => GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    protected Pen LinePen => GetPen(ref _linePen, LineBrush);

    // A pen of `brush`, kept until the brush is replaced. A theme brush that only changes color, such as the
    // accent, repaints through AffectsRender and keeps its pen.
    protected static Pen GetPen(ref Pen? cache, IBrush? brush, double thickness = 1,
        PenLineJoin lineJoin = PenLineJoin.Miter)
    {
        if (cache is null || !ReferenceEquals(cache.Brush, brush))
            cache = new Pen(brush, thickness, lineJoin: lineJoin);
        return cache;
    }

    // The drawing area `x` and `y` in from the edges, never collapsing below 1.
    protected Rect Inset(double x, double y)
        => new(x, y, Math.Max(1, Bounds.Width - x * 2), Math.Max(1, Bounds.Height - y * 2));

    protected void DrawFrame(DrawingContext context)
        => context.DrawRectangle(Background, GetPen(ref _borderPen, BorderBrush),
            new RoundedRect(new Rect(Bounds.Size).Deflate(1), 6));

    protected void DrawHandle(DrawingContext context, Point center, double radius, bool active)
        => context.DrawEllipse(active ? AccentBrush : HandleBrush, GetPen(ref _outlinePen, Foreground, 1.5),
            center, radius, radius);
}
