using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Beutl.Views.Tools.Scopes;

/// <summary>
/// Control that displays a vectorscope visualization of color chrominance.
/// </summary>
public class VectorscopeControl : ScopeControlBase
{
    public static readonly DirectProperty<VectorscopeControl, bool> ShowColorTargetsProperty =
        AvaloniaProperty.RegisterDirect<VectorscopeControl, bool>(nameof(ShowColorTargets), o => o.ShowColorTargets,
            (o, v) => o.ShowColorTargets = v, true);

    // Cached brushes and pens for rendering
    private static readonly SolidColorBrush s_gridBrush = new(Color.FromArgb(160, 40, 40, 40));
    private static readonly Pen s_gridPen = new(s_gridBrush,1.5);
    private static readonly SolidColorBrush s_innerGridBrush = new(Color.FromArgb(120, 30, 30, 30));
    private static readonly Pen s_innerGridPen = new(s_innerGridBrush, 1.5);

    // Cached color target pens
    private static readonly Pen s_redTargetPen = new(new SolidColorBrush(Color.FromArgb(180, 255, 0, 0)), 1.5);
    private static readonly Pen s_greenTargetPen = new(new SolidColorBrush(Color.FromArgb(180, 0, 255, 0)), 1.5);
    private static readonly Pen s_blueTargetPen = new(new SolidColorBrush(Color.FromArgb(180, 0, 0, 255)), 1.5);
    private static readonly Pen s_cyanTargetPen = new(new SolidColorBrush(Color.FromArgb(180, 0, 255, 255)), 1.5);
    private static readonly Pen s_magentaTargetPen = new(new SolidColorBrush(Color.FromArgb(180, 255, 0, 255)), 1.5);
    private static readonly Pen s_yellowTargetPen = new(new SolidColorBrush(Color.FromArgb(180, 255, 255, 0)), 1.5);

    public bool ShowColorTargets
    {
        get;
        set => SetAndRaise(ShowColorTargetsProperty, ref field, value);
    } = true;

    // Vectorscope uses circular display, no standard axis labels
    protected override string[]? VerticalAxisLabels => null;

    protected override string[]? HorizontalAxisLabels => null;

    protected override unsafe WriteableBitmap? RenderScope(
        byte[] sourceData,
        int sourceWidth,
        int sourceHeight,
        int sourceStride,
        int targetWidth,
        int targetHeight,
        WriteableBitmap? existingBitmap,
        CancellationToken token)
    {
        if (token.IsCancellationRequested) return null;

        // Use square size for vectorscope
        int size = Math.Min(targetWidth, targetHeight);
        if (size <= 0) return null;

        // Reuse existing bitmap if size matches
        WriteableBitmap bitmap = existingBitmap?.PixelSize.Width == size && existingBitmap.PixelSize.Height == size
            ? existingBitmap
            : new WriteableBitmap(
                new PixelSize(size, size),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Premul);

        using ILockedFramebuffer fb = bitmap.Lock();
        var dest = new Span<uint>((void*)fb.Address, (fb.RowBytes * fb.Size.Height) / sizeof(uint));
        dest.Fill(PackColor(0, 0, 0, 0));
        int stridePixels = fb.RowBytes / sizeof(uint);

        int step = Math.Max(1, Math.Max(sourceWidth, sourceHeight) / size);

        for (int y = 0; y < sourceHeight; y += step)
        {
            if ((y & 0x3F) == 0)
                token.ThrowIfCancellationRequested();

            int row = y * sourceStride;
            for (int x = 0; x < sourceWidth; x += step)
            {
                int idx = row + x * 4;
                byte b = sourceData[idx];
                byte g = sourceData[idx + 1];
                byte r = sourceData[idx + 2];

                // Convert RGB to CbCr (YCbCr color space)
                int cb = (int)(-0.168736f * r - 0.331264f * g + 0.5f * b + 128);
                int cr = (int)(0.5f * r - 0.418688f * g - 0.081312f * b + 128);

                cb = Math.Clamp(cb, 0, 255);
                cr = Math.Clamp(cr, 0, 255);

                int vx = cb * (size - 1) / 255;
                int vy = (255 - cr) * (size - 1) / 255;

                PlotPoint(dest, stridePixels, vx, vy, PackColor(r, g, b, 180));
            }
        }

        return bitmap;
    }

