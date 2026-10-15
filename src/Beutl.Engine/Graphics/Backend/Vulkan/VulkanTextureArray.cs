using System;
using Silk.NET.Vulkan;

namespace Beutl.Graphics.Backend.Vulkan;

internal sealed class VulkanTextureArray : VulkanLayeredTexture, ITextureArray
{
    public VulkanTextureArray(
        VulkanContext context,
        int width,
        int height,
        uint arraySize,
        TextureFormat format,
        ImageUsageFlags usage)
        : base(
            context,
            width,
            height,
            ThrowIfEmpty(arraySize),
            format,
            usage,
            flags: 0,
            ImageViewType.Type2DArray,
            "texture array image")
    {
    }

    public int Width => LayerWidth;

    public int Height => LayerHeight;

    public uint ArraySize => LayerCount;

    public void TransitionLayerToSampled(uint layerIndex)
    {
        ThrowIfDisposed();

        ValidateLayer(layerIndex);

        TransitionLayer(layerIndex, ImageLayout.ShaderReadOnlyOptimal);
    }

    internal void TransitionLayerToTransferDestination(uint layerIndex)
    {
        ThrowIfDisposed();

        TransitionLayer(layerIndex, ImageLayout.TransferDstOptimal);
    }
}
