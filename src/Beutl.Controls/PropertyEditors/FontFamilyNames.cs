using System.Globalization;
using Beutl.Media;

namespace Beutl.Controls.PropertyEditors;

// The names the font picker shows and searches for a font family.
internal static class FontFamilyNames
{
    // The family's name in the UI language, else in US English, else the name the font manager reports.
    // The legacy family name of a font file can name a single weight ("Noto Sans CJK JP Medium"), so only
    // the name record the family is grouped under is used.
    public static string GetDisplayName(FontFamily family)
        => GetDisplayName(family, CultureInfo.CurrentUICulture);

    public static string GetDisplayName(FontFamily family, CultureInfo culture)
    {
        return TryGetFontName(family) is { } name
            && FontName.Localize(name.GetFamilySpellings(family.Name), culture) is { Length: > 0 } localized
            ? localized
            : family.Name;
    }

    // macOS names the fonts its own UI draws with after a leading dot (".SF NS", ".LastResort",
    // ".Hiragino Kaku Gothic Interface") and leaves them out of font menus; the picker lists them last.
    public static bool IsHidden(FontFamily family) => family.Name.StartsWith('.');

    // The family's name in every language its font carries, so "游ゴシック" and "Yu Gothic" both find it
    // whatever the UI language is.
    public static IEnumerable<string> GetSearchNames(FontFamily family)
    {
        yield return family.Name;
        if (TryGetFontName(family) is { } name)
        {
            foreach (FamilyNameRecord record in name.GetFamilySpellings(family.Name))
            {
                yield return record.Value;
            }
        }
    }

    private static FontName? TryGetFontName(FontFamily family)
    {
        return FontManager.Instance._fontNames.GetValueOrDefault(family);
    }
}
