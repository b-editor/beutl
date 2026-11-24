using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Beutl.Editor.Components.Helpers;

// The frame and handles of the panner and equalizer canvases, so both audio canvases look alike. The
// brushes come from the theme (AudioCanvasResources.axaml), so the canvases follow the light, dark and
// high-contrast themes, a theme an extension adds, and the accent color.
public abstract partial class AudioCanvas : Control
{
    private Pen? _borderPen;
    private Pen? _linePen;
    private Pen? _outlinePen;

    static AudioCanvas()
    {
        AffectsRender<AudioCanvas>(BackgroundProperty, BorderBrushProperty, ForegroundProperty,
            LineBrushProperty, HandleBrushProperty, AccentBrushProperty);
    }

    [GeneratedStyledProperty(AddOwnerFrom = typeof(Border))]
    public partial IBrush? Background { get; set; }

    // The outline of the frame.
    [GeneratedStyledProperty(AddOwnerFrom = typeof(Border))]
    public partial IBrush? BorderBrush { get; set; }

    // The labels and the outline of the handles.
    [GeneratedStyledProperty(AddOwnerFrom = typeof(TextElement))]
    public partial IBrush? Foreground { get; set; }

    // The axes and guides inside the frame.
    [GeneratedStyledProperty]
    public partial IBrush? LineBrush { get; set; }

    // The speakers and the handles that are not active.
    [GeneratedStyledProperty]
    public partial IBrush? HandleBrush { get; set; }

    // The active handle.
    [GeneratedStyledProperty]
    public partial IBrush? AccentBrush { get; set; }

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
