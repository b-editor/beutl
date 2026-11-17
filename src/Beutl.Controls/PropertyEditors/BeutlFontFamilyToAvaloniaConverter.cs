using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Beutl.Controls.PropertyEditors;

public class BeutlFontFamilyToAvaloniaConverter : IMultiValueConverter
{
    public static readonly BeutlFontFamilyToAvaloniaConverter Instance = new();

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        return values.Count > 0 && values[0] is Media.FontFamily family
            ? ToAvalonia(family, values.Count > 1 ? values[1] as Media.FontManager : null)
            : FontFamily.Default;
    }

    public static FontFamily ToAvalonia(Media.FontFamily family, Media.FontManager? fonts)
    {
        FontManager manager = FontManager.Current;
        string name = family.Name;
        if (fonts is not null
            && !manager.SystemFonts.TryGetGlyphTypeface(name, FontStyle.Normal, FontWeight.Normal, FontStretch.Normal, out _))
        {
            BeutlFontCollection collection = BeutlFontCollection.GetOrRegister(manager, fonts);
            if (collection.TryGetGlyphTypeface(name, FontStyle.Normal, FontWeight.Normal, FontStretch.Normal, out _))
                return new FontFamily($"{collection.Key}#{name}");
        }
        return new FontFamily(name);
    }
}
