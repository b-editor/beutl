using System.Diagnostics;
using System.Runtime.InteropServices;
using Beutl.Configuration;
using Beutl.Graphics.Backend.Vulkan;
using Beutl.Media;
using Beutl.Media.Source;
using Silk.NET.Vulkan;

namespace Beutl.UnitTests.Engine.Graphics.Backend;

[TestFixture]
public class VulkanSwapchainRendererTests
{
    private static readonly Extent2D s_view = new(1760, 990);
    private static readonly Extent2D s_old = new(1, 1);

    // Long enough for the presentation thread to take a resize and rebuild the swapchain.
    private static readonly TimeSpan s_rebuildTime = TimeSpan.FromMilliseconds(50);

    // The native view took its new size after the last frame was presented: the swapchain is rebuilt at the view's
    // size and the frame drawn again.
    [Test]
    public void APausedPreviewRebuildsAndRedrawsWhenTheViewGrewAfterTheLastFrame()
    {
        Assert.That(VulkanSwapchainRenderer.ShouldRebuild(s_view, swapchain: s_old), Is.True);
        Assert.That(VulkanSwapchainRenderer.ShouldRedrawPaused(false, s_view, presented: s_old), Is.True);
    }

    // A suboptimal present rebuilt the swapchain at the view's size but showed a frame drawn at the old one: the frame
    // is drawn again without another rebuild.
    [Test]
    public void APausedPreviewRedrawsAFrameShownBeforeTheSwapchainWasRebuilt()
    {
        Assert.That(VulkanSwapchainRenderer.ShouldRebuild(s_view, swapchain: s_view), Is.False);
        Assert.That(VulkanSwapchainRenderer.ShouldRedrawPaused(false, s_view, presented: s_old), Is.True);
    }

    // The player's HDR preview places the frame itself, zoomed and panned past the view's edges, instead of by Stretch.
    [Test]
    public void AFrameGivenADestinationGoesThereWhateverTheStretch()
    {
        var destination = new Beutl.Graphics.Rect(-0.5f, 0.25f, 2f, 1.5f);
        var renderParams = new RenderParams(1920, 1080, Stretch.Uniform, UIToneMappingOperator.None, 0f,
            IsSourceLinear: true, Destination: destination);

        VulkanSwapchainRenderer.ComputeStretchRects(renderParams, s_view, out PresentPushConstants pc);

        Assert.That((pc.DstX, pc.DstY, pc.DstW, pc.DstH), Is.EqualTo((-0.5f, 0.25f, 2f, 1.5f)));
    }

    // A zoomed-in frame shows its pixels sharp, whether the player places it or Stretch does (#4082).
    [Test]
    public void AFrameAskedForSharpPixelsTellsTheShaderSo()
    {
        var placed = new RenderParams(1920, 1080, Stretch.Uniform, UIToneMappingOperator.None, 0f,
            IsSourceLinear: true, Destination: new Beutl.Graphics.Rect(0f, 0f, 1f, 1f), SampleNearest: true);

        VulkanSwapchainRenderer.ComputeStretchRects(placed, s_view, out PresentPushConstants fromPlaced);
        VulkanSwapchainRenderer.ComputeStretchRects(placed with { Destination = null }, s_view, out PresentPushConstants fromStretch);
        VulkanSwapchainRenderer.ComputeStretchRects(placed with { SampleNearest = false }, s_view, out PresentPushConstants smooth);

        Assert.Multiple(() =>
        {
            Assert.That(fromPlaced.SampleNearest, Is.EqualTo(1));
            Assert.That(fromStretch.SampleNearest, Is.EqualTo(1));
            Assert.That(smooth.SampleNearest, Is.Zero);
        });
    }

