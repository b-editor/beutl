using System.Runtime.CompilerServices;

using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

using FluentAvalonia.UI.Controls;

namespace Beutl.Controls.NativeContextMenu;

internal static class ContextMenuIcons
{
    // The size in the menu. The types of icons size their glyphs in their own ways, by a font size or by their box, so
    // an icon is laid out in a box of its usual size and the whole box is scaled to this.
    public const double Size = 14;
    private const double LayoutSize = 16;
    private const int Scale = 2;

    // A symbol takes its font size from its source, where it is 12 unless set, and looks tiny next to other icons. At this
    // size it fills the box as they do, and any larger it is cut off.
    private const double SymbolFontSize = 18;

    // Whether the icon is a glyph, whose alpha is all that counts and whose color AppKit picks, and not a picture.
    public static bool IsTemplate(FAIconSource source) => source is not (FABitmapIconSource or FAImageIconSource);

    // A PNG of the icon. Null for a source that has no icon element.
    public static byte[]? TryRender(FAIconSource source)
    {
        if (CreateIcon(null, source) is not { } icon) return null;

        if (icon is FASymbolIcon symbol)
            symbol.FontSize = SymbolFontSize;

        icon.Foreground = Brushes.Black;
        icon.Measure(new Size(LayoutSize, LayoutSize));
        icon.Arrange(new Rect(0, 0, LayoutSize, LayoutSize));

        int pixels = (int)Size * Scale;
        double dpi = 96 * pixels / LayoutSize;
        using var bitmap = new RenderTargetBitmap(new PixelSize(pixels, pixels), new Vector(dpi, dpi));
        bitmap.Render(icon);

        using var stream = new MemoryStream();
        bitmap.Save(stream);
        return stream.ToArray();
    }

    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "CreateFromUnknown")]
    private static extern FAIconElement? CreateIcon(
        [UnsafeAccessorType("FluentAvalonia.UI.Controls.FAIconHelpers, FluentAvalonia")] object? helpers,
        FAIconSource source);
}
