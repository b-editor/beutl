using System.Reflection;
using System.Runtime.InteropServices;
using Beutl.Configuration;
using Beutl.Graphics.Backend.Vulkan;
using Beutl.Media;
using Beutl.Media.Source;
using Silk.NET.Vulkan;

namespace Beutl.UnitTests.Engine.Graphics.Backend;

[TestFixture]
public class VulkanSwapchainRendererQueueTests
{
    private static readonly RenderParams s_params =
        new(16, 9, Stretch.Uniform, UIToneMappingOperator.None, 0f, IsSourceLinear: true);

    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(10);

    // A view that takes a size calls Resize and then asks for a frame. When the presentation thread finds both waiting,
    // the frame has to be drawn at the new size, or it shows stretched until the idle redraw.
    [Test]
    public void FrameQueuedAfterAResizeIsDrawnAtTheNewSize()
    {
        if (!OperatingSystem.IsMacOS())
            Assert.Ignore("MoltenVK runs only on macOS.");

        VulkanTestEnvironment.EnsureAvailable();
        NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit");

        IntPtr pool = objc_autoreleasePoolPush();
        IntPtr view = objc_msgSend_NSRect(
            objc_msgSend(objc_getClass("NSView"), sel_getUid("alloc")),
            sel_getUid("initWithFrame:"),
            new NSRect(0, 0, 64, 36));
        using var holding = new ManualResetEventSlim();
        using var hold = new ManualResetEventSlim();
        using var presented = new ManualResetEventSlim();
        try
        {
            using var renderer = new VulkanSwapchainRenderer();
            try
            {
                renderer.Initialize(view, 64, 36);

                // A frame is released on the presentation thread, so this holds the thread until both commands below
                // are in the queue, whatever the timing.
                renderer.RequestRender(NewFrame(() =>
                {
                    holding.Set();
                    hold.Wait(s_timeout);
                }), s_params);
                Assert.That(holding.Wait(s_timeout), Is.True, "The first frame was not presented.");

                objc_msgSend_double(objc_msgSend(view, sel_getUid("layer")), sel_getUid("setContentsScale:"), 2.0);
                renderer.Resize(128, 72);
                renderer.RequestRender(NewFrame(presented.Set), s_params);
                hold.Set();

                Assert.That(presented.Wait(s_timeout), Is.True, "The second frame was not presented.");
                var extent = (Extent2D)typeof(VulkanSwapchainRenderer)
                    .GetField("_presentedExtent", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .GetValue(renderer)!;
                Assert.That((extent.Width, extent.Height), Is.EqualTo((128u, 72u)));
            }
            finally
            {
                hold.Set();
            }
        }
        finally
        {
            objc_msgSend(view, sel_getUid("release"));
            objc_autoreleasePoolPop(pool);
        }
    }

    private static Ref<Bitmap> NewFrame(Action onRelease)
    {
        return Ref<Bitmap>.Create(
            new Bitmap(16, 9, BitmapColorType.RgbaF16, BitmapAlphaType.Premul, BitmapColorSpace.LinearSrgb),
            onRelease);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NSRect(double X, double Y, double Width, double Height);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr objc_getClass(string className);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr sel_getUid(string selector);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_NSRect(IntPtr receiver, IntPtr selector, NSRect frame);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_double(IntPtr receiver, IntPtr selector, double arg);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr objc_autoreleasePoolPush();

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern void objc_autoreleasePoolPop(IntPtr pool);
}
