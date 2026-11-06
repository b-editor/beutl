using System.Runtime.InteropServices;
using Avalonia.Platform;
using Beutl.Media;

namespace Beutl.Controls;

public partial class HdrBitmapView
{
    #region Platform-specific native window creation

    // Windows
    private static IPlatformHandle CreateWindowsControl(IPlatformHandle parent)
    {
        var hwnd = CreateWindowExW(
            0x00000000, // dwExStyle
            "Static", // lpClassName
            "", // lpWindowName
            0x40000000 | 0x10000000, // WS_CHILD | WS_VISIBLE
            0, 0, 1, 1,
            parent.Handle,
            IntPtr.Zero,
            GetModuleHandleW(null),
            IntPtr.Zero);

        if (hwnd == IntPtr.Zero)
            throw new InvalidOperationException("Failed to create child HWND");

        return new PlatformHandle(hwnd, "HWND");
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);

    private const int GWLP_WNDPROC = -4;
    private const uint WM_NCHITTEST = 0x0084;
    private const uint WM_NCDESTROY = 0x0082;
    private const int HTTRANSPARENT = -1;

    // The window procedures of the holder windows that pass the mouse through, by holder.
    private static readonly Dictionary<IntPtr, IntPtr> s_holderProcedures = [];

    // Avalonia's holder window answers hit tests itself, so the mouse over the frame never reached the UI under it: no
    // click, drag, wheel or context menu worked on an HDR preview. Answering transparent hands it to the next window under
    // the pointer on this thread, the UI's, as the static control the swapchain draws into already does. Returns whether
    // the holder is new.
    private static unsafe bool PassMouseThrough(IntPtr holder)
    {
        if (holder == IntPtr.Zero || s_holderProcedures.ContainsKey(holder))
            return false;

        delegate* unmanaged<IntPtr, uint, IntPtr, IntPtr, IntPtr> procedure = &HolderWindowProcedure;
        IntPtr previous = SetWindowLongPtrW(holder, GWLP_WNDPROC, (IntPtr)procedure);
        if (previous == IntPtr.Zero)
            return false;

        s_holderProcedures[holder] = previous;
        return true;
    }

    [UnmanagedCallersOnly]
    private static IntPtr HolderWindowProcedure(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == WM_NCHITTEST)
            return HTTRANSPARENT;

        if (!s_holderProcedures.TryGetValue(hwnd, out IntPtr previous))
            return DefWindowProcW(hwnd, message, wParam, lParam);

        if (message == WM_NCDESTROY)
            s_holderProcedures.Remove(hwnd);

