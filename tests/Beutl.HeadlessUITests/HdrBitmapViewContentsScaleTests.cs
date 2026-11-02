using System.Runtime.InteropServices;
using Beutl.Controls;
using Beutl.Graphics.Backend.Vulkan;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class HdrBitmapViewContentsScaleTests
{
    // Only a real CAMetalLayer can show that the selector resolves and the double argument arrives.
    [Test]
    public void The_layer_takes_the_scale_it_is_given()
    {
        if (!OperatingSystem.IsMacOS())
            Assert.Ignore("CAMetalLayer exists only on macOS.");

        NativeLibrary.Load("/System/Library/Frameworks/QuartzCore.framework/QuartzCore");
        IntPtr pool = objc_autoreleasePoolPush();
        IntPtr layer = objc_msgSend(objc_getClass("CAMetalLayer"), sel_getUid("new"));
        try
        {
            HdrBitmapView.SetMacOSContentsScale(layer, 2.0);
            Assert.That(objc_msgSend_double(layer, sel_getUid("contentsScale")), Is.EqualTo(2.0));

            HdrBitmapView.SetMacOSContentsScale(layer, 1.0);
            Assert.That(objc_msgSend_double(layer, sel_getUid("contentsScale")), Is.EqualTo(1.0));
        }
        finally
        {
            objc_msgSend(layer, sel_getUid("release"));
            objc_autoreleasePoolPop(pool);
        }
    }

    // MoltenVK gives a swapchain the layer's bounds times its contentsScale, and that is what puts the view's device
    // pixels on screen: the size the renderer is asked for only counts where the surface reports none.
    [Test]
    public void A_resize_after_the_scale_changes_takes_the_views_device_pixels()
    {
        if (!OperatingSystem.IsMacOS())
            Assert.Ignore("MoltenVK runs only on macOS.");

        TestGpu.RequireAnyDevice();
        NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit");

        IntPtr pool = objc_autoreleasePoolPush();
        IntPtr view = objc_msgSend_NSRect(
            objc_msgSend(objc_getClass("NSView"), sel_getUid("alloc")),
            sel_getUid("initWithFrame:"),
            new NSRect(0, 0, 64, 36));
        try
        {
            using var renderer = new VulkanSwapchainRenderer();
            renderer.Initialize(view, 64, 36);
            IntPtr layer = objc_msgSend(view, sel_getUid("layer"));
            Assert.That(DrawableSize(layer), Is.EqualTo(new NSSize(64, 36)));

            HdrBitmapView.SetMacOSContentsScale(layer, 2.0);
            renderer.Resize(128, 72);
            Assert.That(() => DrawableSize(layer), Is.EqualTo(new NSSize(128, 72)).After(5000, 10));

            HdrBitmapView.SetMacOSContentsScale(layer, 1.0);
            renderer.Resize(64, 36);
            Assert.That(() => DrawableSize(layer), Is.EqualTo(new NSSize(64, 36)).After(5000, 10));
        }
        finally
        {
            objc_msgSend(view, sel_getUid("release"));
            objc_autoreleasePoolPop(pool);
        }
    }

    private static NSSize DrawableSize(IntPtr layer) => objc_msgSend_NSSize(layer, sel_getUid("drawableSize"));

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NSSize(double Width, double Height);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NSRect(double X, double Y, double Width, double Height);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr objc_getClass(string className);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr sel_getUid(string selector);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern double objc_msgSend_double(IntPtr receiver, IntPtr selector);

    // By value, because x86-64 passes a struct this large on the stack rather than in floating-point registers.
    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_NSRect(IntPtr receiver, IntPtr selector, NSRect frame);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern NSSize objc_msgSend_NSSize(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr objc_autoreleasePoolPush();

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern void objc_autoreleasePoolPop(IntPtr pool);
}
