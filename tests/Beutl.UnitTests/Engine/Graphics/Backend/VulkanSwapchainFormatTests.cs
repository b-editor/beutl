using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using Beutl.Graphics.Backend.Vulkan;
using Beutl.Media;
using Silk.NET.Vulkan;

namespace Beutl.UnitTests.Engine.Graphics.Backend;

[TestFixture]
public class VulkanSwapchainFormatTests
{
    // A UNORM swapchain image stores the present shader's output unconverted.
    [TestCase(Format.B8G8R8A8Unorm)]
    [TestCase(Format.R8G8B8A8Unorm)]
    [TestCase(Format.R8G8B8Unorm)]
    [TestCase(Format.B8G8R8Unorm)]
    [TestCase(Format.A8B8G8R8UnormPack32)]
    [TestCase(Format.A2B10G10R10UnormPack32)]
    [TestCase(Format.A2R10G10B10UnormPack32)]
    [TestCase(Format.R16G16B16A16Unorm)]
    public void UnormFormatsNeedTheShaderToEncodeSrgb(Format format)
    {
        Assert.That(VulkanSwapchain.RequiresShaderSrgbEncoding(format, ColorSpaceKHR.SpaceSrgbNonlinearKhr), Is.True);
    }

    // An SRGB format encodes in hardware, and the HDR float format takes linear values.
    [TestCase(Format.B8G8R8A8Srgb)]
    [TestCase(Format.R8G8B8A8Srgb)]
    [TestCase(Format.A8B8G8R8SrgbPack32)]
    [TestCase(Format.R16G16B16A16Sfloat)]
    public void SrgbAndFloatFormatsTakeTheShaderOutputAsIs(Format format)
    {
        Assert.That(VulkanSwapchain.RequiresShaderSrgbEncoding(format, ColorSpaceKHR.SpaceSrgbNonlinearKhr), Is.False);
    }

    // A linear color space expects linear components, so even a UNORM image takes the output as is.
    [TestCase(ColorSpaceKHR.SpaceExtendedSrgbLinearExt)]
    [TestCase(ColorSpaceKHR.SpaceDisplayP3LinearExt)]
    [TestCase(ColorSpaceKHR.SpaceBT709LinearExt)]
    [TestCase(ColorSpaceKHR.SpaceBT2020LinearExt)]
    [TestCase(ColorSpaceKHR.SpaceAdobergbLinearExt)]
    public void LinearColorSpacesTakeTheShaderOutputAsIs(ColorSpaceKHR colorSpace)
    {
        Assert.That(VulkanSwapchain.RequiresShaderSrgbEncoding(Format.B8G8R8A8Unorm, colorSpace), Is.False);
    }

    // PQ needs a transfer function the shader lacks, and a float image in the sRGB color space would get
    // linear values, so the last resort passes both for a linear BT.709 pair.
    [Test]
    public void TheFallbackSkipsPairsThePresentPathCannotShowAsIntended()
    {
        SurfaceFormatKHR[] formats =
        [
            new(Format.A2B10G10R10UnormPack32, ColorSpaceKHR.SpaceHdr10ST2084Ext),
            new(Format.R16G16B16A16Sfloat, ColorSpaceKHR.SpaceSrgbNonlinearKhr),
            new(Format.A2B10G10R10UnormPack32, ColorSpaceKHR.SpaceBT709LinearExt),
            new(Format.B8G8R8A8Unorm, ColorSpaceKHR.SpaceSrgbNonlinearKhr),
        ];

        SurfaceFormatKHR chosen = VulkanSwapchain.SelectFallbackFormat(formats);
        Assert.That((chosen.Format, chosen.ColorSpace), Is.EqualTo((Format.A2B10G10R10UnormPack32, ColorSpaceKHR.SpaceBT709LinearExt)));
    }

    [Test]
    public void TheFallbackTakesTheFirstFormatWhenNoPairFits()
    {
        SurfaceFormatKHR[] formats =
        [
            new(Format.A2B10G10R10UnormPack32, ColorSpaceKHR.SpaceHdr10ST2084Ext),
            new(Format.A2B10G10R10UnormPack32, ColorSpaceKHR.SpaceHdr10HlgExt),
        ];

        SurfaceFormatKHR chosen = VulkanSwapchain.SelectFallbackFormat(formats);
        Assert.That((chosen.Format, chosen.ColorSpace), Is.EqualTo((Format.A2B10G10R10UnormPack32, ColorSpaceKHR.SpaceHdr10ST2084Ext)));
    }

    // Core Animation clips EDR content to the layer's color space before tone mapping it, so a macOS HDR swapchain
    // takes linear BT.2020 in half floats, and only that pair, when the surface offers it.
    [Test]
    public void TheHdrFormatTakesLinearBt2020WhenPreferred()
    {
        SurfaceFormatKHR[] formats =
        [
            new(Format.B8G8R8A8Srgb, ColorSpaceKHR.SpaceSrgbNonlinearKhr),
            new(Format.R16G16B16A16Sfloat, ColorSpaceKHR.SpaceExtendedSrgbLinearExt),
            new(Format.A2B10G10R10UnormPack32, ColorSpaceKHR.SpaceBT2020LinearExt),
            new(Format.R16G16B16A16Sfloat, ColorSpaceKHR.SpaceBT2020LinearExt),
        ];

        SurfaceFormatKHR? preferred = VulkanSwapchain.SelectHdrFormat(formats, preferBt2020: true);
        SurfaceFormatKHR? plain = VulkanSwapchain.SelectHdrFormat(formats, preferBt2020: false);

        Assert.That(preferred, Is.EqualTo(new SurfaceFormatKHR(Format.R16G16B16A16Sfloat, ColorSpaceKHR.SpaceBT2020LinearExt)));
        Assert.That(plain, Is.EqualTo(new SurfaceFormatKHR(Format.R16G16B16A16Sfloat, ColorSpaceKHR.SpaceExtendedSrgbLinearExt)));
    }

