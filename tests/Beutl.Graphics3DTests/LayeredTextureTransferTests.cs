using System.Runtime.InteropServices;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Composite;
using Beutl.Graphics.Backend.Vulkan;
using Silk.NET.Vulkan;

namespace Beutl.Graphics3DTests;

[TestFixture]
[NonParallelizable]
public sealed class LayeredTextureTransferTests
{
    private const int Size = 4;

    public enum TextureKind
    {
        Cube,
        Array,
        CubeArray,
    }

    [TestCase(TextureKind.Cube)]
    [TestCase(TextureKind.Array)]
    [Category("GpuPassFusionGpu")]
    public void UploadColorLayer_PreservesPixels(TextureKind kind)
    {
        VulkanContext context = RequireVulkanContext();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            byte[] expected = CreatePixels();
            using IDisposable texture = CreateTexture(context, kind, out Image image, out uint layer);
            switch (texture)
            {
                case ITextureCube cube:
                    cube.UploadFace((int)layer, expected);
                    break;
                case ITextureArray array:
                    array.UploadLayer(layer, expected);
                    break;
            }

            Assert.That(ReadLayer(context, image, layer), Is.EqualTo(expected));
        });
    }

    [TestCase(TextureKind.Cube)]
    [TestCase(TextureKind.Array)]
    [TestCase(TextureKind.CubeArray)]
    [Category("GpuPassFusionGpu")]
    public void CopyToColorLayer_PreservesPixels(TextureKind kind)
    {
        VulkanContext context = RequireVulkanContext();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            byte[] expected = CreatePixels();
            using ITexture2D source = context.CreateTexture2D(Size, Size, TextureFormat.RGBA8Unorm);
            source.Upload(expected);
            using IDisposable texture = CreateTexture(context, kind, out Image image, out uint layer);
            switch (texture)
            {
                case ITextureCube cube:
                    context.CopyTextureToCubeFace(source, cube, (int)layer);
                    break;
                case ITextureArray array:
                    context.CopyTextureToArrayLayer(source, array, (int)layer);
                    break;
                case ITextureCubeArray cubeArray:
                    context.CopyTextureToCubeArrayFace(source, cubeArray, (int)(layer / 6), (int)(layer % 6));
                    break;
            }

            Assert.That(ReadLayer(context, image, layer), Is.EqualTo(expected));
        });
    }

    private static IDisposable CreateTexture(
        VulkanContext context, TextureKind kind, out Image image, out uint layer)
    {
        switch (kind)
        {
            case TextureKind.Cube:
                var cube = (VulkanTextureCube)context.CreateTextureCube(Size, TextureFormat.RGBA8Unorm);
                image = cube.ImageHandle;
                layer = 4;
                return cube;
            case TextureKind.Array:
                var array = (VulkanTextureArray)context.CreateTextureArray(Size, Size, 2, TextureFormat.RGBA8Unorm);
                image = array.ImageHandle;
                layer = 1;
                return array;
            case TextureKind.CubeArray:
                var cubeArray = (VulkanTextureCubeArray)context.CreateTextureCubeArray(Size, 2, TextureFormat.RGBA8Unorm);
                image = cubeArray.ImageHandle;
                layer = 10;
                return cubeArray;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    private static byte[] CreatePixels()
    {
        var pixels = new byte[Size * Size * 4];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = (byte)(i * 3 + 7);
        return pixels;
    }

    private static unsafe byte[] ReadLayer(VulkanContext context, Image image, uint layer)
    {
        var pixels = new byte[Size * Size * 4];
        using IBuffer buffer = context.CreateBuffer(
            (ulong)pixels.Length,
            BufferUsage.TransferDestination,
            MemoryProperty.HostVisible | MemoryProperty.HostCoherent);
        context.TransitionImageLayout(
            image, ImageLayout.ShaderReadOnlyOptimal, ImageLayout.TransferSrcOptimal,
            ImageAspectFlags.ColorBit, layer, 1);
        context.RecordCommands(commandBuffer =>
        {
            var region = new BufferImageCopy
            {
                ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, layer, 1),
                ImageExtent = new Extent3D(Size, Size, 1),
            };
            context.Vk.CmdCopyImageToBuffer(
                commandBuffer, image, ImageLayout.TransferSrcOptimal, ((VulkanBuffer)buffer).Handle, 1, &region);
        });
        context.TransitionImageLayout(
            image, ImageLayout.TransferSrcOptimal, ImageLayout.ShaderReadOnlyOptimal,
            ImageAspectFlags.ColorBit, layer, 1);
        context.FlushCommands(waitForCompletion: true);

        IntPtr mapped = buffer.Map();
        try
        {
            Marshal.Copy(mapped, pixels, 0, pixels.Length);
        }
        finally
        {
            buffer.Unmap();
        }
        return pixels;
    }

    private static VulkanContext RequireVulkanContext()
        => GpuTestEnvironment.EnsureAvailable() switch
        {
            VulkanContext vulkan => vulkan,
            CompositeContext composite => composite.Vulkan,
            _ => throw new InvalidOperationException("The graphics context has no Vulkan backend."),
        };
}
