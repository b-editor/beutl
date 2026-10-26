using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Vulkan;

namespace Beutl.Graphics3DTests;

[TestFixture]
[NonParallelizable]
public sealed class VulkanTextureTransferTests
{
    [TestCase(TextureFormat.RGBA8Unorm, 4)]
    [TestCase(TextureFormat.BGRA8Unorm, 4)]
    [TestCase(TextureFormat.RGBA16Float, 8)]
    [TestCase(TextureFormat.RGBA32Float, 16)]
    [TestCase(TextureFormat.R8Unorm, 1)]
    [TestCase(TextureFormat.R16Float, 2)]
    [TestCase(TextureFormat.R32Float, 4)]
    [TestCase(TextureFormat.RG32Float, 8)]
    [TestCase(TextureFormat.R32SInt, 4)]
    [TestCase(TextureFormat.R32UInt, 4)]
    [TestCase(TextureFormat.RGBA32SInt, 16)]
    [TestCase(TextureFormat.RGBA32UInt, 16)]
    public void DownloadPixels_PreservesEveryTexel(TextureFormat format, int bytesPerTexel)
    {
        VulkanContext context = GpuTestEnvironment.EnsureVulkanAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            const int width = 3;
            const int height = 2;
            byte[] expected = Enumerable.Range(0, width * height * bytesPerTexel)
                .Select(i => (byte)((i * 37 + 11) & 0xff))
                .ToArray();
            using ITexture2D texture = context.CreateTexture2D(width, height, format);
            texture.Upload(expected);

            Assert.That(texture.DownloadPixels(), Is.EqualTo(expected));

            // Reusing the texture also exercises the layout restored after the first download.
            Array.Reverse(expected);
            texture.Upload(expected);
            Assert.That(texture.DownloadPixels(), Is.EqualTo(expected));
        });
    }
}
