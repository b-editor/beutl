using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Beutl.Controls.PropertyEditors;

public class BeutlFontFamilyToAvaloniaConverter()
    : FuncValueConverter<Media.FontFamily, Avalonia.Media.FontFamily>(
        f => f != null ? ToAvalonia(f.Name) : Avalonia.Media.FontFamily.Default)
{
    public static readonly BeutlFontFamilyToAvaloniaConverter Instance = new();

    // Avalonia looks a plain family name up among the OS fonts only, so a font only Beutl reads, from a material
    // package, the project or a font folder the OS does not know, is named through BeutlFontCollection. A font
    // neither can open keeps its plain name and draws in the default font.
    private static Avalonia.Media.FontFamily ToAvalonia(string name)
    {
        FontManager manager = FontManager.Current;
        return !manager.SystemFonts.TryGetGlyphTypeface(name, FontStyle.Normal, FontWeight.Normal, FontStretch.Normal, out _)
            && BeutlFontCollection.GetOrRegister(manager).TryGetGlyphTypeface(name, FontStyle.Normal, FontWeight.Normal, FontStretch.Normal, out _)
                ? new Avalonia.Media.FontFamily($"{BeutlFontCollection.CollectionKey}#{name}")
                : new Avalonia.Media.FontFamily(name);
    }
}
