using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Beutl.Controls;

[SupportedOSPlatform("windows")]
internal static partial class User32
{
    [LibraryImport("User32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetCursorPos(int x, int y);


    [LibraryImport("User32.dll")]
    public static partial int ShowCursor([MarshalAs(UnmanagedType.Bool)] bool bShow);

    [LibraryImport("user32.dll")]
    public static partial short GetKeyState(int nVirtkey);

    public const int VK_LBUTTON = 0x01;
    public const int VK_ESCAPE = 0x1B;
}