    public override void Render(DrawingContext context)
    {
        // Override to handle square aspect ratio for vectorscope
        var bounds = Bounds;
        double axisMargin = AxisMargin;
        double availableWidth = bounds.Width - axisMargin;
        double availableHeight = bounds.Height - axisMargin;

        // Draw background
        var bgBrush = BackgroundBrush;
        if (bgBrush != null)
        {
            context.FillRectangle(bgBrush, new Rect(0, 0, bounds.Width, bounds.Height));
        }

        if (availableWidth <= 0 || availableHeight <= 0)
            return;

        // Calculate square size and centering offset
        double squareSize = Math.Min(availableWidth, availableHeight);
        double offsetX = axisMargin + (availableWidth - squareSize) / 2;
        double offsetY = (availableHeight - squareSize) / 2;

        DrawVectorscopeGridOnContext(context, offsetX, offsetY, squareSize);

        if (ShowColorTargets)
        {
            DrawColorTargetsOnContext(context, offsetX, offsetY, squareSize);
        }

        // Draw rendered scope image centered
        var bitmap = RenderedBitmap;
        if (bitmap != null)
        {
            var destRect = new Rect(offsetX, offsetY, squareSize, squareSize);
            context.DrawImage(bitmap, destRect);
        }

        // Draw minimal axis indicators for vectorscope
        DrawVectorscopeAxes(context, bounds, axisMargin, offsetX, offsetY, squareSize);
    }

    private void DrawVectorscopeAxes(DrawingContext context, Rect bounds, double axisMargin, double offsetX,
        double offsetY, double squareSize)
    {
        var labelBrush = LabelBrush ?? Brushes.Gray;

        // Draw Cb label (horizontal - blue-yellow axis)
        var cbLabel = new FormattedText(
            "Cb",
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            DefaultTypeface,
            10,
            labelBrush);
        context.DrawText(cbLabel, new Point(offsetX + squareSize / 2 - cbLabel.Width / 2, offsetY + squareSize + 4));

        // Draw Cr label (vertical - red-cyan axis)
        var crLabel = new FormattedText(
            "Cr",
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            DefaultTypeface,
            10,
            labelBrush);
        context.DrawText(crLabel,
            new Point(axisMargin - crLabel.Width - 4, offsetY + squareSize / 2 - crLabel.Height / 2));
    }

    private void DrawVectorscopeGridOnContext(DrawingContext context, double offsetX, double offsetY, double size)
    {
        double center = size / 2;
        double radius = size / 2 - 6;

        // Draw crosshairs
        context.DrawLine(s_gridPen, new Point(offsetX, offsetY + center), new Point(offsetX + size, offsetY + center));
        context.DrawLine(s_gridPen, new Point(offsetX + center, offsetY), new Point(offsetX + center, offsetY + size));

        // Draw outer circle
        context.DrawEllipse(null, s_gridPen, new Point(offsetX + center, offsetY + center), radius, radius);

        // Draw 75% radius circle
        double radius75 = radius * 0.75;
        context.DrawEllipse(null, s_innerGridPen, new Point(offsetX + center, offsetY + center), radius75, radius75);
    }

    private void DrawColorTargetsOnContext(DrawingContext context, double offsetX, double offsetY, double size)
    {
        var colorTargets = new (int Cb, int Cr, Pen Pen)[]
        {
            (90, 240, s_redTargetPen),      // Red
            (54, 34, s_greenTargetPen),     // Green
            (240, 110, s_blueTargetPen),    // Blue
            (166, 16, s_cyanTargetPen),     // Cyan
            (202, 222, s_magentaTargetPen), // Magenta
            (16, 146, s_yellowTargetPen)    // Yellow
        };

        double sizeD = size;
        double boxSize = 8;
        foreach (var (cb, cr, pen) in colorTargets)
        {
            double x = offsetX + cb * (sizeD - 1) / 255;
            double y = offsetY + (255 - cr) * (sizeD - 1) / 255;

            context.DrawRectangle(null, pen, new Rect(x - boxSize / 2, y - boxSize / 2, boxSize, boxSize));
        }
    }
}
