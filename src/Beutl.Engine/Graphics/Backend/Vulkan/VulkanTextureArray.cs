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
            "texture array image",
            static layer => $"texture array layer image view {layer}")
    {
    }

    public int Width => LayerWidth;

    public int Height => LayerHeight;

    public uint ArraySize => LayerCount;

    public void TransitionLayerToAttachment(uint layerIndex)
    {
        ThrowIfDisposed();

        ValidateLayer(layerIndex);

        var targetLayout = Format.IsDepthFormat()
            ? ImageLayout.DepthStencilAttachmentOptimal
            : ImageLayout.ColorAttachmentOptimal;

        TransitionLayer(layerIndex, targetLayout);
    }

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

    public void TransitionAllToSampled()
    {
        ThrowIfDisposed();

        TransitionAllLayers(ImageLayout.ShaderReadOnlyOptimal);
    }

    public void UploadLayer(uint layerIndex, ReadOnlySpan<byte> data)
    {
        ThrowIfDisposed();

        ValidateLayer(layerIndex);

        UploadToLayer(layerIndex, data);
    }

    public IntPtr GetLayerView(uint layerIndex)
    {
        ThrowIfDisposed();

        ValidateLayer(layerIndex);

        return GetLayerViewHandle(layerIndex);
    }
}
