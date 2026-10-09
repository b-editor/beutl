using Avalonia.Data.Converters;
using Avalonia.Styling;
using LiveMarkdown.Avalonia;
using TextMateSharp.Grammars;

namespace Beutl.Converters;

public static class MarkdownConverters
{
    // An empty builder publishes an empty document, clearing any previously rendered content.
    public static readonly IValueConverter ToBuilder = new FuncValueConverter<string?, ObservableStringBuilder>(
        text => new ObservableStringBuilder(text));

    public static readonly IValueConverter CodeBlockTheme = new FuncValueConverter<ThemeVariant?, ThemeName>(
        theme => theme == ThemeVariant.Light ? ThemeName.LightPlus : ThemeName.DarkPlus);
}
