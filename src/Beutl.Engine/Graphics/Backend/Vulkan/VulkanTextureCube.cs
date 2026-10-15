using System;
using Silk.NET.Vulkan;

namespace Beutl.Graphics.Backend.Vulkan;

internal sealed class VulkanTextureCube : VulkanLayeredTexture, ITextureCube
{
    public VulkanTextureCube(
        VulkanContext context,
        int size,
        TextureFormat format,
        ImageUsageFlags usage)
        : base(
            context,
            size,
            size,
            layerCount: 6,
            format,
            usage,
            ImageCreateFlags.CreateCubeCompatibleBit,
            ImageViewType.TypeCube,
            "cube map image")
    {
    }

    public int Size => LayerWidth;

    internal void TransitionFaceToTransferDestination(int faceIndex)
    {
        ThrowIfDisposed();

        TransitionLayer((uint)faceIndex, ImageLayout.TransferDstOptimal);
    }

    internal void TransitionFaceToSampled(int faceIndex)
    {
        ThrowIfDisposed();

        TransitionLayer((uint)faceIndex, ImageLayout.ShaderReadOnlyOptimal);
    }

    /// <summary>Throws unless <paramref name="faceIndex"/> names one of a cube's six faces.</summary>
    internal static void ThrowIfFaceOutOfRange(int faceIndex)
    {
        if (faceIndex < 0 || faceIndex >= 6)
            throw new ArgumentOutOfRangeException(nameof(faceIndex), "Face index must be 0-5");
    }
}
