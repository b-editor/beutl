using Avalonia;
using Beutl.Configuration;
using Beutl.Media;

namespace Beutl.Controls;

public sealed partial class ControlFonts
{
    private static readonly AttachedProperty<FontManager?> s_fallbackProperty =
        AvaloniaProperty.RegisterAttached<ControlFonts, StyledElement, FontManager?>("FallbackFonts");

    [GeneratedAttachedProperty(Inherits = true)]
    public static partial FontManager? GetFonts(AvaloniaObject element);

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
