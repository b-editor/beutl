using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;

using Beutl.Configuration;

namespace Beutl.Controls;

internal class CursorHelper
{
    public static readonly Cursor SizeWestEast = new(StandardCursorType.SizeWestEast);

    public static void AdjustCursorPosition(TextBlock headerText, Point point, ref Point dragStart)
    {
        EditorConfig editorConfig = GlobalConfiguration.Instance.EditorConfig;
        if (editorConfig.AdjustOutOfScreenCursor
            && OperatingSystem.IsWindows()
            && TopLevel.GetTopLevel(headerText) is WindowBase window
            && window.Screens.ScreenFromWindow(window) is Screen screen)
        {
            PixelPoint screenPoint = headerText.PointToScreen(point);

            PixelRect area = screen.WorkingArea;

            int tolerance = 10;
            bool chPos = false;
            if ((area.Right - tolerance) <= screenPoint.X
                && screenPoint.X <= area.Right)
            {
                // 右端に到達
                screenPoint = screenPoint.WithX(area.X + (tolerance * 2));
                chPos = true;
            }
            else if (area.X <= screenPoint.X
                && screenPoint.X <= (area.X + tolerance))
            {
                // 左端に到達
                screenPoint = screenPoint.WithX(area.Right - (tolerance * 2));
                chPos = true;
            }

            if (chPos && User32.SetCursorPos(screenPoint.X, screenPoint.Y))
            {
                dragStart = headerText.PointToClient(screenPoint);
            }
        }
    }
}