    [Test]
    public void TheHdrFormatFallsBackToExtendedSrgbWithoutBt2020()
    {
        SurfaceFormatKHR[] formats =
        [
            new(Format.B8G8R8A8Srgb, ColorSpaceKHR.SpaceSrgbNonlinearKhr),
            new(Format.R16G16B16A16Sfloat, ColorSpaceKHR.SpaceDisplayP3LinearExt),
            new(Format.R16G16B16A16Sfloat, ColorSpaceKHR.SpaceExtendedSrgbLinearExt),
        ];

        SurfaceFormatKHR? chosen = VulkanSwapchain.SelectHdrFormat(formats, preferBt2020: true);

        Assert.That(chosen, Is.EqualTo(new SurfaceFormatKHR(Format.R16G16B16A16Sfloat, ColorSpaceKHR.SpaceExtendedSrgbLinearExt)));
    }

    [Test]
    public void ThereIsNoHdrFormatWithoutAHalfFloatPair()
    {
        SurfaceFormatKHR[] formats =
        [
            new(Format.B8G8R8A8Srgb, ColorSpaceKHR.SpaceSrgbNonlinearKhr),
            new(Format.A2B10G10R10UnormPack32, ColorSpaceKHR.SpaceHdr10ST2084Ext),
            new(Format.A2B10G10R10UnormPack32, ColorSpaceKHR.SpaceBT2020LinearExt),
        ];

        Assert.That(VulkanSwapchain.SelectHdrFormat(formats, preferBt2020: true), Is.Null);
    }

    // A BT.2020 swapchain gets the present shader's linear sRGB through the matrix BT.2020's primaries give.
    [Test]
    public void TheShaderConvertsToBt2020WithItsPrimaries()
    {
        float[] columns = Regex.Matches(VulkanPresentPipeline.SrgbToBt2020, @"-?\d+\.\d+")
            .Select(m => float.Parse(m.Value, CultureInfo.InvariantCulture))
            .ToArray();
        PreviewGamutMapping bt2020 = PreviewGamutMapping.For(
            new DisplayPrimaries(0.708f, 0.292f, 0.170f, 0.797f, 0.131f, 0.046f), withinSrgb: false);
        Vector3[] rows = [bt2020.ToDisplay0, bt2020.ToDisplay1, bt2020.ToDisplay2];

        Assert.That(columns, Has.Length.EqualTo(9));
        for (int row = 0; row < 3; row++)
        {
            Assert.That(columns[row], Is.EqualTo(rows[row].X).Within(1e-5f), $"row {row}, red");
            Assert.That(columns[3 + row], Is.EqualTo(rows[row].Y).Within(1e-5f), $"row {row}, green");
            Assert.That(columns[6 + row], Is.EqualTo(rows[row].Z).Within(1e-5f), $"row {row}, blue");
        }
    }

    // A paused preview follows its surface's size, which is only a size when the surface reports one: 0xFFFFFFFF
    // leaves it to the swapchain (Wayland), and a minimized window reports zero.
    [TestCase(800u, 400u, true)]
    [TestCase(uint.MaxValue, uint.MaxValue, false)]
    [TestCase(0u, 0u, false)]
    [TestCase(800u, 0u, false)]
    public void OnlyASurfaceThatReportsASizeIsFollowed(uint width, uint height, bool followed)
    {
        Extent2D? extent = VulkanSwapchain.UsableExtent(new Extent2D(width, height));

        Assert.That(extent.HasValue, Is.EqualTo(followed));
        if (followed)
            Assert.That((extent!.Value.Width, extent.Value.Height), Is.EqualTo((width, height)));
    }

    // A layer that is not opaque shows the UI where the present shader draws nothing, around a frame given a
    // destination. A child window on Windows or X11 cannot be blended with its parent, so it stays opaque there.
    [Test]
    public void TheSwapchainBlendsWithTheUiUnderItWhereThePlatformOffersAlpha()
    {
        const CompositeAlphaFlagsKHR all = CompositeAlphaFlagsKHR.OpaqueBitKhr | CompositeAlphaFlagsKHR.PreMultipliedBitKhr
            | CompositeAlphaFlagsKHR.PostMultipliedBitKhr | CompositeAlphaFlagsKHR.InheritBitKhr;

        Assert.Multiple(() =>
        {
            Assert.That(VulkanSwapchain.SelectCompositeAlpha(all, blendsWithUnderlay: true),
                Is.EqualTo(CompositeAlphaFlagsKHR.PreMultipliedBitKhr));
            Assert.That(VulkanSwapchain.SelectCompositeAlpha(all, blendsWithUnderlay: false),
                Is.EqualTo(CompositeAlphaFlagsKHR.OpaqueBitKhr));
            // What MoltenVK 1.4.0 offers.
            Assert.That(VulkanSwapchain.SelectCompositeAlpha(
                    CompositeAlphaFlagsKHR.OpaqueBitKhr | CompositeAlphaFlagsKHR.PostMultipliedBitKhr
                    | CompositeAlphaFlagsKHR.InheritBitKhr, blendsWithUnderlay: true),
                Is.EqualTo(CompositeAlphaFlagsKHR.PostMultipliedBitKhr));
            Assert.That(VulkanSwapchain.SelectCompositeAlpha(CompositeAlphaFlagsKHR.OpaqueBitKhr, blendsWithUnderlay: true),
                Is.EqualTo(CompositeAlphaFlagsKHR.OpaqueBitKhr));
        });
    }
}
