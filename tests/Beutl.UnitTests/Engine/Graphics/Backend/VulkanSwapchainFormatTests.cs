using Beutl.Graphics.Backend.Vulkan;
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
}
