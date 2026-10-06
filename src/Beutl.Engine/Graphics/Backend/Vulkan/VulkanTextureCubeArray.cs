using System;
using Silk.NET.Vulkan;

namespace Beutl.Graphics.Backend.Vulkan;

/// <summary>
/// Vulkan implementation of <see cref="ITextureCubeArray"/>.
/// Used for multiple point light shadow maps.
/// </summary>
internal sealed unsafe class VulkanTextureCubeArray : ITextureCubeArray, IVulkanContextResource
{
    private readonly VulkanContext _context;
    private readonly Silk.NET.Vulkan.Image _image;
    private readonly DeviceMemory _memory;

    public VulkanContext OwnerContext => _context;
    private readonly ImageView _imageView;           // Cube array view for sampling
    private readonly ImageView[,] _faceViews;        // Individual face views [arrayIndex, faceIndex] for framebuffer attachment
    private readonly ImageLayout[,] _faceLayouts;
    private readonly int _size;
    private readonly uint _arraySize;
    private readonly TextureFormat _format;
    private bool _disposed;

    public VulkanTextureCubeArray(
        VulkanContext context,
        int size,
        uint arraySize,
        TextureFormat format,
        ImageUsageFlags usage)
    {
        if (arraySize == 0)
            throw new ArgumentException("Array size must be greater than 0", nameof(arraySize));
        if (!context.SupportsImageCubeArray)
        {
            // Named here rather than left to vkCreateImageView, whose failure mentions neither this texture
            // nor the feature that is missing.
            throw new NotSupportedException(
                "This Vulkan device does not support cube-array image views (imageCubeArray), which a cube "
                + "texture array needs both for its view and for the SampledCubeArray capability its shaders "
                + "declare.");
        }

        _context = context;
        _size = size;
        _arraySize = arraySize;
        _format = format;
        _faceViews = new ImageView[arraySize, 6];
        _faceLayouts = new ImageLayout[arraySize, 6];

        var vk = context.Vk;
        var device = context.Device;

        // Total layers = arraySize * 6 faces
        uint totalLayers = arraySize * 6;

        // Create cube map array image (cube-compatible)
        _image = context.CreateTextureImage(
            format,
            size,
            size,
            totalLayers,
            usage,
            ImageCreateFlags.CreateCubeCompatibleBit,
            pNext: null,
            "cube map array image");

        _memory = context.AllocateAndBindImageMemory(_image, "cube map array image", out _);

        // Create cube map array image view (for sampling all cubes at once as samplerCubeArray)
        Result result = context.TryCreateImageView(
            _image,
            format,
            ImageViewType.TypeCubeArray,
            baseArrayLayer: 0,
            layerCount: totalLayers,
            out ImageView cubeArrayView);
        if (result != Result.Success)
        {
            vk.DestroyImage(device, _image, null);
            vk.FreeMemory(device, _memory, null);
            throw new InvalidOperationException($"Failed to create Vulkan cube map array image view: {result}");
        }
        _imageView = cubeArrayView;

        // Create individual face views (for framebuffer attachment)
        for (uint arrIdx = 0; arrIdx < arraySize; arrIdx++)
        {
            for (int faceIdx = 0; faceIdx < 6; faceIdx++)
            {
                // Layer index = arrIdx * 6 + faceIdx
                uint layerIndex = arrIdx * 6 + (uint)faceIdx;

                result = context.TryCreateSingleLayerView(_image, format, layerIndex, out ImageView faceView);
                if (result != Result.Success)
                {
                    // Clean up previously created views
                    ReleaseConstructedHandles(vk, device, arrIdx, faceIdx);
                    throw new InvalidOperationException($"Failed to create Vulkan cube array face image view [{arrIdx},{faceIdx}]: {result}");
                }
                _faceViews[arrIdx, faceIdx] = faceView;
            }
        }

        // Every allocated face is covered by the cube-array view the shader samples, whether or not any
        // light ever renders into it, so a face has to start in a layout the sampler can read rather than
        // in UNDEFINED, which is what the descriptor would otherwise present to the lighting pass.
        try
        {
            _context.TransitionImageLayout(
                _image,
                ImageLayout.Undefined,
                ImageLayout.ShaderReadOnlyOptimal,
                _format.GetAspectMask(),
                baseArrayLayer: 0,
                layerCount: totalLayers);
        }
        catch
        {
            ReleaseConstructedHandles(vk, device, arraySize - 1, 6);
            throw;
        }

        for (uint arrIdx = 0; arrIdx < arraySize; arrIdx++)
        {
            for (int faceIdx = 0; faceIdx < 6; faceIdx++)
                _faceLayouts[arrIdx, faceIdx] = ImageLayout.ShaderReadOnlyOptimal;
        }
    }