        return CallWindowProcW(previous, hwnd, message, wParam, lParam);
    }

    // Shapes the holder window to the frame, or removes its shape. The child window it holds is clipped with it.
    private static void SetWindowsShape(IntPtr holder, Avalonia.PixelRect? shape)
    {
        if (holder == IntPtr.Zero)
            return;

        IntPtr region = shape is { } rect ? CreateRectRgn(rect.X, rect.Y, rect.Right, rect.Bottom) : IntPtr.Zero;
        // The window owns a region it takes.
        if (SetWindowRgn(holder, region, true) == 0 && region != IntPtr.Zero)
            DeleteObject(region);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtrW(IntPtr hwnd, int index, IntPtr newLong);

    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProcW(IntPtr previous, IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, [MarshalAs(UnmanagedType.Bool)] bool redraw);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr gdiObject);

    // macOS
    private static IPlatformHandle CreateMacOSControl(IPlatformHandle parent)
    {
        // [[NSView alloc] initWithFrame:NSMakeRect(0, 0, 1, 1)]
        var nsViewClass = objc_getClass("NSView");
        var allocSel = sel_getUid("alloc");
        var initWithFrameSel = sel_getUid("initWithFrame:");

        var nsView = objc_msgSend_IntPtr(nsViewClass, allocSel);
        nsView = objc_msgSend_NSRect(nsView, initWithFrameSel, new NSRect(0, 0, 1, 1));

        if (nsView == IntPtr.Zero)
            throw new InvalidOperationException("Failed to create NSView");

        return new PlatformHandle(nsView, "NSView");
    }

    private static IntPtr RetainMacOSObject(IntPtr obj)
    {
        return obj != IntPtr.Zero ? objc_msgSend_IntPtr(obj, sel_getUid("retain")) : IntPtr.Zero;
    }

    private static void ReleaseMacOSObject(IntPtr obj)
    {
        if (obj != IntPtr.Zero)
        {
            objc_msgSend_void(obj, sel_getUid("release"));
        }
    }

    // The black level of an OLED mastering display.
    private const float MasteringBlackNits = 0.0001f;

    // [nsView layer]: the CAMetalLayer the Vulkan surface presents to.
    private static IntPtr GetMacOSLayer(IntPtr nsView)
    {
        return objc_msgSend_IntPtr(nsView, sel_getUid("layer"));
    }

    // layer.contentsScale = scale. MoltenVK reports the layer's bounds times this as the surface's size and the
    // swapchain takes that size, whatever size it is asked for.
    internal static void SetMacOSContentsScale(IntPtr layer, double scale)
    {
        if (layer != IntPtr.Zero)
            objc_msgSend_void_double(layer, sel_getUid("setContentsScale:"), scale);
    }

    // layer.EDRMetadata = [CAEDRMetadata HDR10MetadataWithMinLuminance:maxLuminance:opticalOutputScale:], or nil to
    // show values as they are, clipped at the screen's headroom. The metadata describes a 1000 nit mastering display,
    // as most HDR10 content declares, and a value of 1.0 as Beutl's 203 nit reference white.
    internal static void SetMacOSEdrMetadata(IntPtr layer, bool toneMap)
    {
        if (layer == IntPtr.Zero)
            return;

        var metadata = IntPtr.Zero;
        var metadataClass = objc_getClass("CAEDRMetadata");
        if (toneMap && metadataClass != IntPtr.Zero)
        {
            metadata = objc_msgSend_float3(
                metadataClass,
                sel_getUid("HDR10MetadataWithMinLuminance:maxLuminance:opticalOutputScale:"),
                MasteringBlackNits,
                PreviewDisplayMapping.ContentPeakNits,
                PreviewDisplayMapping.ReferenceWhiteNits);
        }

        objc_msgSend_void_IntPtr(layer, sel_getUid("setEDRMetadata:"), metadata);
    }

    // [[[nsView window] screen] colorSpace]: the NSColorSpace of the screen the view is on, or IntPtr.Zero while the
    // view has no window or the window no screen.
    private static IntPtr GetMacOSScreenColorSpace(IntPtr nsView)
    {
        IntPtr window = objc_msgSend_IntPtr(nsView, sel_getUid("window"));
        IntPtr screen = window != IntPtr.Zero ? objc_msgSend_IntPtr(window, sel_getUid("screen")) : IntPtr.Zero;
        return screen != IntPtr.Zero ? objc_msgSend_IntPtr(screen, sel_getUid("colorSpace")) : IntPtr.Zero;
    }

    // The primaries of an NSColorSpace, or null when it is not RGB. ColorSync converts its pure red, green and blue to
    // extended linear sRGB, which keeps components outside sRGB, and their chromaticities follow from that. A screen's
    // profile can be a table rather than a matrix, so converting colors serves where reading its tags would not.
    internal static unsafe DisplayPrimaries? GetMacOSPrimaries(IntPtr nsColorSpace)
    {
        IntPtr colorSpace = nsColorSpace != IntPtr.Zero
            ? objc_msgSend_IntPtr(nsColorSpace, sel_getUid("CGColorSpace"))
            : IntPtr.Zero;
        if (colorSpace == IntPtr.Zero || CGColorSpaceGetModel(colorSpace) != ColorSpaceModelRgb
            || s_extendedLinearSrgb.Value == IntPtr.Zero)
        {
            return null;
        }

        Span<float> xy = stackalloc float[6];
        double* components = stackalloc double[4];
        for (int i = 0; i < 3; i++)
        {
            // Pure red, green or blue, opaque.
            components[0] = i == 0 ? 1 : 0;
            components[1] = i == 1 ? 1 : 0;
            components[2] = i == 2 ? 1 : 0;
            components[3] = 1;

            IntPtr color = CGColorCreate(colorSpace, components);
            IntPtr converted = CGColorCreateCopyByMatchingToColorSpace(
                s_extendedLinearSrgb.Value, RenderingIntentRelativeColorimetric, color, IntPtr.Zero);
            CGColorRelease(color);
            if (converted == IntPtr.Zero)
                return null;

            double* srgb = CGColorGetComponents(converted);
            // Linear sRGB to CIE XYZ (D65).
            double x = 0.4124564 * srgb[0] + 0.3575761 * srgb[1] + 0.1804375 * srgb[2];
            double y = 0.2126729 * srgb[0] + 0.7151522 * srgb[1] + 0.0721750 * srgb[2];
            double z = 0.0193339 * srgb[0] + 0.1191920 * srgb[1] + 0.9503041 * srgb[2];
            CGColorRelease(converted);

            xy[i * 2] = (float)(x / (x + y + z));
            xy[i * 2 + 1] = (float)(y / (x + y + z));
        }

        return new DisplayPrimaries(xy[0], xy[1], xy[2], xy[3], xy[4], xy[5]);
    }

    private const string CoreGraphicsLibrary = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    private const int ColorSpaceModelRgb = 1;

    private const int RenderingIntentRelativeColorimetric = 2;

    private static readonly Lazy<IntPtr> s_extendedLinearSrgb = new(() =>
    {
        // kCGColorSpaceExtendedLinearSRGB is a CFStringRef variable, so its export holds the string's address.
        IntPtr library = NativeLibrary.Load(CoreGraphicsLibrary);
        IntPtr name = Marshal.ReadIntPtr(NativeLibrary.GetExport(library, "kCGColorSpaceExtendedLinearSRGB"));
        return CGColorSpaceCreateWithName(name);
    });

    [DllImport(CoreGraphicsLibrary)]
    private static extern IntPtr CGColorSpaceCreateWithName(IntPtr name);

    [DllImport(CoreGraphicsLibrary)]
    private static extern int CGColorSpaceGetModel(IntPtr space);

    [DllImport(CoreGraphicsLibrary)]
    private static extern unsafe IntPtr CGColorCreate(IntPtr space, double* components);

    [DllImport(CoreGraphicsLibrary)]
    private static extern IntPtr CGColorCreateCopyByMatchingToColorSpace(
        IntPtr space, int intent, IntPtr color, IntPtr options);

    [DllImport(CoreGraphicsLibrary)]
    private static extern unsafe double* CGColorGetComponents(IntPtr color);

    [DllImport(CoreGraphicsLibrary)]
    private static extern void CGColorRelease(IntPtr color);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr objc_getClass(string className);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr sel_getUid(string selector);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_void(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_void_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg);

    // Float arguments travel in floating-point registers on arm64 and x86_64 alike, so plain objc_msgSend serves.
    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_void_double(IntPtr receiver, IntPtr selector, double arg);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_float3(IntPtr receiver, IntPtr selector, float arg1, float arg2, float arg3);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NSRect(double X, double Y, double Width, double Height);

    // By value, because x86-64 passes a struct this large on the stack rather than in floating-point registers.
    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_NSRect(IntPtr receiver, IntPtr selector, NSRect frame);

    // Linux
    private static IPlatformHandle CreateLinuxControl(IPlatformHandle parent)
    {
        // For X11, we return the parent handle and let the surface helper handle it
        // In a full implementation, we'd create a child X window
        return parent;
    }

    private const int ShapeBounding = 0;
    private const int ShapeSet = 0;
    private const int Unsorted = 0;

    private static readonly Lazy<IntPtr> s_x11Display = new(() =>
    {
        try
        {
            return XOpenDisplay(IntPtr.Zero);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return IntPtr.Zero;
        }
    });

    // Shapes the window to the frame with the X shape extension, or removes its shape. Without libXext the window keeps
    // its full size, and black shows around the frame.
    private static void SetX11Shape(IntPtr window, Avalonia.PixelRect? shape)
    {
        IntPtr display = s_x11Display.Value;
        if (display == IntPtr.Zero)
            return;

        try
        {
            if (shape is { } rect)
            {
                var rectangle = new XRectangle(
                    (short)rect.X, (short)rect.Y, (ushort)Math.Max(0, rect.Width), (ushort)Math.Max(0, rect.Height));
                XShapeCombineRectangles(display, window, ShapeBounding, 0, 0, ref rectangle, 1, ShapeSet, Unsorted);
            }
            else
            {
                XShapeCombineMask(display, window, ShapeBounding, 0, 0, IntPtr.Zero, ShapeSet);
            }

            XFlush(display);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct XRectangle(short X, short Y, ushort Width, ushort Height);

    [DllImport("libX11.so.6")]
    private static extern IntPtr XOpenDisplay(IntPtr displayName);

    [DllImport("libX11.so.6")]
    private static extern int XFlush(IntPtr display);

    [DllImport("libXext.so.6")]
    private static extern void XShapeCombineRectangles(IntPtr display, IntPtr window, int destinationKind, int xOffset,
        int yOffset, ref XRectangle rectangles, int count, int operation, int ordering);

    [DllImport("libXext.so.6")]
    private static extern void XShapeCombineMask(IntPtr display, IntPtr window, int destinationKind, int xOffset,
        int yOffset, IntPtr pixmap, int operation);

    #endregion

    private sealed class PlatformHandle(IntPtr handle, string descriptor) : IPlatformHandle
    {
        public IntPtr Handle { get; } = handle;
        public string HandleDescriptor { get; } = descriptor;
    }
}
