#if WINDOWS
#nullable enable

using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

using FluentAvalonia.UI.Media;

namespace Beutl.Controls.PropertyEditors;

public partial class ColorDropper
{
    private static bool WindowsIsClickDown()
        => User32.GetKeyState(User32.VK_LBUTTON) < 0;

    private static bool WindowsIsEscapeDown()
        => User32.GetKeyState(User32.VK_ESCAPE) < 0;

    private static (int X, int Y) WindowsGetCursorPosition()
    {
        Point pos = Cursor.Position;
        return (pos.X, pos.Y);
    }

    private static Color2 WindowsGetColorAtPoint(int x, int y)
    {
        Screen screen = Screen.PrimaryScreen!;
        using var bitmap = new Bitmap(screen.Bounds.Width, screen.Bounds.Height, PixelFormat.Format32bppArgb);

        using (var bmpGraphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            bmpGraphics.CopyFromScreen(0, 0, 0, 0, bitmap.Size);
        }

        Color c = bitmap.GetPixel(x, y);
        return Color2.FromARGB(c.A, c.R, c.G, c.B);
    }
}

#endif
