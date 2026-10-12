using System;
using Silk.NET.Vulkan;

namespace Beutl.Graphics.Backend.Vulkan;

internal sealed class VulkanTextureCubeArray : VulkanLayeredTexture, ITextureCubeArray
{
    public VulkanTextureCubeArray(
        VulkanContext context,
        int size,
        uint arraySize,
        TextureFormat format,
        ImageUsageFlags usage)
        : base(
            context,
            size,
            size,
            GetLayerCount(context, arraySize),
            format,
            usage,
            ImageCreateFlags.CreateCubeCompatibleBit,
            ImageViewType.TypeCubeArray,
            "cube map array image",
            static layer => $"cube array face image view [{layer / 6},{layer % 6}]")
    {
        ArraySize = arraySize;
    }

    private static uint GetLayerCount(VulkanContext context, uint arraySize)
    {
        ThrowIfEmpty(arraySize);
        if (!context.SupportsImageCubeArray)
        {
            // Named here rather than left to vkCreateImageView, whose failure mentions neither this texture
            // nor the feature that is missing.
            throw new NotSupportedException(
                "This Vulkan device does not support cube-array image views (imageCubeArray), which a cube "
                + "texture array needs both for its view and for the SampledCubeArray capability its shaders "
                + "declare.");
        }

        return arraySize * 6;
    }

    public int Size => LayerWidth;

    public uint ArraySize { get; }

    public void TransitionToSampled()
    {
        ThrowIfDisposed();

        TransitionAllLayers(ImageLayout.ShaderReadOnlyOptimal);
    }

    internal void TransitionFaceToTransferDestination(uint arrayIndex, int faceIndex)
    {
        ThrowIfDisposed();
        TransitionLayer(arrayIndex * 6 + (uint)faceIndex, ImageLayout.TransferDstOptimal);
    }

    internal void TransitionFaceToSampled(uint arrayIndex, int faceIndex)
    {
        ThrowIfDisposed();
        TransitionLayer(arrayIndex * 6 + (uint)faceIndex, ImageLayout.ShaderReadOnlyOptimal);
    }

    public IntPtr GetFaceView(uint arrayIndex, int faceIndex)
    {
        ThrowIfDisposed();

        if (arrayIndex >= ArraySize)
            throw new ArgumentOutOfRangeException(nameof(arrayIndex));
        VulkanTextureCube.ThrowIfFaceOutOfRange(faceIndex);

        return GetLayerViewHandle(arrayIndex * 6 + (uint)faceIndex);
    }
}
