using Avalonia;
using Avalonia.Input;

using Beutl.Configuration;

namespace Beutl.Controls;

internal class PointerLockHelper
{
    public static readonly Cursor SizeWestEast = new(StandardCursorType.SizeWestEast);

    public static void Pressed()
    {
        EditorConfig editorConfig = GlobalConfiguration.Instance.EditorConfig;
        if (OperatingSystem.IsWindows() && editorConfig.EnablePointerLockInProperty)
        {
            User32.ShowCursor(false);
        }
    }

    public static void Released()
    {
        EditorConfig editorConfig = GlobalConfiguration.Instance.EditorConfig;
        if (OperatingSystem.IsWindows() && editorConfig.EnablePointerLockInProperty)
        {
            User32.ShowCursor(true);
        }
    }

    public static void Moved(Visual visual, Point point, ref Point dragStart)
    {
        EditorConfig editorConfig = GlobalConfiguration.Instance.EditorConfig;
        if (editorConfig.EnablePointerLockInProperty
            && OperatingSystem.IsWindows())
        {
            PixelPoint screenPoint = visual.PointToScreen(dragStart);

            User32.SetCursorPos(screenPoint.X, screenPoint.Y);
        }
        else
        {
            dragStart = point;
        }
    }
}
