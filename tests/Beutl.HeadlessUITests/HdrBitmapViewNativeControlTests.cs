using System.Runtime.InteropServices;
using Avalonia.Headless.NUnit;
using Avalonia.Platform;
using Beutl.Configuration;
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

    // On macOS the view settles Auto by its screen: Core Animation tone maps on a screen that shows HDR, and one that shows
    // nothing above SDR white clips as the SDR preview does. A mode chosen outright does not follow the screen.
    [AvaloniaTest]
    public void Auto_follows_whether_the_screen_shows_hdr()
    {
        RunOnMacOS(() =>
        {
            var view = new TestView { ToneMapping = UIToneMappingOperator.Auto };
            IPlatformHandle control = view.Create();
            try
            {
                // A view without a window has no screen yet, and counts as on one that shows HDR.
                Assert.That(view.EffectiveToneMapping, Is.EqualTo(UIToneMappingOperator.FitToDisplay));
                Assert.That(HasEdrMetadata(control), Is.True);

                Assert.That(view.SetScreenHeadroom(1.0), Is.True, "an SDR screen changes what Auto stands for");
                Assert.That(view.EffectiveToneMapping, Is.EqualTo(UIToneMappingOperator.None));
                Assert.That(HasEdrMetadata(control), Is.False);
                Assert.That(view.SetScreenHeadroom(1.0), Is.False, "the same screen again changes nothing");

                view.ToneMapping = UIToneMappingOperator.FitToDisplay;
                Assert.That(view.EffectiveToneMapping, Is.EqualTo(UIToneMappingOperator.FitToDisplay));
                Assert.That(HasEdrMetadata(control), Is.True);
                Assert.That(view.SetScreenHeadroom(2.0), Is.False, "a mode chosen outright does not follow the screen");

                view.ToneMapping = UIToneMappingOperator.Auto;
                Assert.That(view.EffectiveToneMapping, Is.EqualTo(UIToneMappingOperator.FitToDisplay));
                Assert.That(HasEdrMetadata(control), Is.True);
            }
            finally
            {
                view.Destroy(control);
            }
        });
    }

    private static bool HasEdrMetadata(IPlatformHandle control)
    {
        IntPtr layer = objc_msgSend(control.Handle, sel_getUid("layer"));
        return objc_msgSend(layer, sel_getUid("EDRMetadata")) != IntPtr.Zero;
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
    private static extern IntPtr sel_getUid(string selector);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr objc_autoreleasePoolPush();

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern void objc_autoreleasePoolPop(IntPtr pool);
}
