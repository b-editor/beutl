using System.Runtime.InteropServices;
using Beutl.Controls;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class HdrBitmapViewEdrMetadataTests
{
    // FitToDisplay hands tone mapping to Core Animation through the layer's EDR metadata, which only a real
    // CAMetalLayer can show: the selectors resolve, the float arguments arrive, and clearing restores raw values.
    [Test]
    public void Fit_to_display_sets_and_clears_the_layer_edr_metadata()
    {
        if (!OperatingSystem.IsMacOS())
            Assert.Ignore("CAMetalLayer exists only on macOS.");

        NativeLibrary.Load("/System/Library/Frameworks/QuartzCore.framework/QuartzCore");
        IntPtr pool = objc_autoreleasePoolPush();
        IntPtr layer = objc_msgSend(objc_getClass("CAMetalLayer"), sel_getUid("new"));
        try
        {
            HdrBitmapView.SetMacOSEdrMetadata(layer, toneMap: true);
            IntPtr metadata = objc_msgSend(layer, sel_getUid("EDRMetadata"));
            Assert.That(metadata, Is.Not.EqualTo(IntPtr.Zero));
            string? description = Marshal.PtrToStringUTF8(
                objc_msgSend(objc_msgSend(metadata, sel_getUid("description")), sel_getUid("UTF8String")));
            Assert.That(description, Does.Contain("min nits: 0.000100").And.Contain("max nits: 1000.000000"));

            HdrBitmapView.SetMacOSEdrMetadata(layer, toneMap: false);
            Assert.That(objc_msgSend(layer, sel_getUid("EDRMetadata")), Is.EqualTo(IntPtr.Zero));
        }
        finally
        {
            objc_msgSend(layer, sel_getUid("release"));
            objc_autoreleasePoolPop(pool);
        }
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
