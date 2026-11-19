using Avalonia;

namespace Beutl.Helpers;

// The shell does not reveal an auto-hide taskbar over a client area that covers the monitor.
// Leave 2 px free on those edges when maximized, as Chromium and Windows Terminal do.
internal sealed partial class WindowsAutoHideTaskbar
{
    private const int Gap = 2;

    internal static PixelRect Shrink(PixelRect client, PixelRect monitor, AutoHideEdges edges)
    {
        int left = client.X;
        int top = client.Y;
        int right = client.Right;
        int bottom = client.Bottom;
        if ((edges & AutoHideEdges.Left) != 0 && left <= monitor.X)
            left += Gap;
        if ((edges & AutoHideEdges.Top) != 0 && top <= monitor.Y)
            top += Gap;
        if ((edges & AutoHideEdges.Right) != 0 && right >= monitor.Right)
            right -= Gap;
        if ((edges & AutoHideEdges.Bottom) != 0 && bottom >= monitor.Bottom)
            bottom -= Gap;
        return new PixelRect(left, top, right - left, bottom - top);
    }
}

[Flags]
internal enum AutoHideEdges
{
    None = 0,
    Left = 1,
    Top = 2,
    Right = 4,
    Bottom = 8
}
