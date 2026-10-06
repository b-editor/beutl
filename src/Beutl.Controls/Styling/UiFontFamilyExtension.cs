using Avalonia.Markup.Xaml;

namespace Beutl.Controls.Styling;

#if BEUTL_LINKED_UI_FONTS
internal sealed class UiFontFamilyExtension : MarkupExtension
#else
public sealed class UiFontFamilyExtension : MarkupExtension
#endif
{
    public string? FamilyName { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        FamilyName is { Length: > 0 } name ? UiFonts.GetEmbeddedFontFamily(name) : UiFonts.DefaultFontFamily;
}
