using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Beutl.Views.Tools.Scopes;

/// <summary>
/// Control that displays a waveform visualization of video luminance and RGB channels.
/// </summary>
public class WaveformControl : ScopeControlBase
{
    private static readonly string[] s_verticalLabels = ["100", "75", "50", "25", "0"];

    protected override string[]? VerticalAxisLabels => s_verticalLabels;

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
        // Reuse existing bitmap if size matches
        WriteableBitmap bitmap = existingBitmap?.PixelSize.Width == targetWidth && existingBitmap.PixelSize.Height == targetHeight
            ? existingBitmap
            : new WriteableBitmap(
                new PixelSize(targetWidth, targetHeight),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Premul);

        using ILockedFramebuffer fb = bitmap.Lock();
        var dest = new Span<uint>((void*)fb.Address, (fb.RowBytes * fb.Size.Height) / sizeof(uint));
        dest.Fill(PackColor(10, 10, 10));
        int stridePixels = fb.RowBytes / sizeof(uint);

        int xStep = Math.Max(1, sourceWidth / targetWidth);
        int yStep = Math.Max(1, sourceHeight / targetHeight);
        int channelWidth = targetWidth / 3;

        for (int y = 0; y < sourceHeight; y += yStep)
        {
            if ((y & 0x3F) == 0)
                token.ThrowIfCancellationRequested();

            int row = y * sourceStride;
            for (int x = 0; x < sourceWidth; x += xStep)
            {
                int idx = row + x * 4;
                byte b = sourceData[idx];
                byte g = sourceData[idx + 1];
                byte r = sourceData[idx + 2];

                // Luma waveform (full width overlay)
                int waveformX = x * targetWidth / sourceWidth;
                int luma = (int)(0.2126f * r + 0.7152f * g + 0.0722f * b);
                int lumaY = (int)((255 - luma) * (targetHeight - 1) / 255f);
                PlotPoint(dest, stridePixels, waveformX, lumaY, PackColor(220, 220, 220, 180));

                // RGB parade (three columns)
                int mappedX = x * (channelWidth - 1) / sourceWidth;
                PlotPoint(dest, stridePixels, mappedX, (int)((255 - r) * (targetHeight - 1) / 255f), PackColor(200, 80, 80, 160));
                PlotPoint(dest, stridePixels, channelWidth + mappedX, (int)((255 - g) * (targetHeight - 1) / 255f), PackColor(80, 200, 120, 160));
                PlotPoint(dest, stridePixels, 2 * channelWidth + mappedX, (int)((255 - b) * (targetHeight - 1) / 255f), PackColor(80, 140, 220, 160));
            }
        }

        // Draw horizontal guide lines
        DrawHorizontalGuide(dest, stridePixels, targetWidth, targetHeight / 4);
        DrawHorizontalGuide(dest, stridePixels, targetWidth, targetHeight / 2);
        DrawHorizontalGuide(dest, stridePixels, targetWidth, (targetHeight * 3) / 4);

        return bitmap;
    }

    private static void DrawHorizontalGuide(Span<uint> dest, int stride, int width, int y)
    {
        int height = dest.Length / stride;
        if (y < 0 || y >= height) return;

        uint color = PackColor(50, 50, 50, 160);
        int index = y * stride;
        int maxX = Math.Min(width, stride);
        for (int x = 0; x < maxX; x++)
        {
            dest[index + x] = color;
        }
    }
}
