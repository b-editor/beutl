using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Beutl.Controls;

[SupportedOSPlatform("macos")]
internal static partial class CoreGraphics
{
    private const string Framework =
        "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    [LibraryImport(Framework)]
    public static partial int CGAssociateMouseAndMouseCursorPosition(
        [MarshalAs(UnmanagedType.I1)] bool connected);

    [LibraryImport(Framework)]
    public static partial void CGGetLastMouseDelta(
        out int deltaX, out int deltaY);

    [LibraryImport(Framework)]
    public static partial uint CGMainDisplayID();

    [LibraryImport(Framework)]
    public static partial int CGDisplayHideCursor(uint display);

    [LibraryImport(Framework)]
    public static partial int CGDisplayShowCursor(uint display);

    [StructLayout(LayoutKind.Sequential)]
    public struct CGPoint
    {
        public double X;
        public double Y;
    }

    [LibraryImport(Framework)]
    public static partial int CGWarpMouseCursorPosition(CGPoint point);
}
