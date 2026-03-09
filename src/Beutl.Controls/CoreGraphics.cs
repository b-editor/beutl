using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Beutl.Controls;

[SupportedOSPlatform("macos")]
internal static partial class CoreGraphics
{
    private const string CGLib = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CFLib = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    public const int kCGEventSourceStateCombinedSessionState = 0;
    public const uint kCGMouseButtonLeft = 0;
    public const ushort kVK_Escape = 0x35;

    [StructLayout(LayoutKind.Sequential)]
    public struct CGPoint
    {
        public double X;
        public double Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CGSize
    {
        public double Width;
        public double Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CGRect
    {
        public CGPoint Origin;
        public CGSize Size;

        public CGRect(double x, double y, double w, double h)
        {
            Origin = new CGPoint { X = x, Y = y };
            Size = new CGSize { Width = w, Height = h };
        }
    }

    [LibraryImport(CGLib)]
    public static partial int CGAssociateMouseAndMouseCursorPosition(
        [MarshalAs(UnmanagedType.I1)] bool connected);

    [LibraryImport(CGLib)]
    public static partial void CGGetLastMouseDelta(
        out int deltaX, out int deltaY);

    [LibraryImport(CGLib)]
    public static partial uint CGMainDisplayID();

    [LibraryImport(CGLib)]
    public static partial int CGDisplayHideCursor(uint display);

    [LibraryImport(CGLib)]
    public static partial int CGDisplayShowCursor(uint display);

    [LibraryImport(CGLib)]
    public static partial int CGWarpMouseCursorPosition(CGPoint point);

    [LibraryImport(CGLib)]
    public static partial nint CGDisplayPixelsHigh(uint display);

    [LibraryImport(CGLib)]
    public static partial IntPtr CGDisplayCreateImageForRect(uint display, CGRect rect);

    [LibraryImport(CGLib)]
    public static partial IntPtr CGImageGetDataProvider(IntPtr image);

    [LibraryImport(CGLib)]
    public static partial void CGImageRelease(IntPtr image);

    [LibraryImport(CGLib)]
    public static partial IntPtr CGDataProviderCopyData(IntPtr provider);

    [LibraryImport(CFLib)]
    public static partial IntPtr CFDataGetBytePtr(IntPtr data);

    [LibraryImport(CFLib)]
    public static partial nint CFDataGetLength(IntPtr data);

    [LibraryImport(CFLib)]
    public static partial void CFRelease(IntPtr cf);

    [LibraryImport(CGLib)]
    public static partial IntPtr CGEventCreate(IntPtr source);

    [LibraryImport(CGLib)]
    public static partial CGPoint CGEventGetLocation(IntPtr @event);

    [LibraryImport(CGLib)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CGEventSourceButtonState(int stateID, uint button);

    [LibraryImport(CGLib)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CGEventSourceKeyState(int stateID, ushort keycode);
}
