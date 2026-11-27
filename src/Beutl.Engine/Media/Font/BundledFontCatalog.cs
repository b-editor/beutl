using System.Globalization;

namespace Beutl.Media;

internal static class BundledFontCatalog
{
    internal const string AssemblyName = "Beutl.BundledFonts";

    private const string DefaultFamilyName = "Noto Sans";
    private const string JapaneseFamilyName = "Noto Sans JP";
    private const string SimplifiedChineseFamilyName = "Noto Sans SC";
    private const string KoreanFamilyName = "Noto Sans KR";

    // Noto Sans 2.015; Noto Sans CJK f8d157532fbfaeda587e826d4cd5b21a49186f7c.
    // SC/KR SemiBold faces are static instances of the subset variable TTFs at wght=600.
    internal static IReadOnlyList<string> UiFamilyNames { get; } =
    [
        DefaultFamilyName,
        JapaneseFamilyName,
        SimplifiedChineseFamilyName,
        KoreanFamilyName,
    ];

    internal static IEnumerable<string> GetOrderedUiFamilyNames(CultureInfo culture)
    {
        string primaryFamily = culture.TwoLetterISOLanguageName switch
        {
            "ja" => JapaneseFamilyName,
            "zh" => SimplifiedChineseFamilyName,
            "ko" => KoreanFamilyName,
            _ => DefaultFamilyName,
        };

        return UiFamilyNames.Prepend(primaryFamily).Distinct();
    }

    // The trailing slash keeps the "Fonts/NotoSans" prefix from matching the CJK folders.
    internal static string GetResourcePrefix(string familyName) => $"Fonts/{familyName.Replace(" ", "")}/";
}
