using Avalonia.Markup.Xaml;

namespace Beutl.Controls.Styling;

#if BEUTL_LINKED_UI_FONTS
internal sealed class UiFontFamilyExtension : MarkupExtension
#else
public sealed class UiFontFamilyExtension : MarkupExtension
#endif
{
    public bool ForShortcuts { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        ForShortcuts ? UiFonts.ShortcutFontFamily : UiFonts.DefaultFontFamily;
}
