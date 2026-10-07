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
        Assert.That(VulkanSwapchain.RequiresShaderSrgbEncoding(format), Is.True);
    }

    // An SRGB format encodes in hardware, and the HDR float format takes linear values.
    [TestCase(Format.B8G8R8A8Srgb)]
    [TestCase(Format.R8G8B8A8Srgb)]
    [TestCase(Format.A8B8G8R8SrgbPack32)]
    [TestCase(Format.R16G16B16A16Sfloat)]
    public void SrgbAndFloatFormatsTakeTheShaderOutputAsIs(Format format)
    {
        Assert.That(VulkanSwapchain.RequiresShaderSrgbEncoding(format), Is.False);
    }
}
