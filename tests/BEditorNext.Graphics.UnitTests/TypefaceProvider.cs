
using SkiaSharp;

namespace BEditorNext.Graphics.UnitTests;

public static class TypefaceProvider
{
    public static SKTypeface CreateTypeface()
    {
        using Stream? stream = typeof(TextElementTests).Assembly.GetManifestResourceStream("BEditorNext.Graphics.UnitTests.Assets.Font.NotoSansJP-Regular.otf");

        if (stream == null)
            return SKTypeface.CreateDefault();

        return SKTypeface.FromStream(stream);
    }
}
