using Beutl.AgentToolkit.Rendering;
using SkiaSharp;

namespace Beutl.AgentToolkit.Tests.Rendering;

public sealed class StoryboardRendererTests
{
    [Test]
    public void Calculate_layout_uses_three_column_grid()
    {
        StoryboardContactSheetLayout layout = StoryboardRenderer.CalculateLayout(
            frameCount: 5,
            sourceWidth: 200,
            sourceHeight: 100);

        Assert.Multiple(() =>
        {
            Assert.That(layout.ColumnCount, Is.EqualTo(3));
            Assert.That(layout.RowCount, Is.EqualTo(2));
            Assert.That(layout.CellWidth, Is.EqualTo(200));
            Assert.That(layout.ImageHeight, Is.EqualTo(100));
            Assert.That(layout.Width, Is.EqualTo(664));
            Assert.That(layout.Height, Is.EqualTo(312));
        });
    }

    [Test]
    public void Render_contact_sheet_png_downscales_to_requested_long_edge()
    {
        string workspace = CreateWorkspace();
        StoryboardContactSheetFrame[] frames = Enumerable.Range(0, 5)
            .Select(index => new StoryboardContactSheetFrame(
                $"shot-{index}",
                index + 0.25,
                WriteFrame(workspace, $"shot-{index}.png", 1920, 1080, new SKColor((byte)(30 + index * 20), 90, 180))))
            .ToArray();

        StoryboardContactSheetPng result = new StoryboardRenderer()
            .RenderContactSheetPng(frames, ImagePreviewEncoder.DefaultMaxLongEdge);
        using SKBitmap bitmap = Decode(result.Bytes);

        Assert.Multiple(() =>
        {
            Assert.That(result.Layout.Width, Is.LessThanOrEqualTo(ImagePreviewEncoder.DefaultMaxLongEdge));
            Assert.That(result.Layout.Height, Is.LessThanOrEqualTo(ImagePreviewEncoder.DefaultMaxLongEdge));
            Assert.That(bitmap.Width, Is.EqualTo(result.Layout.Width));
            Assert.That(bitmap.Height, Is.EqualTo(result.Layout.Height));
        });
    }

    [Test]
    public void Render_contact_sheet_png_burns_timecode_label()
    {
        string workspace = CreateWorkspace();
        var frame = new StoryboardContactSheetFrame(
            "intro",
            1.25,
            WriteFrame(workspace, "intro.png", 200, 100, SKColors.CornflowerBlue));

        StoryboardContactSheetPng result = new StoryboardRenderer().RenderContactSheetPng([frame]);
        using SKBitmap bitmap = Decode(result.Bytes);
        int captionTop = StoryboardRenderer.Padding + result.Layout.ImageHeight;

        Assert.Multiple(() =>
        {
            Assert.That(StoryboardRenderer.CreateLabel(frame), Is.EqualTo("00:00:01.250  intro"));
            Assert.That(ContainsDarkPixel(bitmap, captionTop, captionTop + StoryboardRenderer.LabelHeight), Is.True);
        });
    }

    [Test]
    public void Render_contact_sheet_png_is_deterministic_for_same_inputs()
    {
        string workspace = CreateWorkspace();
        StoryboardContactSheetFrame[] frames =
        [
            new("first", 0.5, WriteFrame(workspace, "first.png", 240, 135, SKColors.OrangeRed)),
            new("second", 1.5, WriteFrame(workspace, "second.png", 240, 135, SKColors.MediumSeaGreen))
        ];
        var renderer = new StoryboardRenderer();

        byte[] first = renderer.RenderContactSheetPng(frames, ImagePreviewEncoder.DefaultMaxLongEdge).Bytes;
        byte[] second = renderer.RenderContactSheetPng(frames, ImagePreviewEncoder.DefaultMaxLongEdge).Bytes;

        Assert.That(second, Is.EqualTo(first));
    }

    private static string CreateWorkspace()
    {
        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string WriteFrame(string directory, string fileName, int width, int height, SKColor color)
    {
        string path = Path.Combine(directory, fileName);
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        surface.Canvas.Clear(color);
        byte[] png = ImagePreviewEncoder.EncodeSurfaceToPng(surface);
        File.WriteAllBytes(path, png);
        return path;
    }

    private static SKBitmap Decode(byte[] png)
    {
        return SKBitmap.Decode(png)
               ?? throw new InvalidOperationException("Contact sheet PNG could not be decoded.");
    }

    private static bool ContainsDarkPixel(SKBitmap bitmap, int topInclusive, int bottomExclusive)
    {
        int top = Math.Clamp(topInclusive, 0, bitmap.Height);
        int bottom = Math.Clamp(bottomExclusive, top, bitmap.Height);
        for (int y = top; y < bottom; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                SKColor pixel = bitmap.GetPixel(x, y);
                if (pixel.Alpha > 0 && pixel.Red < 80 && pixel.Green < 80 && pixel.Blue < 80)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
