using System.Runtime.InteropServices;
using Beutl.Graphics.Backend.Vulkan;
using Beutl.Media;
using ColorMode = Beutl.Graphics.Backend.Vulkan.WindowsDisplayLuminance.DisplayConfigAdvancedColorMode;

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
        Assert.That(sizeof(WindowsDisplayLuminance.DisplayConfigGetAdvancedColorInfo), Is.EqualTo(32));
        Assert.That(sizeof(WindowsDisplayLuminance.DisplayConfigGetAdvancedColorInfo2), Is.EqualTo(36));
        Assert.That(sizeof(WindowsDisplayLuminance.DxgiOutputDesc1), Is.EqualTo(152));

        WindowsDisplayLuminance.DxgiOutputDesc1 desc = default;
        byte* start = (byte*)&desc;
        WindowsDisplayLuminance.DisplayConfigGetAdvancedColorInfo2 info = default;
        byte* infoStart = (byte*)&info;
#pragma warning disable CA1416 // Only layouts are read; nothing here calls Windows.
        Assert.That((byte*)&desc.Monitor - start, Is.EqualTo(88));
        Assert.That((byte*)&desc.ColorSpace - start, Is.EqualTo(100));
        Assert.That((byte*)desc.Primaries - start, Is.EqualTo(104));
        Assert.That((byte*)&desc.MaxLuminance - start, Is.EqualTo(140));
        Assert.That((byte*)&info.Value - infoStart, Is.EqualTo(20));
        Assert.That((byte*)&info.ActiveColorMode - infoStart, Is.EqualTo(32));
        Assert.That((int)ColorMode.Wcg, Is.EqualTo(1));
        Assert.That((int)ColorMode.Hdr, Is.EqualTo(2));
#pragma warning restore CA1416
    }

    // Before Windows 11 24H2 the mode is only flagged: advanced color is off in SDR mode, and on in WCG mode on a
    // display that enforces wide color and in HDR mode on any other.
    [Test]
    public void AdvancedColorInfo_ReadsTheModeFromItsFlags()
    {
#pragma warning disable CA1416 // Only flags are read; nothing here calls Windows.
        Assert.That(ModeOf(0b0000), Is.EqualTo(ColorMode.Sdr));
        // An SDR display that auto color management can take on, with it off and on.
        Assert.That(ModeOf(0b0101), Is.EqualTo(ColorMode.Sdr));
        Assert.That(ModeOf(0b0111), Is.EqualTo(ColorMode.Wcg));
        // An HDR display with HDR off and on.
        Assert.That(ModeOf(0b0001), Is.EqualTo(ColorMode.Sdr));
        Assert.That(ModeOf(0b0011), Is.EqualTo(ColorMode.Hdr));
#pragma warning restore CA1416
    }

    // In SDR mode Windows clips colors to sRGB, unless auto color management (WCG) converts them to the display's
    // primaries. A mode that cannot be read, or primaries that describe no display, leave colors to Windows.
    [Test]
    public void GamutInSdrMode_FitsColorsWhereWindowsClipsThem()
    {
        var displayP3 = new DisplayPrimaries(0.680f, 0.320f, 0.265f, 0.690f, 0.150f, 0.060f);
#pragma warning disable CA1416 // Only the mapping is chosen; nothing here calls Windows.
        PreviewGamutMapping wcg = WindowsDisplayLuminance.GamutInSdrMode(ColorMode.Wcg, displayP3);

        Assert.That(WindowsDisplayLuminance.GamutInSdrMode(ColorMode.Sdr, displayP3),
            Is.EqualTo(PreviewGamutMapping.Srgb));
        Assert.That(wcg, Is.EqualTo(PreviewGamutMapping.For(displayP3, withinSrgb: false)));
        Assert.That(wcg.IsEnabled, Is.True);
        Assert.That(WindowsDisplayLuminance.GamutInSdrMode(ColorMode.Wcg, default),
            Is.EqualTo(PreviewGamutMapping.None));
        Assert.That(WindowsDisplayLuminance.GamutInSdrMode(ColorMode.Hdr, displayP3),
            Is.EqualTo(PreviewGamutMapping.None));
        Assert.That(WindowsDisplayLuminance.GamutInSdrMode(null, displayP3), Is.EqualTo(PreviewGamutMapping.None));
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
        TestContext.Out.WriteLine($"{display.Current}, {display.Gamut}");

        Assert.That(display.Refresh(), Is.False);
    }

#pragma warning disable CA1416 // Only flags are read; nothing here calls Windows.
    private static ColorMode ModeOf(uint value)
    {
        return new WindowsDisplayLuminance.DisplayConfigGetAdvancedColorInfo { Value = value }.ColorMode;
    }
#pragma warning restore CA1416

    [DllImport("user32.dll")]
    private static extern nint GetDesktopWindow();
}
