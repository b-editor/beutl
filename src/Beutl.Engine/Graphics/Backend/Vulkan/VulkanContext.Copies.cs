using Silk.NET.Vulkan;

namespace Beutl.Graphics.Backend.Vulkan;

using Image = Silk.NET.Vulkan.Image;

internal sealed unsafe partial class VulkanContext
{
    public unsafe void CopyBuffer(IBuffer source, IBuffer destination, ulong size)
    {
        var vulkanSource = RequireOwned<VulkanBuffer>(source, nameof(source));
        var vulkanDest = RequireOwned<VulkanBuffer>(destination, nameof(destination));

        RecordCommands(cmd =>
        {
            var copyRegion = new BufferCopy { Size = size };
            Vk.CmdCopyBuffer(cmd, vulkanSource.Handle, vulkanDest.Handle, 1, &copyRegion);
        });
    }


    public unsafe void CopyTexture(ITexture2D source, ITexture2D destination)
    {
        var vulkanSource = RequireOwned<VulkanTexture2D>(source, nameof(source));
        var vulkanDest = RequireOwned<VulkanTexture2D>(destination, nameof(destination));

        vulkanSource.TransitionTo(ImageLayout.TransferSrcOptimal);

        // Track both layouts through the deferred recording batch.
        vulkanDest.TransitionTo(ImageLayout.TransferDstOptimal);

        RecordCommands(cmd =>
        {
            // Use blit for format conversion (RGBA8 -> BGRA8)
            var blitRegion = new ImageBlit
            {
                SrcSubresource = new ImageSubresourceLayers
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    MipLevel = 0,
                    BaseArrayLayer = 0,
                    LayerCount = 1
                },
                DstSubresource = new ImageSubresourceLayers
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    MipLevel = 0,
                    BaseArrayLayer = 0,
                    LayerCount = 1
                }
            };

            blitRegion.SrcOffsets[0] = new Offset3D(0, 0, 0);
            blitRegion.SrcOffsets[1] = new Offset3D(source.Width, source.Height, 1);
            blitRegion.DstOffsets[0] = new Offset3D(0, 0, 0);
            blitRegion.DstOffsets[1] = new Offset3D(destination.Width, destination.Height, 1);

            Vk.CmdBlitImage(
                cmd,
                vulkanSource.ImageHandle,
                ImageLayout.TransferSrcOptimal,
                vulkanDest.ImageHandle,
                ImageLayout.TransferDstOptimal,
                1,
                &blitRegion,
                Filter.Nearest);
        });

        vulkanDest.MarkContentsUnknown();

        // Skia-writable destinations must land in the layout their wrap declares. Storage-only
        // destinations retain the transfer layout until their next use establishes its layout.
        if ((vulkanDest.Usage & ImageUsageFlags.ColorAttachmentBit) != 0)
            vulkanDest.TransitionTo(VulkanTexture2D.SkiaInteropLayout);

        vulkanSource.TransitionTo(ImageLayout.ShaderReadOnlyOptimal);
    }

    public unsafe void CopyTextureToCubeFace(ITexture2D source, ITextureCube destination, int faceIndex)
    {
        VulkanTextureCube.ThrowIfFaceOutOfRange(faceIndex);

        var vulkanSource = RequireOwned<VulkanTexture2D>(source, nameof(source));
        var vulkanDest = RequireOwned<VulkanTextureCube>(destination, nameof(destination));

        vulkanSource.TransitionTo(ImageLayout.TransferSrcOptimal);
        vulkanDest.TransitionFaceToTransferDestination(faceIndex);

        RecordCopyToArrayLayer(
            vulkanSource.ImageHandle,
            vulkanDest.ImageHandle,
            GetCopyAspectMask(source.Format),
            (uint)faceIndex,
            (uint)source.Width,
            (uint)source.Height);

        vulkanDest.TransitionFaceToSampled(faceIndex);

        vulkanSource.TransitionTo(ImageLayout.ShaderReadOnlyOptimal);
    }

    public unsafe void CopyTextureToArrayLayer(ITexture2D source, ITextureArray destination, int layerIndex)
    {
        if (layerIndex < 0 || layerIndex >= (int)destination.ArraySize)
            throw new ArgumentOutOfRangeException(nameof(layerIndex), $"Layer index must be 0-{destination.ArraySize - 1}");

        var vulkanSource = RequireOwned<VulkanTexture2D>(source, nameof(source));
        var vulkanDest = RequireOwned<VulkanTextureArray>(destination, nameof(destination));

        var aspectMask = GetCopyAspectMask(source.Format);

        vulkanSource.TransitionTo(ImageLayout.TransferSrcOptimal);
        vulkanDest.TransitionLayerToTransferDestination((uint)layerIndex);

        RecordCopyToArrayLayer(
            vulkanSource.ImageHandle,
            vulkanDest.ImageHandle,
            aspectMask,
            (uint)layerIndex,
            (uint)source.Width,
            (uint)source.Height);

        vulkanDest.TransitionLayerToSampled((uint)layerIndex);

        vulkanSource.TransitionTo(ImageLayout.ShaderReadOnlyOptimal);
    }

    public unsafe void CopyTextureToCubeArrayFace(ITexture2D source, ITextureCubeArray destination, int arrayIndex, int faceIndex)
    {
        if (arrayIndex < 0 || arrayIndex >= (int)destination.ArraySize)
            throw new ArgumentOutOfRangeException(nameof(arrayIndex), $"Array index must be 0-{destination.ArraySize - 1}");
        VulkanTextureCube.ThrowIfFaceOutOfRange(faceIndex);

        var vulkanSource = RequireOwned<VulkanTexture2D>(source, nameof(source));
        var vulkanDest = RequireOwned<VulkanTextureCubeArray>(destination, nameof(destination));

        var aspectMask = GetCopyAspectMask(source.Format);

        uint layerIndex = (uint)(arrayIndex * 6 + faceIndex);

        vulkanSource.TransitionTo(ImageLayout.TransferSrcOptimal);
        vulkanDest.TransitionFaceToTransferDestination((uint)arrayIndex, faceIndex);

        RecordCopyToArrayLayer(
            vulkanSource.ImageHandle,
            vulkanDest.ImageHandle,
            aspectMask,
            layerIndex,
            (uint)source.Width,
            (uint)source.Height);

        vulkanDest.TransitionFaceToSampled((uint)arrayIndex, faceIndex);

        vulkanSource.TransitionTo(ImageLayout.ShaderReadOnlyOptimal);
    }

    /// <summary>
    /// The aspect a copy from a texture of <paramref name="format"/> transfers. A depth format copies its depth
    /// aspect alone, even where <see cref="TextureFormatExtensions.GetAspectMask"/> adds stencil.
    /// </summary>
    private static ImageAspectFlags GetCopyAspectMask(TextureFormat format)
    {
        return format.IsDepthFormat() ? ImageAspectFlags.DepthBit : ImageAspectFlags.ColorBit;
    }

    /// <summary>
    /// Copies a single-layer 2D image into one array layer of the destination. The caller owns the
    /// transitions: when the recorded batch runs the source must be in
    /// <see cref="ImageLayout.TransferSrcOptimal"/> and the destination layer in
    /// <see cref="ImageLayout.TransferDstOptimal"/>.
    /// </summary>
    private unsafe void RecordCopyToArrayLayer(
        Image sourceImage,
        Image destinationImage,
        ImageAspectFlags aspectMask,
        uint destinationArrayLayer,
        uint width,
        uint height)
    {
        RecordCommands(cmd =>
        {
            var copyRegion = new ImageCopy
            {
                SrcSubresource = new ImageSubresourceLayers
                {
                    AspectMask = aspectMask,
                    MipLevel = 0,
                    BaseArrayLayer = 0,
                    LayerCount = 1
                },
                SrcOffset = new Offset3D(0, 0, 0),
                DstSubresource = new ImageSubresourceLayers
                {
                    AspectMask = aspectMask,
                    MipLevel = 0,
                    BaseArrayLayer = destinationArrayLayer,
                    LayerCount = 1
                },
                DstOffset = new Offset3D(0, 0, 0),
                Extent = new Extent3D(width, height, 1)
            };

            Vk.CmdCopyImage(
                cmd,
                sourceImage,
                ImageLayout.TransferSrcOptimal,
                destinationImage,
                ImageLayout.TransferDstOptimal,
                1,
                &copyRegion);
        });
    }
}
