using System.Runtime.InteropServices;
using Beutl.Graphics.Backend.Vulkan;
using Beutl.Media;

namespace Beutl.UnitTests.Engine;

[TestFixture]
public class PresentLuminanceTests
{
    // Windows shows scRGB 1.0 at 80 nit on an HDR display, so reference white is written as SDR white over 80 nit,
    // and the headroom is the display's peak over SDR white.
    [Test]
    public void ForScRgbHdr_WritesReferenceWhiteAsSdrWhite()
    {
        PresentLuminance luminance = PresentLuminance.ForScRgbHdr(sdrWhiteNits: 240f, maxLuminanceNits: 1000f);

        Assert.That(luminance.ReferenceWhite, Is.EqualTo(3f).Within(1e-6f));
        Assert.That(luminance.Headroom, Is.EqualTo(1000f / 240f).Within(1e-6f));
    }

    // Without a white level, 1.0 stays at 80 nit; without a peak, the headroom is unknown.
    [TestCase(0f, 1000f, 1f, 12.5f)]
    [TestCase(float.NaN, 1000f, 1f, 12.5f)]
    [TestCase(200f, 0f, 2.5f, 0f)]
    public void ForScRgbHdr_UnknownValues(float sdrWhiteNits, float maxLuminanceNits, float referenceWhite, float headroom)
    {
        PresentLuminance luminance = PresentLuminance.ForScRgbHdr(sdrWhiteNits, maxLuminanceNits);

        Assert.That(luminance.ReferenceWhite, Is.EqualTo(referenceWhite).Within(1e-6f));
        Assert.That(luminance.Headroom, Is.EqualTo(headroom).Within(1e-6f));
    }

    // FitToDisplay rolls off into the display's peak where it is below the content's, and leaves values alone where
    // it is unknown or holds the content.
    [Test]
    public void Headroom_DecidesTheDisplayMapping()
    {
        PreviewDisplayMapping dim = PreviewDisplayMapping.For(PresentLuminance.ForScRgbHdr(240f, 600f).Headroom);

        Assert.That(dim, Is.EqualTo(PreviewDisplayMapping.For(2.5f)));
        Assert.That(dim, Is.Not.EqualTo(PreviewDisplayMapping.Identity));
        Assert.That(PreviewDisplayMapping.For(PresentLuminance.ForScRgbHdr(200f, 1000f).Headroom),
            Is.EqualTo(PreviewDisplayMapping.Identity));
        Assert.That(PreviewDisplayMapping.For(PresentLuminance.Unknown.Headroom),
            Is.EqualTo(PreviewDisplayMapping.Identity));
        Assert.That(PreviewDisplayMapping.For(PresentLuminance.Sdr.Headroom), Is.EqualTo(PreviewDisplayMapping.For(1f)));
    }

    // The structs WindowsDisplayLuminance hands to DXGI and the display configuration keep the layout of their C
    // declarations on 64-bit Windows.
    [Test]
    public unsafe void WindowsInteropStructs_MatchTheirCLayouts()
    {
        Assert.That(sizeof(WindowsDisplayLuminance.DisplayConfigPathInfo), Is.EqualTo(72));
        Assert.That(sizeof(WindowsDisplayLuminance.DisplayConfigModeInfo), Is.EqualTo(64));
        Assert.That(sizeof(WindowsDisplayLuminance.DisplayConfigSourceDeviceName), Is.EqualTo(84));
        Assert.That(sizeof(WindowsDisplayLuminance.DisplayConfigSdrWhiteLevel), Is.EqualTo(24));
        Assert.That(sizeof(WindowsDisplayLuminance.DxgiOutputDesc1), Is.EqualTo(152));

        WindowsDisplayLuminance.DxgiOutputDesc1 desc = default;
        byte* start = (byte*)&desc;
#pragma warning disable CA1416 // Only offsets are taken; nothing here calls Windows.
        Assert.That((byte*)&desc.Monitor - start, Is.EqualTo(88));
        Assert.That((byte*)&desc.ColorSpace - start, Is.EqualTo(100));
        Assert.That((byte*)desc.Primaries - start, Is.EqualTo(104));
        Assert.That((byte*)&desc.MaxLuminance - start, Is.EqualTo(140));
#pragma warning restore CA1416
    }

    // The real queries, for the display of the desktop window, which has no HDR settings to change between them.
    [Test]
    public void WindowsDisplayLuminance_ReadsTheDesktopDisplayOnce()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("DXGI exists only on Windows.");
            return;
        }

        using var display = new WindowsDisplayLuminance(GetDesktopWindow());
        display.Refresh();
        TestContext.Out.WriteLine(display.Current);

        Assert.That(display.Refresh(), Is.False);
    }

    [DllImport("user32.dll")]
    private static extern nint GetDesktopWindow();
}
