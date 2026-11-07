using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;

using SkiaSharp;

using PreviewCheckerboard = Beutl.Media.PreviewCheckerboard;

namespace Beutl.Views;

// The checkerboard the preview can show behind the frame in place of black (#4066). It lies outside the frame panel's
// transform, under it, so its squares keep their size on screen whatever the zoom: drawn inside the transform, a tile
// that is not whole pixels in the panel's units left a seam between tiles. The squares start at the frame's corner.
internal sealed class PreviewCheckerboardBackground : Control
{
    public static readonly StyledProperty<Rect?> FrameRectProperty =
        AvaloniaProperty.Register<PreviewCheckerboardBackground, Rect?>(nameof(FrameRect));

    static PreviewCheckerboardBackground()
    {
        AffectsRender<PreviewCheckerboardBackground>(FrameRectProperty);
    }

    public PreviewCheckerboardBackground()
    {
        IsHitTestVisible = false;
    }

    // Where the frame is, in this control's coordinates, or null while there is none.
    public Rect? FrameRect
    {
        get => GetValue(FrameRectProperty);
        set => SetValue(FrameRectProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (FrameRect is not { } frame)
            return;

        Rect visible = frame.Intersect(new Rect(Bounds.Size));
        if (visible.Width > 0 && visible.Height > 0)
            context.Custom(new CheckerboardDrawOperation(visible, frame.TopLeft));
    }

    // Repeats two squares of each gray from the frame's corner with a Skia shader, whose matrix places them exactly.
    private sealed class CheckerboardDrawOperation(Rect bounds, Point origin) : ICustomDrawOperation
    {
        // Made on first use, when Skia is loaded.
        private static SKImage? s_tile;

        public Rect Bounds { get; } = bounds;

        public void Dispose()
        {
        }

        public bool Equals(ICustomDrawOperation? other)
            => other is CheckerboardDrawOperation draw && draw.Bounds == Bounds && draw._origin == _origin;

        private readonly Point _origin = origin;

        public bool HitTest(Point p) => false;

        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } leaseFeature)
                return;

            using ISkiaSharpApiLease lease = leaseFeature.Lease();
            s_tile ??= CreateTile();
            using SKShader shader = s_tile.ToShader(
                SKShaderTileMode.Repeat,
                SKShaderTileMode.Repeat,
                new SKSamplingOptions(SKFilterMode.Nearest),
                SKMatrix.CreateScaleTranslation(
                    PreviewCheckerboard.SquareSize, PreviewCheckerboard.SquareSize, (float)_origin.X, (float)_origin.Y));
            using var paint = new SKPaint { Shader = shader };
            lease.SkCanvas.DrawRect(Bounds.ToSKRect(), paint);
        }

        private static SKImage CreateTile()
        {
            byte light = Gray(PreviewCheckerboard.Light);
            byte dark = Gray(PreviewCheckerboard.Dark);
            using var bitmap = new SKBitmap(new SKImageInfo(2, 2, SKColorType.Bgra8888, SKAlphaType.Opaque));
            bitmap.SetPixel(0, 0, new SKColor(light, light, light));
            bitmap.SetPixel(1, 1, new SKColor(light, light, light));
            bitmap.SetPixel(1, 0, new SKColor(dark, dark, dark));
            bitmap.SetPixel(0, 1, new SKColor(dark, dark, dark));
            return SKImage.FromBitmap(bitmap);

            static byte Gray(float value) => (byte)MathF.Round(value * 255);
        }
    }
}
