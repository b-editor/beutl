using System;
using Silk.NET.Vulkan;

namespace Beutl.Graphics.Backend.Vulkan;

/// <summary>
/// Vulkan implementation of <see cref="ITextureCube"/>.
/// Used for point light shadow maps.
/// </summary>
internal sealed unsafe class VulkanTextureCube : ITextureCube, IVulkanContextResource
{
    private readonly VulkanContext _context;
    private readonly Silk.NET.Vulkan.Image _image;
    private readonly DeviceMemory _memory;

    public VulkanContext OwnerContext => _context;
    private readonly ImageView _imageView;           // Cube map view for sampling
    private readonly ImageView[] _faceViews;         // Individual face views for framebuffer attachment
    private readonly ImageLayout[] _faceLayouts = new ImageLayout[6];
    private readonly int _size;
    private readonly TextureFormat _format;
    private bool _disposed;

    public VulkanTextureCube(
        VulkanContext context,
        int size,
        TextureFormat format,
        ImageUsageFlags usage)
    {
        _context = context;
        _size = size;
        _format = format;
        _faceViews = new ImageView[6];

        var vk = context.Vk;
        var device = context.Device;

        // Create cube map image (six faces, cube-compatible)
        _image = context.CreateTextureImage(
            format,
            size,
            size,
            arrayLayers: 6,
            usage,
            ImageCreateFlags.CreateCubeCompatibleBit,
            pNext: null,
            "cube map image");

        _memory = context.AllocateAndBindImageMemory(_image, "cube map image", out _);

        // Create cube map image view (for sampling all 6 faces at once)
        Result result = context.TryCreateImageView(
            _image, format, ImageViewType.TypeCube, baseArrayLayer: 0, layerCount: 6, out ImageView cubeView);
        if (result != Result.Success)
        {
            vk.DestroyImage(device, _image, null);
            vk.FreeMemory(device, _memory, null);
            throw new InvalidOperationException($"Failed to create Vulkan cube map image view: {result}");
        }
        _imageView = cubeView;

        // Create individual face views (for framebuffer attachment)
        for (int i = 0; i < 6; i++)
        {
            result = context.TryCreateSingleLayerView(_image, format, (uint)i, out ImageView faceView);
            if (result != Result.Success)
            {
                // Clean up previously created views
                ReleaseConstructedHandles(vk, device, createdFaceViews: i);
                throw new InvalidOperationException($"Failed to create Vulkan cube face image view {i}: {result}");
            }
            _faceViews[i] = faceView;
        }

        // The cube view samples all six faces whether or not every one is rendered into, so a face has to
        // start in a layout the sampler can read rather than in UNDEFINED.
        try
        {
            _context.TransitionImageLayout(
                _image,
                ImageLayout.Undefined,
                ImageLayout.ShaderReadOnlyOptimal,
                _format.GetAspectMask(),
                baseArrayLayer: 0,
                layerCount: 6);
        }
        catch
        {
            ReleaseConstructedHandles(vk, device, createdFaceViews: _faceViews.Length);
            throw;
        }

        for (int i = 0; i < _faceLayouts.Length; i++)
            _faceLayouts[i] = ImageLayout.ShaderReadOnlyOptimal;
    }

    // Releases what the constructor created before a later step failed: the first createdFaceViews face views,
    // then the cube view, the image and, last, its memory.
    private void ReleaseConstructedHandles(Vk vk, Device device, int createdFaceViews)
    {
        for (int j = 0; j < createdFaceViews; j++)
            vk.DestroyImageView(device, _faceViews[j], null);
        vk.DestroyImageView(device, _imageView, null);
        vk.DestroyImage(device, _image, null);
        vk.FreeMemory(device, _memory, null);
    }

    public int Size => _size;

    public TextureFormat Format => _format;

    public IntPtr NativeHandle => (IntPtr)_image.Handle;

    public Silk.NET.Vulkan.Image ImageHandle => _image;

    public ImageView ImageViewHandle => _imageView;

    public void TransitionToAttachment()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var targetLayout = _format.IsDepthFormat()
            ? ImageLayout.DepthStencilAttachmentOptimal
            : ImageLayout.ColorAttachmentOptimal;

        TransitionAllFaces(targetLayout);
    }

    public void TransitionToSampled()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        TransitionAllFaces(ImageLayout.ShaderReadOnlyOptimal);
    }

    private void TransitionAllFaces(ImageLayout newLayout)
    {
        for (int i = 0; i < _faceLayouts.Length; i++)
        {
            TransitionFace(i, newLayout);
        }
    }

    internal void TransitionFaceToTransferDestination(int faceIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (faceIndex < 0 || faceIndex >= 6)
            throw new ArgumentOutOfRangeException(nameof(faceIndex));

        TransitionFace(faceIndex, ImageLayout.TransferDstOptimal);
    }

    internal void TransitionFaceToSampled(int faceIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (faceIndex < 0 || faceIndex >= 6)
            throw new ArgumentOutOfRangeException(nameof(faceIndex));

        TransitionFace(faceIndex, ImageLayout.ShaderReadOnlyOptimal);
    }

    private void TransitionFace(int faceIndex, ImageLayout newLayout)
    {
        ImageLayout oldLayout = _faceLayouts[faceIndex];
        if (oldLayout == newLayout)
            return;

        _context.TransitionImageLayout(
            _image,
            oldLayout,
            newLayout,
            _format.GetAspectMask(),
            baseArrayLayer: (uint)faceIndex,
            layerCount: 1);
        _faceLayouts[faceIndex] = newLayout;
    }

    public void UploadFace(int faceIndex, ReadOnlySpan<byte> data)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        ThrowIfFaceOutOfRange(faceIndex);

        // Transition face to transfer destination
        TransitionFace(faceIndex, ImageLayout.TransferDstOptimal);

        _context.UploadToImageLayer(
            data,
            _image,
            _format.GetAspectMask(),
            (uint)faceIndex,
            (uint)_size,
            (uint)_size);

        // Transition back to shader read
        TransitionFace(faceIndex, ImageLayout.ShaderReadOnlyOptimal);
    }

    public IntPtr GetFaceView(int faceIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        ThrowIfFaceOutOfRange(faceIndex);

        return (IntPtr)_faceViews[faceIndex].Handle;
    }

    /// <summary>Throws unless <paramref name="faceIndex"/> names one of a cube's six faces.</summary>
    internal static void ThrowIfFaceOutOfRange(int faceIndex)
    {
        if (faceIndex < 0 || faceIndex >= 6)
            throw new ArgumentOutOfRangeException(nameof(faceIndex), "Face index must be 0-5");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        ImageView[] faceViews = _faceViews;
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
