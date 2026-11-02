using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Platform;
using Avalonia.Threading;
using Beutl.Controls;
using Beutl.Media;
using Beutl.Media.Source;
using BtlBitmap = Beutl.Media.Bitmap;

namespace Beutl.HeadlessUITests;

// A frame drawn before the native view has the view's size is stretched over it once it takes that size. A headless
// window has no native control host to give it one, so these watch when a frame leaves for the renderer: it holds a
// reference to the source until it is drawn. They create the control themselves, which only macOS does without a
// parent.
[TestFixture]
public class HdrBitmapViewFirstFrameTests
{
    // The source's own reference and the view's clone.
    private const int HeldByTheView = 2;

    [AvaloniaTest]
    public void A_view_without_a_size_queues_no_frame()
    {
        RunOnMacOS(() =>
        {
            using Ref<BtlBitmap> source = NewSource();
            var view = new TestView();
            IPlatformHandle control = view.Create();

            view.Source = source;

            Assert.That(source.RefCount, Is.EqualTo(HeldByTheView));
            view.Destroy(control);
        });
    }

    // Layout runs before the native view takes the size it gives, and it cannot take it any sooner: ancestors still
    // being arranged have their old bounds.
    [AvaloniaTest]
    public void A_new_size_is_drawn_after_layout()
    {
        RunOnMacOS(() =>
        {
            using Ref<BtlBitmap> source = NewSource();
            var view = new TestView();
            IPlatformHandle control = view.Create();
            var window = new Window { Content = view, Width = 200, Height = 100 };
            window.Show();
            view.Source = source;

            window.UpdateLayout();

            Assert.That(view.Bounds.Width, Is.GreaterThan(0));
            Assert.That(source.RefCount, Is.EqualTo(HeldByTheView), "Layout queued a frame.");

            Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);

            Assert.That(view.WaitForFirstDraw(), Is.True, "The frame was not drawn after layout.");
            window.Close();
            view.Destroy(control);
        });
    }

    // Playback can deliver a frame after layout and before the draw queued for the new size.
    [AvaloniaTest]
    public void A_frame_arriving_before_the_queued_draw_waits_for_it()
    {
        RunOnMacOS(() =>
        {
            using Ref<BtlBitmap> first = NewSource();
            using Ref<BtlBitmap> next = NewSource();
            var view = new TestView();
            IPlatformHandle control = view.Create();
            var window = new Window { Content = view, Width = 200, Height = 100 };
            window.Show();
            view.Source = first;
            window.UpdateLayout();

            view.Source = next;

            Assert.That(next.RefCount, Is.EqualTo(HeldByTheView), "The frame did not wait for the queued draw.");

            Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);

            Assert.That(view.WaitForFirstDraw(), Is.True, "The frame was not drawn by the queued draw.");
            window.Close();
            view.Destroy(control);
        });
    }

    private static Ref<BtlBitmap> NewSource()
    {
        return Ref<BtlBitmap>.Create(
            new BtlBitmap(4, 4, BitmapColorType.RgbaF16, BitmapAlphaType.Premul, BitmapColorSpace.LinearSrgb));
    }

    private static void RunOnMacOS(Action test)
    {
        if (!OperatingSystem.IsMacOS())
            Assert.Ignore("Only macOS creates this control without a parent to attach to.");

        TestGpu.RequireAnyDevice();
        NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit");

        IntPtr pool = objc_autoreleasePoolPush();
        try
        {
            test();
        }
        finally
        {
            objc_autoreleasePoolPop(pool);
        }
    }

    private sealed class TestView : HdrBitmapView
    {
        private const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;

        public IPlatformHandle Create() => CreateNativeControlCore(new PlatformHandle(IntPtr.Zero, "NSView"));

        public void Destroy(IPlatformHandle control) => DestroyNativeControlCore(control);

        public bool WaitForFirstDraw()
        {
            object renderer = typeof(HdrBitmapView).GetField("_renderer", Flags)!.GetValue(this)!;
            FieldInfo lastParams = renderer.GetType().GetField("_lastRenderParams", Flags)!;
            return SpinWait.SpinUntil(() => lastParams.GetValue(renderer) != null, TimeSpan.FromSeconds(10));
        }
    }

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr objc_autoreleasePoolPush();

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern void objc_autoreleasePoolPop(IntPtr pool);
}
