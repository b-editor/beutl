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

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr objc_autoreleasePoolPush();

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern void objc_autoreleasePoolPop(IntPtr pool);
}
