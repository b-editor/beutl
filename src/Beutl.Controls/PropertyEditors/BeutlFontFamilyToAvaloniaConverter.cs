using Avalonia.Data.Converters;

namespace Beutl.Controls.PropertyEditors;

public class BeutlFontFamilyToAvaloniaConverter()
    : FuncValueConverter<Media.FontFamily, Avalonia.Media.FontFamily>(
        f => f != null ? new Avalonia.Media.FontFamily(f.Name) : Avalonia.Media.FontFamily.Default)
{
    public static readonly BeutlFontFamilyToAvaloniaConverter Instance = new();
}
