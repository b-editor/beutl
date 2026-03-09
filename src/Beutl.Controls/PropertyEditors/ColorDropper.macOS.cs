#if !WINDOWS
#nullable enable

using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using FluentAvalonia.UI.Media;

using static Beutl.Controls.CoreGraphics;

namespace Beutl.Controls.PropertyEditors;

public partial class ColorDropper
{
    [SupportedOSPlatform("macos")]
    private static bool MacOSIsClickDown()
        => CGEventSourceButtonState(kCGEventSourceStateCombinedSessionState, kCGMouseButtonLeft);

    [SupportedOSPlatform("macos")]
    private static bool MacOSIsEscapeDown()
        => CGEventSourceKeyState(kCGEventSourceStateCombinedSessionState, kVK_Escape);

    [SupportedOSPlatform("macos")]
    private static (int X, int Y) MacOSGetCursorPosition()
    {
        IntPtr @event = CGEventCreate(IntPtr.Zero);
        try
        {
            CGPoint point = CGEventGetLocation(@event);
            // CGEventGetLocation returns coordinates in top-left origin (same as CGDisplayCreateImageForRect)
            return ((int)point.X, (int)point.Y);
        }
        finally
        {
            if (@event != IntPtr.Zero)
                CFRelease(@event);
        }
    }

    [SupportedOSPlatform("macos")]
    private static Color2 MacOSGetColorAtPoint(int x, int y)
    {
        uint display = CGMainDisplayID();
        CGRect rect = new(x, y, 1, 1);
        IntPtr image = CGDisplayCreateImageForRect(display, rect);

        if (image == IntPtr.Zero)
            return new Color2(255, 0, 0, 0);

        try
        {
            return ExtractFirstPixel(image);
        }
        finally
        {
            CGImageRelease(image);
        }
    }

    [SupportedOSPlatform("macos")]
    private static Color2 ExtractFirstPixel(IntPtr image)
    {
        IntPtr provider = CGImageGetDataProvider(image);
        if (provider == IntPtr.Zero)
            return new Color2(255, 0, 0, 0);

        IntPtr data = CGDataProviderCopyData(provider);
        if (data == IntPtr.Zero)
            return new Color2(255, 0, 0, 0);

        try
        {
            nint length = CFDataGetLength(data);
            if (length < 4)
                return new Color2(255, 0, 0, 0);

            IntPtr bytes = CFDataGetBytePtr(data);

            // Default CGImage format: BGRA (kCGBitmapByteOrder32Host + kCGImageAlphaPremultipliedFirst)
            byte b = Marshal.ReadByte(bytes, 0);
            byte g = Marshal.ReadByte(bytes, 1);
            byte r = Marshal.ReadByte(bytes, 2);
            byte a = Marshal.ReadByte(bytes, 3);

            // Undo premultiplied alpha
            if (a > 0 && a < 255)
            {
                r = Unpremultiply(r, a);
                g = Unpremultiply(g, a);
                b = Unpremultiply(b, a);
            }

            return Color2.FromARGB(a, r, g, b);
        }
        finally
        {
            CFRelease(data);
        }
    }

    private static byte Unpremultiply(byte c, byte a)
        => (byte)Math.Min(255, (int)Math.Round(c * 255.0 / a));
}

#endif
