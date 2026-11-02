using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Platform;
using Avalonia.Threading;
using Beutl.Controls;
using Beutl.Graphics.Backend.Vulkan;
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
        RunWithView((view, _) =>
        {
            using Ref<BtlBitmap> source = NewSource();

            view.Source = source;

            Assert.That(source.RefCount, Is.EqualTo(HeldByTheView));
        });
    }

    // Layout runs before the native view takes the size it gives, and it cannot take it any sooner: ancestors still
    // being arranged have their old bounds.
    [AvaloniaTest]
    public void A_new_size_is_drawn_after_layout()
    {
        RunWithView((view, window) =>
        {
            using Ref<BtlBitmap> source = NewSource();
            view.Source = source;

            window.UpdateLayout();

            Assert.That(view.Bounds.Width, Is.GreaterThan(0));
            Assert.That(source.RefCount, Is.EqualTo(HeldByTheView), "Layout queued a frame.");

            Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);

            Assert.That(
                view.WaitForFirstDrawnWidth(), Is.EqualTo(source.Value.Width), "The frame was not drawn after layout.");
        });
    }

    // Playback can deliver a frame after layout and before the draw queued for the new size.
    [AvaloniaTest]
    public void A_frame_arriving_before_the_queued_draw_waits_for_it()
    {
        RunWithView((view, window) =>
        {
            using Ref<BtlBitmap> first = NewSource();
            using Ref<BtlBitmap> next = NewSource(8);
            view.Source = first;
            window.UpdateLayout();

            view.Source = next;

            Assert.That(next.RefCount, Is.EqualTo(HeldByTheView), "The frame did not wait for the queued draw.");

            Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);

            Assert.That(
                view.WaitForFirstDrawnWidth(),
                Is.EqualTo(next.Value.Width),
                "The queued draw did not take the latest frame.");
        });
    }

    // A handler of SizeChanged runs inside layout, before the native view has that size.
    [AvaloniaTest]
    public void A_frame_set_from_a_size_changed_handler_waits_for_the_queued_draw()
    {
        RunWithView((view, window) =>
        {
            using Ref<BtlBitmap> first = NewSource();
            using Ref<BtlBitmap> next = NewSource(8);
            view.SizeChanged += (_, _) => view.Source = next;
            view.Source = first;

            window.UpdateLayout();

            Assert.That(next.RefCount, Is.EqualTo(HeldByTheView), "The frame did not wait for the queued draw.");
        });
    }

    // A view in a shown window, with no source yet and so no size. It goes away even when an assertion fails, or its
    // presentation thread and native view would outlive the test.
    private static void RunWithView(Action<TestView, Window> test)
    {
        RunOnMacOS(() =>
        {
            var view = new TestView();
            IPlatformHandle control = view.Create();
            var window = new Window { Content = view, Width = 200, Height = 100 };
            try
            {
                window.Show();
                test(view, window);
            }
            finally
            {
                window.Close();
                view.Destroy(control);
            }
        });
    }

    private static Ref<BtlBitmap> NewSource(int size = 4)
    {
        return Ref<BtlBitmap>.Create(
            new BtlBitmap(size, size, BitmapColorType.RgbaF16, BitmapAlphaType.Premul, BitmapColorSpace.LinearSrgb));
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

        // The width of the first frame to reach the renderer, or null when none does.
        public float? WaitForFirstDrawnWidth()
        {
            object renderer = typeof(HdrBitmapView).GetField("_renderer", Flags)!.GetValue(this)!;
            FieldInfo lastParams = renderer.GetType().GetField("_lastRenderParams", Flags)!;
            SpinWait.SpinUntil(() => lastParams.GetValue(renderer) != null, TimeSpan.FromSeconds(10));
            return ((RenderParams?)lastParams.GetValue(renderer))?.SourceWidth;
        }
    }

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr objc_autoreleasePoolPush();

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern void objc_autoreleasePoolPop(IntPtr pool);
}
