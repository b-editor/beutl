using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Source;

namespace Beutl.UnitTests.TestInfrastructure;

public static class TestImages
{
    // Writes a white square PNG.
    public static void WritePng(string path, int size = 4)
    {
        using var bitmap = new Bitmap(size, size);
        bitmap.GetPixelSpan().Fill(255);
        Assert.That(bitmap.Save(path, EncodedImageFormat.Png), Is.True);
    }

    // Writes a white square PNG and returns an ImageSource that reads it.
    public static ImageSource WriteImage(string path, int size = 4)
    {
        WritePng(path, size);
        var source = new ImageSource();
        source.ReadFrom(new Uri(path));
        return source;
    }
}
