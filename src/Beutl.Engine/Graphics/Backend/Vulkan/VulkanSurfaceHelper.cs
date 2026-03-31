using System.Runtime.InteropServices;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Vulkan.Extensions.KHR;

namespace Beutl.Graphics.Backend.Vulkan;

/// <summary>
/// Holds a Vulkan surface and any platform-specific resources that must be released alongside it.
/// </summary>
internal readonly struct VulkanSurfaceInfo(SurfaceKHR surface, IntPtr platformHandle = default)
{
    public SurfaceKHR Surface { get; } = surface;

    /// <summary>
    /// Platform-specific handle that must be released when the surface is destroyed.
    /// On Linux/X11, this is the Display* opened via XOpenDisplay.
    /// </summary>
    public IntPtr PlatformHandle { get; } = platformHandle;
}

/// <summary>
/// Platform-specific Vulkan surface creation helper.
/// </summary>
internal static unsafe class VulkanSurfaceHelper
{
    private static readonly ILogger s_logger = Log.CreateLogger(typeof(VulkanSurfaceHelper));

    public static VulkanSurfaceInfo CreateSurface(Vk vk, Instance instance, IntPtr nativeHandle, string handleDescriptor)
    {
        if (OperatingSystem.IsWindows())
            return new VulkanSurfaceInfo(CreateWin32Surface(vk, instance, nativeHandle));

        if (OperatingSystem.IsMacOS())
            return new VulkanSurfaceInfo(CreateMetalSurface(vk, instance, nativeHandle));

        if (OperatingSystem.IsLinux())
            return CreateXlibSurface(vk, instance, nativeHandle);

        throw new PlatformNotSupportedException($"Vulkan surface creation is not supported on this platform");
    }

    public static void DestroySurface(Vk vk, Instance instance, VulkanSurfaceInfo surfaceInfo)
    {
        if (surfaceInfo.Surface.Handle == 0)
            return;

        if (!vk.TryGetInstanceExtension(instance, out KhrSurface khrSurface))
        {
            s_logger.LogWarning("Failed to get KHR_surface extension for surface destruction");
            return;
        }

        khrSurface.DestroySurface(instance, surfaceInfo.Surface, null);

        // Release platform-specific resources (e.g., X11 Display)
        if (OperatingSystem.IsLinux() && surfaceInfo.PlatformHandle != IntPtr.Zero)
        {
            XCloseDisplay(surfaceInfo.PlatformHandle);
            s_logger.LogDebug("Closed X11 display {Handle}", surfaceInfo.PlatformHandle);
        }
    }

    private static SurfaceKHR CreateWin32Surface(Vk vk, Instance instance, IntPtr hwnd)
    {
        if (!vk.TryGetInstanceExtension(instance, out KhrWin32Surface win32Surface))
            throw new InvalidOperationException("VK_KHR_win32_surface extension is not available");

        var hinstance = GetModuleHandleW(null);

        var createInfo = new Win32SurfaceCreateInfoKHR
        {
            SType = StructureType.Win32SurfaceCreateInfoKhr,
            Hinstance = hinstance,
            Hwnd = hwnd
        };

        SurfaceKHR surface;
        var result = win32Surface.CreateWin32Surface(instance, &createInfo, null, &surface);
        if (result != Result.Success)
            throw new InvalidOperationException($"Failed to create Win32 Vulkan surface: {result}");

        s_logger.LogDebug("Created Win32 Vulkan surface from HWND {Handle}", hwnd);
        return surface;
    }

    private static SurfaceKHR CreateMetalSurface(Vk vk, Instance instance, IntPtr nsView)
    {
        if (!vk.TryGetInstanceExtension(instance, out ExtMetalSurface metalSurface))
            throw new InvalidOperationException("VK_EXT_metal_surface extension is not available");

        // Create CAMetalLayer and attach to NSView
        var metalLayer = CreateAndAttachMetalLayer(nsView);
        if (metalLayer == IntPtr.Zero)
            throw new InvalidOperationException("Failed to create CAMetalLayer for NSView");

        var createInfo = new MetalSurfaceCreateInfoEXT
        {
            SType = StructureType.MetalSurfaceCreateInfoExt,
            PLayer = (nint*)metalLayer
        };

        SurfaceKHR surface;
        var result = metalSurface.CreateMetalSurface(instance, &createInfo, null, &surface);
        if (result != Result.Success)
            throw new InvalidOperationException($"Failed to create Metal Vulkan surface: {result}");

        s_logger.LogDebug("Created Metal Vulkan surface from NSView {Handle}", nsView);
        return surface;
    }