    // The presentation thread takes a frame through its queue and a redraw beside it. A frame requested with the old
    // destination just before a zoom can reach a later batch than the zoom's redraw did, and must not move the frame back.
    [Test]
    public void AFrameRequestedBeforeARedrawTakenEarlierKeepsTheRedrawsDestination()
    {
        RenderParams fit = WithDestination(new Beutl.Graphics.Rect(0.1f, 0.1f, 0.8f, 0.8f));
        RenderParams zoomed = WithDestination(new Beutl.Graphics.Rect(-0.5f, -0.5f, 2f, 2f));
        var newest = new VulkanSwapchainRenderer.NewestRenderParams();

        Assert.Multiple(() =>
        {
            Assert.That(newest.Take(zoomed, stamp: 2), Is.EqualTo(zoomed), "the redraw, taken first");
            Assert.That(newest.Take(fit, stamp: 1), Is.EqualTo(zoomed), "the frame requested before it, taken later");
            Assert.That(newest.Take(fit, stamp: 3), Is.EqualTo(fit), "a request after both");
        });
    }

    private static RenderParams WithDestination(Beutl.Graphics.Rect destination)
    {
        return new RenderParams(1920, 1080, Stretch.Uniform, UIToneMappingOperator.None, 0f, IsSourceLinear: true,
            Destination: destination);
    }

    [Test]
    public void APausedPreviewKeepsAFrameDrawnAtTheSurfacesSize()
    {
        Assert.That(VulkanSwapchainRenderer.ShouldRebuild(s_view, swapchain: s_view), Is.False);
        Assert.That(VulkanSwapchainRenderer.ShouldRedrawPaused(false, s_view, presented: s_view), Is.False);
    }

    // A surface without a size to follow (Wayland, a minimized window) changes nothing, while a changed display still
    // redraws the frame.
    [Test]
    public void OnlyAChangedDisplayRedrawsWhenTheSurfaceReportsNoSize()
    {
        Assert.That(VulkanSwapchainRenderer.ShouldRebuild(null, swapchain: s_old), Is.False);
        Assert.That(VulkanSwapchainRenderer.ShouldRedrawPaused(false, null, presented: s_old), Is.False);
        Assert.That(VulkanSwapchainRenderer.ShouldRedrawPaused(true, null, presented: s_old), Is.True);
        Assert.That(VulkanSwapchainRenderer.ShouldRedrawPaused(true, s_view, presented: s_view), Is.True);
    }

    // MoltenVK 1.4.0 leaves the layer at 1x1 when a swapchain replaces one with a frame still on its way to the screen,
    // and every frame after that shows as one pixel stretched over the view. A layer outside any window starts holding
    // its presents back about two seconds after it is created, so the later rebuilds here meet that case.
    [Test]
    public void RebuildsAfterPresentsLeaveTheLayerAtTheSwapchainsSize()
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
        try
        {
            using var renderer = new VulkanSwapchainRenderer();
            renderer.Initialize(view, 64, 36);
            IntPtr layer = objc_msgSend(view, sel_getUid("layer"));

            var elapsed = Stopwatch.StartNew();
            for (int i = 0; elapsed.Elapsed < TimeSpan.FromSeconds(3); i++)
            {
                using var presented = new ManualResetEventSlim();
                renderer.RequestRender(
                    Ref<Bitmap>.Create(
                        new Bitmap(16, 9, BitmapColorType.RgbaF16, BitmapAlphaType.Premul, BitmapColorSpace.LinearSrgb),
                        presented.Set),
                    new RenderParams(16, 9, Stretch.Uniform, UIToneMappingOperator.None, 0f, IsSourceLinear: true));
                Assert.That(presented.Wait(TimeSpan.FromSeconds(10)), Is.True, "The frame was not presented.");

                renderer.Resize(64, 36);
                Thread.Sleep(s_rebuildTime);

                Assert.That(
                    objc_msgSend_NSSize(layer, sel_getUid("drawableSize")),
                    Is.EqualTo(new NSSize(64, 36)),
                    $"After rebuild {i}, {elapsed.Elapsed} in.");
            }
        }
        finally
        {
            objc_msgSend(view, sel_getUid("release"));
            objc_autoreleasePoolPop(pool);
        }
    }

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
