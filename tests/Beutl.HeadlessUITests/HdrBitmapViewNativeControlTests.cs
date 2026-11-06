using System.Runtime.InteropServices;
using Avalonia.Headless.NUnit;
using Avalonia.Platform;
using Beutl.Controls;
using Beutl.Media;
using Beutl.Media.Source;
using BtlBitmap = Beutl.Media.Bitmap;

namespace Beutl.HeadlessUITests;

// A headless window has no native control host, so these call CreateNativeControlCore themselves. On macOS it takes no
// parent and a surface only needs an NSView, which is why they run nowhere else.
[TestFixture]
public class HdrBitmapViewNativeControlTests
{
    // Attaching a view again changes nothing about Source, so a new native control has to take the frame back itself.
    [AvaloniaTest]
    public void A_new_native_control_takes_the_source_back()
    {
        RunOnMacOS(() =>
        {
            using Ref<BtlBitmap> source = NewSource();
            var view = new TestView { Source = source };

            IPlatformHandle first = view.Create();
            Assert.That(source.RefCount, Is.EqualTo(2), "the first control keeps the clone made when Source was set");

            view.Destroy(first);
            Assert.That(source.RefCount, Is.EqualTo(1), "destroying the control releases the frame");

            IPlatformHandle second = view.Create();
            Assert.That(source.RefCount, Is.EqualTo(2), "the next control takes the frame back");

            view.Destroy(second);
            Assert.That(source.RefCount, Is.EqualTo(1));
        });
    }

    [AvaloniaTest]
    public void A_source_disposed_while_out_of_the_tree_leaves_nothing_to_take()
    {
        RunOnMacOS(() =>
        {
            Ref<BtlBitmap> source = NewSource();
            var view = new TestView { Source = source };
            view.Destroy(view.Create());
            source.Dispose();

            IPlatformHandle control = view.Create();

            Assert.That(source.RefCount, Is.Zero);
            view.Destroy(control);
        });
    }

    [AvaloniaTest]
    public void A_source_set_while_out_of_the_tree_is_cloned_once()
    {
        RunOnMacOS(() =>
        {
            using Ref<BtlBitmap> old = NewSource();
            using Ref<BtlBitmap> current = NewSource();
            var view = new TestView { Source = old };
            view.Destroy(view.Create());
            view.Source = current;

            IPlatformHandle control = view.Create();

            Assert.Multiple(() =>
            {
                Assert.That(old.RefCount, Is.EqualTo(1));
                Assert.That(current.RefCount, Is.EqualTo(2));
            });

            view.Destroy(control);
            Assert.That(current.RefCount, Is.EqualTo(1));
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
        public IPlatformHandle Create() => CreateNativeControlCore(new PlatformHandle(IntPtr.Zero, "NSView"));

        public void Destroy(IPlatformHandle control) => DestroyNativeControlCore(control);
    }

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr objc_autoreleasePoolPush();

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern void objc_autoreleasePoolPop(IntPtr pool);
}
