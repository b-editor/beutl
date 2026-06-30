using SkiaSharp;

namespace Beutl.AgentToolkit.Rendering;

public sealed class StoryboardRenderer
{
    private const int CellGap = 16;
    private const int LabelHeight = 32;
    private const int Padding = 16;

    public string RenderContactSheet(
        IReadOnlyList<StoryboardContactSheetFrame> frames,
        string outputPath)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        if (frames.Count == 0)
        {
            throw new ArgumentException("Storyboard requires at least one frame.", nameof(frames));
        }

        using SKBitmap first = DecodeFrame(frames[0].StillPath);
        int cellWidth = first.Width;
        int imageHeight = first.Height;
        int columnCount = Math.Min(3, frames.Count);
        int rowCount = (int)Math.Ceiling(frames.Count / (double)columnCount);
        int width = Padding * 2 + columnCount * cellWidth + (columnCount - 1) * CellGap;
        int height = Padding * 2 + rowCount * (imageHeight + LabelHeight) + (rowCount - 1) * CellGap;

        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
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
            int column = i % columnCount;
            int row = i / columnCount;
            int x = Padding + column * (cellWidth + CellGap);
            int y = Padding + row * (imageHeight + LabelHeight + CellGap);

            using SKBitmap bitmap = DecodeFrame(frame.StillPath);
            var imageRect = new SKRect(x, y, x + cellWidth, y + imageHeight);
            var captionRect = new SKRect(x, y + imageHeight, x + cellWidth, y + imageHeight + LabelHeight);
            canvas.DrawBitmap(bitmap, imageRect);
            canvas.DrawRect(captionRect, captionBackground);
            canvas.DrawRect(new SKRect(x, y, x + cellWidth, y + imageHeight + LabelHeight), borderPaint);

            string label = $"{frame.Name}  {frame.TimeSeconds:0.###}s";
            canvas.DrawText(TrimToWidth(label, cellWidth - 16, font), x + 8, y + imageHeight + 22, font, textPaint);
        }

        string? directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using Stream stream = File.Create(outputPath);
        data.SaveTo(stream);
        return outputPath;
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
