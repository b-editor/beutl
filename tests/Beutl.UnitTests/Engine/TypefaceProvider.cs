using System.Reflection;
using Beutl.Configuration;
using Beutl.Media;
using Beutl.Media.TextFormatting;

namespace Beutl.UnitTests.Engine;

public static class TypefaceProvider
{
    static TypefaceProvider()
    {
        GlobalConfiguration.Instance.Save(Path.Combine(ArtifactProvider.GetArtifactDirectory(), "settings.json"));
        Assembly asm = typeof(TypefaceProvider).Assembly;
        string[] array =
        [
            "BeutlTestVariable.ttf",
            "BeutlTestColorEmoji.ttf",
            "BeutlTestSeparateEmoji.ttf",
            "BeutlTestComposedText.otf",
            "NotoSansJP-Bold.otf",
            "NotoSansJP-Regular.otf",
            "Roboto-Medium.ttf",
            "Roboto-Regular.ttf",
        ];

        foreach (string item in array)
        {
            Stream? stream = asm.GetManifestResourceStream("Beutl.UnitTests.Assets.Font." + item);

            if (stream != null)
            {
                FontManager.Instance.AddFont(stream);
                stream.Dispose();
            }
        }
    }

    public static Typeface Typeface()
    {
        return new Typeface(new FontFamily("Noto Sans JP"));
    }

    public static FormattedText CreateText(string text, float size, float spacing = 0)
    {
        Typeface typeface = Typeface();
        return new FormattedText
        {
            Font = typeface.FontFamily,
            Style = typeface.Style,
            Weight = typeface.Weight,
            Size = size,
            Spacing = spacing,
            Text = text
        };
    }
}
