using System.Reflection;

using Beutl.Configuration;
using Beutl.Media;

namespace Beutl.Graphics.UnitTests;

public static class TypefaceProvider
{
    static TypefaceProvider()
    {
        GlobalConfiguration.Instance.Save(Path.Combine(ArtifactProvider.GetArtifactDirectory(), "settings.json"));
        GlobalConfiguration.Instance.FontConfig.FontDirectories.Clear();
        Assembly asm = typeof(TypefaceProvider).Assembly;
        string[] array =
        [
            "NotoSansJP-Black.otf",
            "NotoSansJP-Bold.otf",
            "NotoSansJP-Light.otf",
            "NotoSansJP-Medium.otf",
            "NotoSansJP-Regular.otf",
            "NotoSansJP-Thin.otf",
            "Roboto-Medium.ttf",
            "Roboto-Regular.ttf",
        ];

        foreach (string item in array)
        {
            Stream? stream = asm.GetManifestResourceStream("Beutl.Engine.UnitTests.Assets.Font." + item);

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
}
