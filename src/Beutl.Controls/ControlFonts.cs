using Avalonia;
using Beutl.Configuration;
using Beutl.Media;

namespace Beutl.Controls;

public sealed class ControlFonts
{
    public static readonly AttachedProperty<FontManager?> FontsProperty =
        AvaloniaProperty.RegisterAttached<ControlFonts, StyledElement, FontManager?>("Fonts", inherits: true);

    private static readonly AttachedProperty<FontManager?> s_fallbackProperty =
        AvaloniaProperty.RegisterAttached<ControlFonts, StyledElement, FontManager?>("FallbackFonts");

    public static FontManager? GetFonts(AvaloniaObject element) => element.GetValue(FontsProperty);

    public static void SetFonts(AvaloniaObject element, FontManager? fonts) => element.SetValue(FontsProperty, fonts);

    // For the owner of a fork it handed to controls, once they no longer show it: the UI copies of its fonts go.
    public static void Release(FontManager fonts)
    {
        ArgumentNullException.ThrowIfNull(fonts);
        PropertyEditors.BeutlFontCollection.Unregister(fonts);
    }

    internal static FontManager GetOrCreate(AvaloniaObject element)
    {
        if ((GetFonts(element) ?? element.GetValue(s_fallbackProperty)) is { } fonts)
            return fonts;
        fonts = new FontManager(ControlConfiguration.GetConfiguration(element)?.FontConfig ?? new FontConfig());
        element.SetValue(s_fallbackProperty, fonts);
        return fonts;
    }
}
