using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Vulkan;
using Silk.NET.Vulkan;

namespace Beutl.Graphics3DTests;

[TestFixture]
[NonParallelizable]
public sealed class VulkanTextureTransferTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void CopyTexture_PreservesStorageAndAttachmentDestinations(bool colorAttachment)
    {
        VulkanContext context = GpuTestEnvironment.EnsureVulkanAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            const int width = 3;
            const int height = 2;
            byte[] expected = Enumerable.Range(0, width * height * 4).Select(i => (byte)(i * 7)).ToArray();
            using ITexture2D source = context.CreateTexture2D(width, height, TextureFormat.RGBA8Unorm);
            source.Upload(expected);
            ImageUsageFlags usage = ImageUsageFlags.StorageBit | ImageUsageFlags.SampledBit
                | ImageUsageFlags.TransferSrcBit | ImageUsageFlags.TransferDstBit;
            if (colorAttachment) usage |= ImageUsageFlags.ColorAttachmentBit;
            using var destination = new VulkanTexture2D(context, width, height, TextureFormat.RGBA8Unorm, usage);

            context.CopyTexture(source, destination);

            // Inspect the tracked layout before readback establishes its own transfer layout.
            var layout = (ImageLayout)destination.CreateSkiaImageInfo().ImageLayout;
            Assert.That(layout, Is.EqualTo(colorAttachment
                ? VulkanTexture2D.SkiaInteropLayout : ImageLayout.TransferDstOptimal));
            Assert.That(destination.DownloadPixels(), Is.EqualTo(expected));
        });
    }

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
