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

    // The editor settings' peak stands in for the one the display reports, against the same SDR white, and fills in
    // a peak the display leaves out.
    [TestCase(240f, 1000f, 600f, 2.5f)]
    [TestCase(200f, 0f, 800f, 4f)]
    [TestCase(0f, 1000f, 400f, 5f)]
    public void ForScRgbHdr_PeakOverrideReplacesTheReportedPeak(
        float sdrWhiteNits, float peakNits, float peakOverrideNits, float headroom)
    {
        var report = new HdrDisplayReport(sdrWhiteNits, peakNits);

        PresentLuminance luminance = PresentLuminance.ForScRgbHdr(report, peakOverrideNits);

        Assert.That(luminance.ReferenceWhite,
            Is.EqualTo(PresentLuminance.ForScRgbHdr(sdrWhiteNits, peakNits).ReferenceWhite));
        Assert.That(luminance.Headroom, Is.EqualTo(headroom).Within(1e-6f));
    }

    // Without an override (0, the default) or with one that is not a positive number, the reported peak decides.
    [TestCase(0f)]
    [TestCase(-100f)]
    [TestCase(float.NaN)]
    public void ForScRgbHdr_WithoutAPeakOverrideUsesTheReportedPeak(float peakOverrideNits)
    {
        var report = new HdrDisplayReport(SdrWhiteNits: 240f, PeakNits: 1000f);

        Assert.That(PresentLuminance.ForScRgbHdr(report, peakOverrideNits),
            Is.EqualTo(PresentLuminance.ForScRgbHdr(sdrWhiteNits: 240f, maxLuminanceNits: 1000f)));
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
        TestContext.Out.WriteLine($"{display.Current} {display.Report}");

        Assert.That(display.Refresh(), Is.False);

        // A peak override reads the display again and changes only an HDR one, against the white it reports.
        PresentLuminance reported = display.Current;
        display.Refresh(peakLuminanceOverride: 600f);
        Assert.That(display.Current,
            Is.EqualTo(display.Report is { } report ? PresentLuminance.ForScRgbHdr(report, 600f) : reported));
        Assert.That(display.Refresh(peakLuminanceOverride: 600f), Is.False);
    }

    [DllImport("user32.dll")]
    private static extern nint GetDesktopWindow();
}
