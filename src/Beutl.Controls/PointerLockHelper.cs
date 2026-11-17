using Avalonia;
using Avalonia.Input;
using Beutl.Configuration;

namespace Beutl.Controls;

internal class PointerLockHelper
{
    public static readonly Cursor SizeWestEast = new(StandardCursorType.SizeWestEast);

    // macOS state
    private static uint s_macDisplay;
    private static PixelPoint s_savedScreenPosition;

    internal static bool IsEnabled(Visual visual)
        => ControlConfiguration.GetConfiguration(visual)?.EditorConfig.EnablePointerLockInProperty.Value ?? true;

    public static bool Pressed(Visual visual, Point dragStart)
    {
        if (!IsEnabled(visual))
            return false;

        if (OperatingSystem.IsWindows())
        {
            User32.ShowCursor(false);
        }
        else if (OperatingSystem.IsMacOS())
        {
            s_macDisplay = CoreGraphics.CGMainDisplayID();
            s_savedScreenPosition = visual.PointToScreen(dragStart);
            CoreGraphics.CGDisplayHideCursor(s_macDisplay);
            CoreGraphics.CGAssociateMouseAndMouseCursorPosition(false);
            // ここでCGGetLastMouseDeltaを呼び出さない場合、最初のMovedで大きなデルタが発生してしまう
            CoreGraphics.CGGetLastMouseDelta(out _, out _);
        }
        return OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();
    }

    public static void Released(bool locked)
    {
        if (!locked)
            return;

        if (OperatingSystem.IsWindows())
        {
            User32.ShowCursor(true);
        }
        else if (OperatingSystem.IsMacOS())
        {
            CoreGraphics.CGAssociateMouseAndMouseCursorPosition(true);
            CoreGraphics.CGWarpMouseCursorPosition(new CoreGraphics.CGPoint
            {
                X = s_savedScreenPosition.X,
                Y = s_savedScreenPosition.Y
            });
            CoreGraphics.CGDisplayShowCursor(s_macDisplay);
        }
    }

    public static Point Moved(Visual visual, Point point, ref Point dragStart)
    {
        if (IsEnabled(visual))
        {
            if (OperatingSystem.IsWindows())
            {
                Point move = point - dragStart;
                PixelPoint screenPoint = visual.PointToScreen(dragStart);
                User32.SetCursorPos(screenPoint.X, screenPoint.Y);
                return move;
            }
            else if (OperatingSystem.IsMacOS())
            {
                CoreGraphics.CGGetLastMouseDelta(out int dx, out int dy);
                return new Point(dx, dy);
            }
        }

        // Without a pointer lock (disabled, or an unsupported OS), returns the delta from the last position.
        Point delta = point - dragStart;
        dragStart = point;
        return delta;
    }
}
