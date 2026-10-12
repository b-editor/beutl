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
            "cube map image",
            static face => $"cube face image view {face}")
    {
    }

    public int Size => LayerWidth;

    public void TransitionToAttachment()
    {
        ThrowIfDisposed();

        var targetLayout = Format.IsDepthFormat()
            ? ImageLayout.DepthStencilAttachmentOptimal
            : ImageLayout.ColorAttachmentOptimal;

        TransitionAllLayers(targetLayout);
    }

    public void TransitionToSampled()
    {
        ThrowIfDisposed();

        TransitionAllLayers(ImageLayout.ShaderReadOnlyOptimal);
    }

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

    public void UploadFace(int faceIndex, ReadOnlySpan<byte> data)
    {
        ThrowIfDisposed();

        ThrowIfFaceOutOfRange(faceIndex);

        UploadToLayer((uint)faceIndex, data);
    }

    public IntPtr GetFaceView(int faceIndex)
    {
        ThrowIfDisposed();

        ThrowIfFaceOutOfRange(faceIndex);

        return GetLayerViewHandle((uint)faceIndex);
    }

    /// <summary>Throws unless <paramref name="faceIndex"/> names one of a cube's six faces.</summary>
    internal static void ThrowIfFaceOutOfRange(int faceIndex)
    {
        if (faceIndex < 0 || faceIndex >= 6)
            throw new ArgumentOutOfRangeException(nameof(faceIndex), "Face index must be 0-5");
    }
}
