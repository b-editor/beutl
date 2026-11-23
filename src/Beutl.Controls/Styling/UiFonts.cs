using System.Globalization;
using Avalonia.Media;
using SkiaSharp;
using BundledFontCatalog = Beutl.Media.BundledFontCatalog;

namespace Beutl.Controls.Styling;

#if BEUTL_LINKED_UI_FONTS
internal static class UiFonts
#else
public static class UiFonts
#endif
{
    public const string UiCultureArgument = "--ui-culture";

    public static FontFamily DefaultFontFamily => GetFontFamily(CultureInfo.CurrentUICulture);

    public static FontFamily ShortcutFontFamily => GetShortcutFontFamily(CultureInfo.CurrentUICulture);

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
            FontFamilyMappings = CreateFontFamilyMappings(),
        };
    }

    private static Dictionary<string, FontFamily>? CreateFontFamilyMappings()
    {
        // Avalonia keeps an entry of a font list such as "Consolas, Menlo, monospace" only when the face
        // it resolves to carries that name, so the fontconfig "monospace" alias is always dropped and Linux
        // falls back to the proportional UI font, which breaks the terminal's cell grid. Map the alias to
        // the family fontconfig picks for it.
        if (!OperatingSystem.IsLinux())
            return null;

        // Ask for a Latin face like Consolas and Menlo: under a CJK locale fontconfig ranks a CJK monospace
        // first, whose full-width box-drawing glyphs overflow the terminal's single-width cells.
        using SKTypeface? monospace = SKFontManager.Default.MatchCharacter("monospace", SKFontStyle.Normal, ["en"], 'M');
        return string.IsNullOrEmpty(monospace?.FamilyName)
            ? null
            : new Dictionary<string, FontFamily> { ["monospace"] = new FontFamily(monospace.FamilyName) };
    }

    public static FontFamily GetShortcutFontFamily(CultureInfo culture)
    {
        // Keep the macOS keyboard font local to shortcut labels so ordinary text can use emoji.
        return OperatingSystem.IsMacOS()
            ? new FontFamily(string.Join(", ", GetOrderedFamilies(culture).Select(family => family.ToString()).Prepend("Lucida Grande")))
            : GetFontFamily(culture);
    }

    private static FontFamily[] GetOrderedFamilies(CultureInfo culture)
        => [.. BundledFontCatalog.GetFamilyNames(culture).Select(GetEmbeddedFontFamily)];

    internal static FontFamily GetEmbeddedFontFamily(string name) =>
        new($"resm:{BundledFontCatalog.GetResourceDirectory(name)}?assembly={BundledFontCatalog.AssemblyName}#{name}");
}
