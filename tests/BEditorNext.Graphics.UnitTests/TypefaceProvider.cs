using BEditorNext.Configuration;

namespace BEditorNext.Graphics.UnitTests;

public static class TypefaceProvider
{
    static TypefaceProvider()
    {
        GlobalConfiguration.Instance.FontConfig.FontDirectories.Clear();
        using Stream? stream = typeof(TypefaceProvider).Assembly.GetManifestResourceStream("BEditorNext.Graphics.UnitTests.Assets.Font.NotoSansJP-Regular.otf");

        if (stream != null)
            FontManager.Instance.AddFont(stream);
    }

    public static Typeface Typeface()
    {
        return new Typeface(new FontFamily("Noto Sans JP"));
    }
}