    // Releases what the constructor created before a later step failed: the face views up to the given position,
    // then the cube-array view, the image and, last, its memory.
    private void ReleaseConstructedHandles(Vk vk, Device device, uint currentArrayIdx, int currentFaceIdx)
    {
        CleanupFaceViews(currentArrayIdx, currentFaceIdx, vk, device);
        vk.DestroyImageView(device, _imageView, null);
        vk.DestroyImage(device, _image, null);
        vk.FreeMemory(device, _memory, null);
    }

    private void CleanupFaceViews(uint currentArrayIdx, int currentFaceIdx, Vk vk, Device device)
    {
        for (uint arrIdx = 0; arrIdx <= currentArrayIdx; arrIdx++)
        {
            int maxFace = arrIdx < currentArrayIdx ? 6 : currentFaceIdx;
            for (int faceIdx = 0; faceIdx < maxFace; faceIdx++)
            {
                if (_faceViews[arrIdx, faceIdx].Handle != 0)
                {
                    vk.DestroyImageView(device, _faceViews[arrIdx, faceIdx], null);
                }
            }
        }
    }

    public int Size => _size;

    public uint ArraySize => _arraySize;

    public TextureFormat Format => _format;

    public IntPtr NativeHandle => (IntPtr)_image.Handle;

    public Silk.NET.Vulkan.Image ImageHandle => _image;

    public ImageView ImageViewHandle => _imageView;

    public void TransitionToSampled()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        for (uint arrayIndex = 0; arrayIndex < _arraySize; arrayIndex++)
        {
            for (int faceIndex = 0; faceIndex < 6; faceIndex++)
            {
                TransitionFace(arrayIndex, faceIndex, ImageLayout.ShaderReadOnlyOptimal);
            }
        }
    }

    internal void TransitionFaceToTransferDestination(uint arrayIndex, int faceIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateFace(arrayIndex, faceIndex);
        TransitionFace(arrayIndex, faceIndex, ImageLayout.TransferDstOptimal);
    }

    internal void TransitionFaceToSampled(uint arrayIndex, int faceIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateFace(arrayIndex, faceIndex);
        TransitionFace(arrayIndex, faceIndex, ImageLayout.ShaderReadOnlyOptimal);
    }

    /// <inheritdoc cref="VulkanTextureArray.GetLayerLayout"/>
    internal ImageLayout GetFaceLayout(uint arrayIndex, int faceIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateFace(arrayIndex, faceIndex);
        return _faceLayouts[arrayIndex, faceIndex];
    }

    private void TransitionFace(uint arrayIndex, int faceIndex, ImageLayout newLayout)
    {
        ImageLayout oldLayout = _faceLayouts[arrayIndex, faceIndex];
        if (oldLayout == newLayout)
            return;

        uint layerIndex = arrayIndex * 6 + (uint)faceIndex;
        _context.TransitionImageLayout(
            _image,
            oldLayout,
            newLayout,
            _format.GetAspectMask(),
            baseArrayLayer: layerIndex,
            layerCount: 1);
        _faceLayouts[arrayIndex, faceIndex] = newLayout;
    }

    private void ValidateFace(uint arrayIndex, int faceIndex)
    {
        if (arrayIndex >= _arraySize)
            throw new ArgumentOutOfRangeException(nameof(arrayIndex));
        if (faceIndex < 0 || faceIndex >= 6)
            throw new ArgumentOutOfRangeException(nameof(faceIndex));
    }

    public IntPtr GetFaceView(uint arrayIndex, int faceIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (arrayIndex >= _arraySize)
            throw new ArgumentOutOfRangeException(nameof(arrayIndex));
        VulkanTextureCube.ThrowIfFaceOutOfRange(faceIndex);

        return (IntPtr)_faceViews[arrayIndex, faceIndex].Handle;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        ImageView[,] faceViews = _faceViews;
        ImageView imageView = _imageView;
        Silk.NET.Vulkan.Image image = _image;
        DeviceMemory memory = _memory;
        _context.DeferRelease(() =>
        {
            var vk = _context.Vk;
            var device = _context.Device;

            foreach (ImageView faceView in faceViews)
            {
                if (faceView.Handle != 0)
                {
                    vk.DestroyImageView(device, faceView, null);
                }
            }

            if (imageView.Handle != 0)
            {
                vk.DestroyImageView(device, imageView, null);
            }

            if (image.Handle != 0)
            {
                vk.DestroyImage(device, image, null);
            }

            if (memory.Handle != 0)
            {
                vk.FreeMemory(device, memory, null);
            }
        });
    }
}
