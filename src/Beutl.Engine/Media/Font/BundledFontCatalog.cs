using System.Globalization;

namespace Beutl.Media;

internal static class BundledFontCatalog
{
    internal const string AssemblyName = "Beutl.BundledFonts";

    // Noto Sans 2.015; Noto Sans CJK f8d157532fbfaeda587e826d4cd5b21a49186f7c.
    // SC/KR SemiBold faces are static instances of the subset variable TTFs at wght=600.
    internal static IReadOnlyList<string> FamilyNames { get; } =
    [
        "Noto Sans",
        "Noto Sans JP",
        "Noto Sans SC",
        "Noto Sans KR",
    ];

    internal static string[] GetFamilyNames(CultureInfo culture)
    {
        string primaryFamily = culture.TwoLetterISOLanguageName switch
        {
            "ja" => "Noto Sans JP",
            "zh" => "Noto Sans SC",
            "ko" => "Noto Sans KR",
            _ => "Noto Sans",
        };

        return [.. FamilyNames.Prepend(primaryFamily).Distinct()];
    }

    // The trailing slash keeps Fonts/NotoSans/ from matching the CJK resource folders.
    internal static string GetResourceDirectory(string name) => $"Fonts/{name.Replace(" ", "")}/";
}
