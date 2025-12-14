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
    public static readonly StyledProperty<bool> ShowColorTargetsProperty =
        AvaloniaProperty.Register<VectorscopeControl, bool>(nameof(ShowColorTargets), true);

    public bool ShowColorTargets
    {
        get => GetValue(ShowColorTargetsProperty);
        set => SetValue(ShowColorTargetsProperty, value);
    }

    // Vectorscope uses circular display, no standard axis labels
    protected override string[]? VerticalAxisLabels => null;

    protected override string[]? HorizontalAxisLabels => null;

    protected override unsafe WriteableBitmap RenderScope(
        byte[] sourceData,
        int sourceWidth,
        int sourceHeight,
        int sourceStride,
        int targetWidth,
        int targetHeight,
        WriteableBitmap? existingBitmap,
        CancellationToken token)
    {
        // Use square size for vectorscope
        int size = Math.Min(targetWidth, targetHeight);

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
        dest.Fill(PackColor(8, 8, 8));
        int stridePixels = fb.RowBytes / sizeof(uint);

        // Draw grid and color targets
        DrawVectorscopeGrid(dest, stridePixels, size);

        if (ShowColorTargets)
        {
            DrawColorTargets(dest, stridePixels, size);
        }

        // Plot color data
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

                PlotPoint(dest, stridePixels, vx, vy, PackColor(b, g, r, 180));
            }
        }

        return bitmap;
    }

    private static void DrawVectorscopeGrid(Span<uint> dest, int stride, int size)
    {
        int center = size / 2;
        uint gridColor = PackColor(40, 40, 40, 160);

        // Draw crosshairs
        for (int i = 0; i < size && i < stride; i++)
        {
            int hIndex = center * stride + i;
            int vIndex = i * stride + center;
            if (hIndex < dest.Length) dest[hIndex] = gridColor;
            if (vIndex < dest.Length) dest[vIndex] = gridColor;
        }

        // Draw circular outline
        int radius = size / 2 - 6;
        for (int angle = 0; angle < 360; angle++)
        {
            double rad = Math.PI * angle / 180;
            int x = center + (int)(Math.Cos(rad) * radius);
            int y = center + (int)(Math.Sin(rad) * radius);
            if ((uint)x < size && (uint)y < size)
            {
                dest[y * stride + x] = gridColor;
            }
        }

        // Draw 75% radius circle
        int radius75 = (int)(radius * 0.75);
        for (int angle = 0; angle < 360; angle += 2)
        {
            double rad = Math.PI * angle / 180;
            int x = center + (int)(Math.Cos(rad) * radius75);
            int y = center + (int)(Math.Sin(rad) * radius75);
            if ((uint)x < size && (uint)y < size)
            {
                dest[y * stride + x] = PackColor(30, 30, 30, 120);
            }
        }
    }

    private static void DrawColorTargets(Span<uint> dest, int stride, int size)
    {
        int center = size / 2;
        int radius = size / 2 - 6;

        // Standard color bar positions in vectorscope (CbCr space)
        // These are approximate positions for 75% color bars
        var targets = new[]
        {
            (name: "R", cb: 90, cr: 240, color: PackColor(180, 60, 60, 200)),   // Red
            (name: "Y", cb: 16, cr: 210, color: PackColor(180, 180, 60, 200)),  // Yellow
            (name: "G", cb: 54, cr: 34, color: PackColor(60, 180, 60, 200)),    // Green
            (name: "C", cb: 166, cr: 16, color: PackColor(60, 180, 180, 200)),  // Cyan
            (name: "B", cb: 240, cr: 110, color: PackColor(60, 60, 180, 200)),  // Blue
            (name: "M", cb: 202, cr: 222, color: PackColor(180, 60, 180, 200)), // Magenta
        };

        foreach (var (_, cb, cr, color) in targets)
        {
            int x = center + (cb - 128) * radius / 128;
            int y = center - (cr - 128) * radius / 128;

            // Draw target box
            for (int dx = -3; dx <= 3; dx++)
            {
                for (int dy = -3; dy <= 3; dy++)
                {
                    if (Math.Abs(dx) == 3 || Math.Abs(dy) == 3)
                    {
                        int px = x + dx;
                        int py = y + dy;
                        if ((uint)px < size && (uint)py < size)
                        {
                            dest[py * stride + px] = color;
                        }
                    }
                }
            }
        }
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

    private void DrawVectorscopeAxes(DrawingContext context, Rect bounds, double axisMargin, double offsetX, double offsetY, double squareSize)
    {
        var axisBrush = AxisBrush ?? Brushes.Gray;
        var labelBrush = LabelBrush ?? Brushes.Gray;

        // Draw Cb label (horizontal - blue-yellow axis)
        var cbLabel = new FormattedText(
            "Cb",
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            DefaultTypeface,
            10,
            labelBrush);
        context.DrawText(cbLabel, new Point(offsetX + squareSize / 2 - cbLabel.Width / 2, offsetY + squareSize + 4));

        // Draw Cr label (vertical - red-cyan axis)
        var crLabel = new FormattedText(
            "Cr",
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            DefaultTypeface,
            10,
            labelBrush);
        context.DrawText(crLabel, new Point(axisMargin - crLabel.Width - 4, offsetY + squareSize / 2 - crLabel.Height / 2));
    }
}
