using BeUtl.Media;
using BeUtl.Media.Pixel;
using BeUtl.Media.TextFormatting;

using NUnit.Framework;

namespace BeUtl.Graphics.UnitTests;

public class GraphicsTests
{
    [Test]
    public void DrawText()
    {
        var element = new TextElement
        {
            Foreground = Brushes.White,
            Size = 200,
            Text = "Text",
            Typeface = TypefaceProvider.Typeface()
        };

        var graphics = new Canvas(500, 500)
        {
            Foreground = Brushes.Gray,
        };

        graphics.Clear(Colors.Black);

        var rect = new Rect(graphics.Size.ToSize(1));
        element.Measure(rect.Size);
        Rect bounds = rect.CenterRect(element.Bounds);

        graphics.Translate(bounds.Position);
        graphics.FillRect(bounds.Size);
        graphics.DrawText(element, element.Bounds.Size);

        Bitmap<Bgra8888> bmp = graphics.GetBitmap();

        Assert.IsTrue(bmp.Save(Path.Combine(ArtifactProvider.GetArtifactDirectory(), "1.png"), EncodedImageFormat.Png));

        bmp.Dispose();
        graphics.Dispose();
    }
}
