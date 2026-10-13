using Beutl.AgentToolkit.Rendering;
using Beutl.AgentToolkit.Tests.Helpers;
using SkiaSharp;

namespace Beutl.AgentToolkit.Tests.Rendering;

public sealed class StoryboardRendererTests
{
    [Test]
    public void Write_failure_preserves_the_previous_contact_sheet()
    {
        string directory = Directory.CreateTempSubdirectory("beutl-contact-sheet-").FullName;
        try
        {
            string output = WriteFrame(directory, "existing.png", 8, 8, SKColors.Red);
            byte[] previous = File.ReadAllBytes(output);
            var frame = new RenderStoryboardShot("frame", 0, output, VisibilityAnalysis: null);
            var renderer = new StoryboardRenderer((path, bytes) =>
            {
                File.WriteAllBytes(path, bytes[..16]);
                throw new IOException("Disk full after a partial write.");
            });

            Assert.Throws<IOException>(() => renderer.RenderContactSheet([frame], output));

            Assert.That(File.ReadAllBytes(output), Is.EqualTo(previous));
            Assert.That(Directory.GetDirectories(directory), Is.Empty);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Test]
    public void Successful_write_preserves_permissions_and_produces_a_complete_contact_sheet()
    {
        string directory = Directory.CreateTempSubdirectory("beutl-contact-sheet-").FullName;
        try
        {
            string output = WriteFrame(directory, "existing.png", 8, 8, SKColors.Red);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(output, UnixFileMode.UserRead | UnixFileMode.UserWrite);

            new StoryboardRenderer().RenderContactSheet([new("frame", 0, output, VisibilityAnalysis: null)], output);

            using SKBitmap bitmap = SKBitmap.Decode(output);
            Assert.That(bitmap, Is.Not.Null);
            Assert.That(bitmap.Width, Is.GreaterThan(8));
            if (!OperatingSystem.IsWindows())
                Assert.That(File.GetUnixFileMode(output), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
            Assert.That(Directory.GetDirectories(directory), Is.Empty);
        }
        finally { Directory.Delete(directory, true); }
    }

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
        string workspace = TestWorkspace.CreateDirectory();
        RenderStoryboardShot[] frames = Enumerable.Range(0, 5)
            .Select(index => new RenderStoryboardShot(
                $"shot-{index}",
                index + 0.25,
                WriteFrame(workspace, $"shot-{index}.png", 1920, 1080, new SKColor((byte)(30 + index * 20), 90, 180)),
                VisibilityAnalysis: null))
            .ToArray();

        byte[] result = new StoryboardRenderer()
            .RenderContactSheetPng(frames, ImagePreviewEncoder.DefaultMaxLongEdge);
        using SKBitmap bitmap = Decode(result);
        StoryboardContactSheetLayout layout = StoryboardRenderer.CalculateLayout(
            frames.Length, 1920, 1080, ImagePreviewEncoder.DefaultMaxLongEdge);

        Assert.Multiple(() =>
        {
            Assert.That(layout.Width, Is.LessThanOrEqualTo(ImagePreviewEncoder.DefaultMaxLongEdge));
            Assert.That(layout.Height, Is.LessThanOrEqualTo(ImagePreviewEncoder.DefaultMaxLongEdge));
            Assert.That(bitmap.Width, Is.EqualTo(layout.Width));
            Assert.That(bitmap.Height, Is.EqualTo(layout.Height));
        });
    }

    [TestCase("intro", 1.25, "shot", 0, "00:00:01.250  intro",
        TestName = "Render_contact_sheet_png_burns_timecode_label")]
    [TestCase("between:intro~detail@L2:1/4", 0.75, "inbetween", 2, "00:00:00.750  inbetween L2  between:intro~detail@L2:1/4",
        TestName = "Render_contact_sheet_png_labels_inbetween_frames_with_subdivision_marker")]
    public void Render_contact_sheet_png_burns_the_frame_label(
        string name, double timeSeconds, string kind, int subdivisionLevel, string expectedLabel)
    {
        string workspace = TestWorkspace.CreateDirectory();
        var frame = new RenderStoryboardShot(
            name,
            timeSeconds,
            WriteFrame(workspace, "frame.png", 200, 100, SKColors.CornflowerBlue),
            VisibilityAnalysis: null,
            kind,
            subdivisionLevel);

        byte[] result = new StoryboardRenderer().RenderContactSheetPng([frame]);
        using SKBitmap bitmap = Decode(result);
        int captionTop = StoryboardRenderer.Padding + StoryboardRenderer.CalculateLayout(1, 200, 100).ImageHeight;

        Assert.Multiple(() =>
        {
            Assert.That(StoryboardRenderer.CreateLabel(frame), Is.EqualTo(expectedLabel));
            Assert.That(ContainsDarkPixel(bitmap, captionTop, captionTop + StoryboardRenderer.LabelHeight), Is.True);
        });
    }

    [Test]
    public void Render_contact_sheet_png_is_deterministic_for_same_inputs()
    {
        string workspace = TestWorkspace.CreateDirectory();
        RenderStoryboardShot[] frames =
        [
            new("first", 0.5, WriteFrame(workspace, "first.png", 240, 135, SKColors.OrangeRed), VisibilityAnalysis: null),
            new("second", 1.5, WriteFrame(workspace, "second.png", 240, 135, SKColors.MediumSeaGreen), VisibilityAnalysis: null)
        ];
        var renderer = new StoryboardRenderer();

        byte[] first = renderer.RenderContactSheetPng(frames, ImagePreviewEncoder.DefaultMaxLongEdge);
        byte[] second = renderer.RenderContactSheetPng(frames, ImagePreviewEncoder.DefaultMaxLongEdge);

        Assert.That(second, Is.EqualTo(first));
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
