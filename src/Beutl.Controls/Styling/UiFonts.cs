using System.Globalization;
using Avalonia.Media;

namespace Beutl.Controls.Styling;

#if BEUTL_LINKED_UI_FONTS
internal static class UiFonts
#else
public static class UiFonts
#endif
{
    public const string UiCultureArgument = "--ui-culture";

    // Noto Sans 2.015; Noto Sans CJK f8d157532fbfaeda587e826d4cd5b21a49186f7c.
    // SC/KR SemiBold faces are static instances of the subset variable TTFs at wght=600.
    private static readonly string[] s_familyNames =
    [
        "Noto Sans",
        "Noto Sans JP",
        "Noto Sans SC",
        "Noto Sans KR",
    ];

    public static FontFamily DefaultFontFamily => GetFontFamily(CultureInfo.CurrentUICulture);

    internal static string[] ApplyCultureArgument(string[] args)
    {
        int index = Array.IndexOf(args, UiCultureArgument);
        if (index < 0 || index + 1 >= args.Length)
            return args;

        var culture = CultureInfo.GetCultureInfo(args[index + 1]);
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        // The helper's own command-line parser only sees its existing options.
        return [.. args[..index], .. args[(index + 2)..]];
    }

    public static FontFamily GetFontFamily(CultureInfo culture)
    {
        return new FontFamily(string.Join(", ", GetOrderedFamilies(culture).Select(family => family.ToString())));
    }

    public static FontManagerOptions CreateFontManagerOptions(CultureInfo culture)
    {
        FontFamily[] families = GetOrderedFamilies(culture);
        return new FontManagerOptions
        {
            DefaultFamilyName = string.Join(", ", families.Select(family => family.ToString())),
            // Also cover controls with an explicit font, such as monospace text boxes.
            FontFallbacks = families.Select(family => new FontFallback { FontFamily = family }).ToArray(),
        };
    }

    private static FontFamily[] GetOrderedFamilies(CultureInfo culture)
    {
        string primaryFamily = culture.TwoLetterISOLanguageName switch
        {
            "ja" => "Noto Sans JP",
            "zh" => "Noto Sans SC",
            "ko" => "Noto Sans KR",
            _ => "Noto Sans",
        };

        return s_familyNames.Prepend(primaryFamily)
            .Distinct()
            .Select(GetEmbeddedFontFamily)
            .ToArray();
    }

    internal static FontFamily GetEmbeddedFontFamily(string name) =>
        new($"avares://{typeof(UiFonts).Assembly.GetName().Name}/Assets/Fonts/{name.Replace(" ", "")}#{name}");
}
