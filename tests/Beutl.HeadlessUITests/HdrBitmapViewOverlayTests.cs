using System.Reflection;
using System.Runtime.InteropServices;
using Beutl.Configuration;
using Beutl.Graphics.Backend.Vulkan;
using Beutl.Media;
using Beutl.Media.Source;
using SkiaSharp;
using BtlRect = Beutl.Graphics.Rect;

namespace Beutl.HeadlessUITests;

// An HDR preview covers the player's viewport and draws the frame where the slot is, with a copy of the UI over it.
// Only MoltenVK on a real CAMetalLayer shows the layer's opacity and the overlay's way through the presentation thread.
[TestFixture]
public class HdrBitmapViewOverlayTests
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(10);

    // An opaque layer would cover the UI around a frame that does not fill the view.
    [Test]
    public void The_layer_lets_the_UI_under_it_show_around_the_frame()
    {
        RunOnRenderer((renderer, layer) =>
        {
            Assert.That(objc_msgSend_bool(layer, sel_getUid("isOpaque")), Is.False);
        });
    }

    [Test]
    public void An_overlay_and_a_moved_frame_are_drawn_without_a_fault()
    {
        RunOnRenderer((renderer, _) =>
        {
            var zoomed = new BtlRect(-0.5f, -0.25f, 2, 2);
            renderer.RequestRender(NewFrame(), Params(new BtlRect(0.25f, 0.25f, 0.5f, 0.5f)));
            renderer.SetOverlay(NewOverlay(), 64, 36);
            Assert.That(() => Field<bool>(renderer, "_overlayShown"), Is.True.After(5000, 10), "the overlay is drawn");

            // Zooming draws the uploaded frame again elsewhere, which the idle redraw then keeps.
            renderer.RequestRedraw(Params(zoomed));
            Assert.That(() => Field<RenderParams?>(renderer, "_lastRenderParams")?.Destination,
                Is.EqualTo(zoomed).After(5000, 10));

            renderer.SetOverlay(null, 64, 36);
            Assert.That(() => Field<bool>(renderer, "_overlayShown"), Is.False.After(5000, 10), "no overlay is left");
            Assert.That(Field<bool>(renderer, "_faulted"), Is.False);
        });
    }

    private static RenderParams Params(BtlRect destination)
    {
        return new RenderParams(16, 9, Stretch.Uniform, UIToneMappingOperator.None, 0f, IsSourceLinear: true,
            Destination: destination);
    }

    private static Ref<Bitmap> NewFrame()
    {
        return Ref<Bitmap>.Create(
            new Bitmap(16, 9, BitmapColorType.RgbaF16, BitmapAlphaType.Premul, BitmapColorSpace.LinearSrgb));
    }

    private static SKPicture NewOverlay()
    {
        using var recorder = new SKPictureRecorder();
        SKCanvas canvas = recorder.BeginRecording(SKRect.Create(64, 36));
        using var paint = new SKPaint { Color = SKColors.White };
        canvas.DrawRect(SKRect.Create(10, 10, 20, 10), paint);
        return recorder.EndRecording();
    }

    private static T Field<T>(VulkanSwapchainRenderer renderer, string name)
    {
        return (T)typeof(VulkanSwapchainRenderer)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(renderer)!;
    }

    private static void RunOnRenderer(Action<VulkanSwapchainRenderer, IntPtr> test)
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
            test(renderer, objc_msgSend(view, sel_getUid("layer")));
        }
        finally
        {
            objc_msgSend(view, sel_getUid("release"));
            objc_autoreleasePoolPop(pool);
        }
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
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool objc_msgSend_bool(IntPtr receiver, IntPtr selector);

    // By value, because x86-64 passes a struct this large on the stack rather than in floating-point registers.
    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_NSRect(IntPtr receiver, IntPtr selector, NSRect frame);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr objc_autoreleasePoolPush();

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern void objc_autoreleasePoolPop(IntPtr pool);
}
