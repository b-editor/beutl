using System.Globalization;
using SkiaSharp;

namespace Beutl.AgentToolkit.Rendering;

public sealed class StoryboardRenderer
{
    internal const int CellGap = 16;
    internal const int LabelHeight = 32;
    internal const int Padding = 16;
    internal const int MaxColumns = 3;

    public string RenderContactSheet(
        IReadOnlyList<StoryboardContactSheetFrame> frames,
        string outputPath)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        string? directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        byte[] png = RenderContactSheetPng(frames).Bytes;
        using Stream stream = File.Create(outputPath);
        stream.Write(png, 0, png.Length);
        return outputPath;
    }

    internal StoryboardContactSheetPng RenderContactSheetPng(
        IReadOnlyList<StoryboardContactSheetFrame> frames,
        int? maxLongEdge = null)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (frames.Count == 0)
        {
            throw new ArgumentException("Storyboard requires at least one frame.", nameof(frames));
        }

        using SKBitmap first = DecodeFrame(frames[0].StillPath);
        StoryboardContactSheetLayout layout = CalculateLayout(
            frames.Count,
            first.Width,
            first.Height,
            maxLongEdge);

        using var surface = SKSurface.Create(new SKImageInfo(layout.Width, layout.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(SKColors.White);

        using var textPaint = new SKPaint
        {
            IsAntialias = true,
            Color = SKColors.Black
        };
        using var font = new SKFont(SKTypeface.Default, 16);
        using var captionBackground = new SKPaint
        {
            Color = new SKColor(248, 248, 248)
        };
        using var borderPaint = new SKPaint
        {
            Color = new SKColor(210, 210, 210),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
            IsAntialias = true
        };

        for (int i = 0; i < frames.Count; i++)
        {
            StoryboardContactSheetFrame frame = frames[i];
            int column = i % layout.ColumnCount;
            int row = i / layout.ColumnCount;
            int x = Padding + column * (layout.CellWidth + CellGap);
            int y = Padding + row * (layout.ImageHeight + LabelHeight + CellGap);

            using SKBitmap bitmap = DecodeFrame(frame.StillPath);
            SKRect cellImageRect = new(x, y, x + layout.CellWidth, y + layout.ImageHeight);
            SKRect imageRect = FitInto(bitmap.Width, bitmap.Height, cellImageRect);
            SKRect captionRect = new(x, y + layout.ImageHeight, x + layout.CellWidth, y + layout.ImageHeight + LabelHeight);
            canvas.DrawBitmap(bitmap, imageRect);
            canvas.DrawRect(captionRect, captionBackground);
            canvas.DrawRect(new SKRect(x, y, x + layout.CellWidth, y + layout.ImageHeight + LabelHeight), borderPaint);

            string label = CreateLabel(frame);
            canvas.DrawText(TrimToWidth(label, layout.CellWidth - 16, font), x + 8, y + layout.ImageHeight + 22, font, textPaint);
        }

        return new StoryboardContactSheetPng(ImagePreviewEncoder.EncodeSurfaceToPng(surface), layout);
    }

    internal static StoryboardContactSheetLayout CalculateLayout(
        int frameCount,
        int sourceWidth,
        int sourceHeight,
        int? maxLongEdge = null)
    {
        if (frameCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameCount), "Storyboard requires at least one frame.");
        }

        if (sourceWidth <= 0 || sourceHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceWidth), "Source frame dimensions must be positive.");
        }

        int columnCount = Math.Min(MaxColumns, frameCount);
        int rowCount = (int)Math.Ceiling(frameCount / (double)columnCount);
        double scale = 1;
        if (maxLongEdge is > 0)
        {
            int horizontalFixed = Padding * 2 + (columnCount - 1) * CellGap;
            int verticalFixed = Padding * 2 + (rowCount - 1) * CellGap + rowCount * LabelHeight;
            double widthScale = (maxLongEdge.Value - horizontalFixed) / (double)(columnCount * sourceWidth);
            double heightScale = (maxLongEdge.Value - verticalFixed) / (double)(rowCount * sourceHeight);
            scale = Math.Min(1, Math.Min(widthScale, heightScale));
            if (!double.IsFinite(scale) || scale <= 0)
            {
                scale = Math.Min(1, Math.Min(1d / sourceWidth, 1d / sourceHeight));
            }
        }

        int cellWidth = Math.Max(1, (int)Math.Floor(sourceWidth * scale));
        int imageHeight = Math.Max(1, (int)Math.Floor(sourceHeight * scale));
        int width = Padding * 2 + columnCount * cellWidth + (columnCount - 1) * CellGap;
        int height = Padding * 2 + rowCount * (imageHeight + LabelHeight) + (rowCount - 1) * CellGap;
        return new StoryboardContactSheetLayout(columnCount, rowCount, cellWidth, imageHeight, width, height);
    }

    private static SKBitmap DecodeFrame(string path)
    {
        SKBitmap? bitmap = SKBitmap.Decode(path);
        if (bitmap is null)
        {
            throw new IOException($"Failed to read storyboard still image '{path}'.");
        }

        return bitmap;
    }

    private static SKRect FitInto(int sourceWidth, int sourceHeight, SKRect bounds)
    {
        double scale = Math.Min(bounds.Width / sourceWidth, bounds.Height / sourceHeight);
        float width = (float)(sourceWidth * scale);
        float height = (float)(sourceHeight * scale);
        float left = bounds.Left + (bounds.Width - width) / 2;
        float top = bounds.Top + (bounds.Height - height) / 2;
        return new SKRect(left, top, left + width, top + height);
    }

    internal static string CreateLabel(StoryboardContactSheetFrame frame)
    {
        return $"{FormatTimecode(frame.TimeSeconds)}  {frame.Name}";
    }

    private static string FormatTimecode(double timeSeconds)
    {
        double seconds = double.IsFinite(timeSeconds) ? Math.Max(0, timeSeconds) : 0;
        return TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
    }

    private static string TrimToWidth(string text, float maxWidth, SKFont font)
    {
        if (font.MeasureText(text) <= maxWidth)
        {
            return text;
        }

        const string ellipsis = "...";
        string trimmed = text;
        while (trimmed.Length > 0 && font.MeasureText(trimmed + ellipsis) > maxWidth)
        {
            trimmed = trimmed[..^1];
        }

        return trimmed.Length == 0 ? ellipsis : trimmed + ellipsis;
    }
}

public sealed record StoryboardContactSheetFrame(
    string Name,
    double TimeSeconds,
    string StillPath);

internal sealed record StoryboardContactSheetPng(
    byte[] Bytes,
    StoryboardContactSheetLayout Layout);

internal sealed record StoryboardContactSheetLayout(
    int ColumnCount,
    int RowCount,
    int CellWidth,
    int ImageHeight,
    int Width,
    int Height);
