using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Beutl.Editor.Components.Helpers;

// The palette and frame of the panner and equalizer canvases, so both audio canvases look alike.
internal static class AudioCanvasStyle
{
    public static readonly ImmutableSolidColorBrush Background = new(Color.FromRgb(30, 32, 36));
    public static readonly ImmutableSolidColorBrush Handle = new(Color.FromRgb(53, 57, 64));
    public static readonly ImmutableSolidColorBrush Accent = new(Color.FromRgb(100, 190, 245));
    public static readonly ImmutablePen Line = new(new ImmutableSolidColorBrush(Color.FromRgb(71, 76, 85)), 1);
    public static readonly ImmutablePen HandleOutline = new(Brushes.White, 1.5);

    public static void DrawFrame(DrawingContext context, Size size)
        => context.DrawRectangle(Background, Line, new RoundedRect(new Rect(size).Deflate(1), 6));

    public static void DrawHandle(DrawingContext context, Point center, double radius, bool active)
        => context.DrawEllipse(active ? Accent : Handle, HandleOutline, center, radius, radius);

    // The drawing area `x` and `y` in from the edges of `size`, never collapsing below 1.
    public static Rect Inset(Size size, double x, double y)
        => new(x, y, Math.Max(1, size.Width - x * 2), Math.Max(1, size.Height - y * 2));
}
