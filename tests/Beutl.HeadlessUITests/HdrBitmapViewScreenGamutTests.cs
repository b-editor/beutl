using System.Runtime.InteropServices;
using Beutl.Controls;
using Beutl.Media;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class HdrBitmapViewScreenGamutTests
{
    // FitToDisplay reads the screen's primaries from its NSColorSpace through ColorSync, which only real color spaces
    // can show: P3 comes back with the components sRGB lacks, sRGB as it is, and a gray space with none.
    [Test]
    public void Screen_primaries_come_from_its_color_space()
    {
        if (!OperatingSystem.IsMacOS())
            Assert.Ignore("NSColorSpace exists only on macOS.");

        NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit");
        IntPtr pool = objc_autoreleasePoolPush();
        try
        {
            IntPtr colorSpaceClass = objc_getClass("NSColorSpace");

            DisplayPrimaries? p3 = HdrBitmapView.GetMacOSPrimaries(
                objc_msgSend(colorSpaceClass, sel_getUid("displayP3ColorSpace")));
            AssertPrimaries(p3, new DisplayPrimaries(0.680f, 0.320f, 0.265f, 0.690f, 0.150f, 0.060f));

            DisplayPrimaries? srgb = HdrBitmapView.GetMacOSPrimaries(
                objc_msgSend(colorSpaceClass, sel_getUid("sRGBColorSpace")));
            AssertPrimaries(srgb, DisplayPrimaries.Srgb);

            DisplayPrimaries? gray = HdrBitmapView.GetMacOSPrimaries(
                objc_msgSend(colorSpaceClass, sel_getUid("genericGrayColorSpace")));
            Assert.That(gray, Is.Null);
        }
        finally
        {
            objc_autoreleasePoolPop(pool);
        }
    }

    // Auto reads whether a screen shows HDR from its potential EDR headroom, which a screen reports as 1.0 or more. No
    // screen counts as one that shows none.
    [Test]
    public void Screen_headroom_comes_from_the_nsscreen()
    {
        if (!OperatingSystem.IsMacOS())
            Assert.Ignore("NSScreen exists only on macOS.");

        NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit");
        IntPtr pool = objc_autoreleasePoolPush();
        try
        {
            Assert.That(HdrBitmapView.GetMacOSScreenHeadroom(IntPtr.Zero), Is.EqualTo(1.0));

            IntPtr screens = objc_msgSend(objc_getClass("NSScreen"), sel_getUid("screens"));
            long count = (long)objc_msgSend(screens, sel_getUid("count"));
            if (count == 0)
                Assert.Ignore("No screen is attached.");

            for (long i = 0; i < count; i++)
            {
                IntPtr screen = objc_msgSend_index(screens, sel_getUid("objectAtIndex:"), (nuint)i);
                double headroom = HdrBitmapView.GetMacOSScreenHeadroom(screen);
                TestContext.Out.WriteLine($"Screen {i}: potential EDR headroom {headroom}");
                Assert.That(headroom, Is.GreaterThanOrEqualTo(1.0).And.LessThan(1000.0));
            }
        }
        finally
        {
            objc_autoreleasePoolPop(pool);
        }
    }

    private static void AssertPrimaries(DisplayPrimaries? actual, DisplayPrimaries expected)
    {
        Assert.That(actual, Is.Not.Null);
        DisplayPrimaries value = actual!.Value;
        Assert.That(value.RedX, Is.EqualTo(expected.RedX).Within(1e-3f));
        Assert.That(value.RedY, Is.EqualTo(expected.RedY).Within(1e-3f));
        Assert.That(value.GreenX, Is.EqualTo(expected.GreenX).Within(1e-3f));
        Assert.That(value.GreenY, Is.EqualTo(expected.GreenY).Within(1e-3f));
        Assert.That(value.BlueX, Is.EqualTo(expected.BlueX).Within(1e-3f));
        Assert.That(value.BlueY, Is.EqualTo(expected.BlueY).Within(1e-3f));
    }

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr objc_getClass(string className);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr sel_getUid(string selector);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_index(IntPtr receiver, IntPtr selector, nuint index);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr objc_autoreleasePoolPush();

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern void objc_autoreleasePoolPop(IntPtr pool);
}