    private static VulkanSurfaceInfo CreateXlibSurface(Vk vk, Instance instance, IntPtr xWindow)
    {
        if (!vk.TryGetInstanceExtension(instance, out KhrXlibSurface xlibSurface))
            throw new InvalidOperationException("VK_KHR_xlib_surface extension is not available");

        var display = XOpenDisplay(null);
        if (display == IntPtr.Zero)
            throw new InvalidOperationException("Failed to open X11 display");

        var createInfo = new XlibSurfaceCreateInfoKHR
        {
            SType = StructureType.XlibSurfaceCreateInfoKhr,
            Dpy = (nint*)display,
            Window = (nint)xWindow
        };

        SurfaceKHR surface;
        var result = xlibSurface.CreateXlibSurface(instance, &createInfo, null, &surface);
        if (result != Result.Success)
        {
            XCloseDisplay(display);
            throw new InvalidOperationException($"Failed to create Xlib Vulkan surface: {result}");
        }

        s_logger.LogDebug("Created Xlib Vulkan surface from XWindow {Handle}", xWindow);
        return new VulkanSurfaceInfo(surface, display);
    }

    #region macOS ObjC Interop

    private static IntPtr CreateAndAttachMetalLayer(IntPtr nsView)
    {
        // [CAMetalLayer layer]
        var caMetalLayerClass = objc_getClass("CAMetalLayer");
        var layerSel = sel_getUid("layer");
        var metalLayer = objc_msgSend_IntPtr(caMetalLayerClass, layerSel);

        if (metalLayer == IntPtr.Zero)
            return IntPtr.Zero;

        // Configure HDR support on the layer
        // [metalLayer setWantsExtendedDynamicRangeContent:YES]
        var setEdrSel = sel_getUid("setWantsExtendedDynamicRangeContent:");
        objc_msgSend_bool(metalLayer, setEdrSel, true);

        // Set pixel format to RGBA16Float for HDR
        // MTLPixelFormatRGBA16Float = 115
        var setPixelFormatSel = sel_getUid("setPixelFormat:");
        objc_msgSend_nuint(metalLayer, setPixelFormatSel, 115);

        // Set colorspace to extended linear sRGB
        var colorSpaceName = CGColorSpaceCreateWithName(
            CoreFoundationString("kCGColorSpaceExtendedLinearSRGB"));
        if (colorSpaceName != IntPtr.Zero)
        {
            var setColorspaceSel = sel_getUid("setColorspace:");
            objc_msgSend_IntPtr_arg(metalLayer, setColorspaceSel, colorSpaceName);
            CGColorSpaceRelease(colorSpaceName);
        }

        // [nsView setWantsLayer:YES]
        var setWantsLayerSel = sel_getUid("setWantsLayer:");
        objc_msgSend_bool(nsView, setWantsLayerSel, true);

        // [nsView setLayer:metalLayer]
        var setLayerSel = sel_getUid("setLayer:");
        objc_msgSend_IntPtr_arg(nsView, setLayerSel, metalLayer);

        s_logger.LogDebug("Created CAMetalLayer with EDR and RGBA16Float for NSView");
        return metalLayer;
    }

    private static IntPtr CoreFoundationString(string value)
    {
        return CFStringCreateWithCString(IntPtr.Zero, value, 0x08000100 /* kCFStringEncodingUTF8 */);
    }

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr objc_getClass(string className);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr sel_getUid(string selector);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_bool(IntPtr receiver, IntPtr selector, bool value);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_nuint(IntPtr receiver, IntPtr selector, nuint value);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_IntPtr_arg(IntPtr receiver, IntPtr selector, IntPtr arg);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern IntPtr CGColorSpaceCreateWithName(IntPtr name);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern void CGColorSpaceRelease(IntPtr colorSpace);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, string cStr, uint encoding);

    #endregion

    #region Windows Interop

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);

    #endregion

    #region Linux Interop

    [DllImport("libX11.so.6")]
    private static extern IntPtr XOpenDisplay(string? display);

    [DllImport("libX11.so.6")]
    private static extern int XCloseDisplay(IntPtr display);

    #endregion
}
