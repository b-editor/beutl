using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Input;

namespace Beutl.Controls.NativeContextMenu;

// What the native menu takes from Avalonia as plain values.
internal static class ContextMenuLayout
{
    // What NSMenuItem.keyEquivalent takes for keys without a character of their own.
    private const char UpArrow = (char)0xF700;
    private const char DownArrow = (char)0xF701;
    private const char LeftArrow = (char)0xF702;
    private const char RightArrow = (char)0xF703;
    private const char F1 = (char)0xF704;
    private const char ForwardDelete = (char)0xF728;
    private const char Home = (char)0xF729;
    private const char End = (char)0xF72B;
    private const char PageUp = (char)0xF72C;
    private const char PageDown = (char)0xF72D;
    private const char Backspace = (char)0x08;
    private const char Tab = (char)0x09;
    private const char Return = (char)0x0D;
    private const char Escape = (char)0x1B;

    // NSEventModifierFlags
    private const ulong ShiftFlag = 1UL << 17;
    private const ulong ControlFlag = 1UL << 18;
    private const ulong OptionFlag = 1UL << 19;
    private const ulong CommandFlag = 1UL << 20;

    // Where a menu of the size goes, as the popup positioner would place it for the placement and with the
    // top-left corner a native menu is given. AppKit keeps the menu on the screen itself.
    public static Point GetLocation(
        PlacementMode placement, PopupAnchor anchor, PopupGravity gravity, Rect target, Size menu, Point offset)
    {
        (anchor, gravity) = placement switch
        {
            PlacementMode.Bottom => (PopupAnchor.Bottom, PopupGravity.Bottom),
            PlacementMode.Right => (PopupAnchor.Right, PopupGravity.Right),
            PlacementMode.Left => (PopupAnchor.Left, PopupGravity.Left),
            PlacementMode.Top => (PopupAnchor.Top, PopupGravity.Top),
            PlacementMode.TopEdgeAlignedRight => (PopupAnchor.TopRight, PopupGravity.TopLeft),
            PlacementMode.TopEdgeAlignedLeft => (PopupAnchor.TopLeft, PopupGravity.TopRight),
            PlacementMode.BottomEdgeAlignedLeft => (PopupAnchor.BottomLeft, PopupGravity.BottomRight),
            PlacementMode.BottomEdgeAlignedRight => (PopupAnchor.BottomRight, PopupGravity.BottomLeft),
            PlacementMode.LeftEdgeAlignedTop => (PopupAnchor.TopLeft, PopupGravity.BottomLeft),
            PlacementMode.LeftEdgeAlignedBottom => (PopupAnchor.BottomLeft, PopupGravity.TopLeft),
            PlacementMode.RightEdgeAlignedTop => (PopupAnchor.TopRight, PopupGravity.BottomRight),
            PlacementMode.RightEdgeAlignedBottom => (PopupAnchor.BottomRight, PopupGravity.TopRight),
            PlacementMode.AnchorAndGravity => (anchor, gravity),
            _ => (PopupAnchor.None, PopupGravity.None)
        };

        double x = anchor.HasFlag(PopupAnchor.Left) ? target.Left
            : anchor.HasFlag(PopupAnchor.Right) ? target.Right
            : target.Center.X;
        double y = anchor.HasFlag(PopupAnchor.Top) ? target.Top
            : anchor.HasFlag(PopupAnchor.Bottom) ? target.Bottom
            : target.Center.Y;

        if (gravity.HasFlag(PopupGravity.Left)) x -= menu.Width;
        else if (!gravity.HasFlag(PopupGravity.Right)) x -= menu.Width / 2;

        if (gravity.HasFlag(PopupGravity.Top)) y -= menu.Height;
        else if (!gravity.HasFlag(PopupGravity.Bottom)) y -= menu.Height / 2;

        return new Point(x, y) + offset;
    }

    public static ulong GetModifierMask(KeyModifiers modifiers)
    {
        ulong mask = 0;
        if (modifiers.HasFlag(KeyModifiers.Shift)) mask |= ShiftFlag;
        if (modifiers.HasFlag(KeyModifiers.Control)) mask |= ControlFlag;
        if (modifiers.HasFlag(KeyModifiers.Alt)) mask |= OptionFlag;
        if (modifiers.HasFlag(KeyModifiers.Meta)) mask |= CommandFlag;
        return mask;
    }

    // Null for a key a menu item cannot show.
    public static string? GetKeyEquivalent(Key key)
    {
        if (key is >= Key.A and <= Key.Z) return ((char)('a' + (key - Key.A))).ToString();
        if (key is >= Key.D0 and <= Key.D9) return ((char)('0' + (key - Key.D0))).ToString();
        if (key is >= Key.NumPad0 and <= Key.NumPad9) return ((char)('0' + (key - Key.NumPad0))).ToString();
        if (key is >= Key.F1 and <= Key.F24) return ((char)(F1 + (key - Key.F1))).ToString();

        char? character = key switch
        {
            Key.Up => UpArrow,
            Key.Down => DownArrow,
            Key.Left => LeftArrow,
            Key.Right => RightArrow,
            Key.Delete => ForwardDelete,
            Key.Home => Home,
            Key.End => End,
            Key.PageUp => PageUp,
            Key.PageDown => PageDown,
            Key.Back => Backspace,
            Key.Tab => Tab,
            Key.Return => Return,
            Key.Escape => Escape,
            Key.Space => ' ',
            Key.OemPlus => '=',
            Key.Add => '+',
            Key.OemMinus or Key.Subtract => '-',
            Key.Multiply => '*',
            Key.Divide or Key.OemQuestion => '/',
            Key.OemComma => ',',
            Key.OemPeriod or Key.Decimal => '.',
            Key.OemSemicolon => ';',
            Key.OemQuotes => '\'',
            Key.OemOpenBrackets => '[',
            Key.OemCloseBrackets => ']',
            Key.OemPipe or Key.OemBackslash => '\\',
            Key.OemTilde => '`',
            _ => null
        };
        return character?.ToString();
    }
}
