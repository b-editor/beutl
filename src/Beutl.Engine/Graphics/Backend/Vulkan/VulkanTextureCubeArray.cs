using System;
using Silk.NET.Vulkan;

namespace Beutl.Graphics.Backend.Vulkan;

/// <summary>
/// Vulkan implementation of <see cref="ITextureCubeArray"/>.
/// Used for multiple point light shadow maps.
/// </summary>
internal sealed unsafe class VulkanTextureCubeArray : ITextureCubeArray
{
    private readonly VulkanContext _context;
    private readonly Silk.NET.Vulkan.Image _image;
    private readonly DeviceMemory _memory;
    private readonly ImageView _imageView;           // Cube array view for sampling
    private readonly ImageView[,] _faceViews;        // Individual face views [arrayIndex, faceIndex] for framebuffer attachment
    private readonly int _size;
    private readonly uint _arraySize;
    private readonly TextureFormat _format;
    private ImageLayout _currentLayout = ImageLayout.Undefined;
    private bool _disposed;

    public VulkanTextureCubeArray(
        VulkanContext context,
        int size,
        uint arraySize,
        TextureFormat format,
        ImageUsageFlags usage = ImageUsageFlags.SampledBit | ImageUsageFlags.DepthStencilAttachmentBit)
    {
        if (arraySize == 0)
            throw new ArgumentException("Array size must be greater than 0", nameof(arraySize));

        _context = context;
        _size = size;
        _arraySize = arraySize;
        _format = format;
        _faceViews = new ImageView[arraySize, 6];

        var vk = context.Vk;
        var device = context.Device;

        // Total layers = arraySize * 6 faces
        uint totalLayers = arraySize * 6;

        // Create cube map array image
        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            Flags = ImageCreateFlags.CreateCubeCompatibleBit,  // Enable cube map compatibility
            ImageType = ImageType.Type2D,
            Format = format.ToVulkanFormat(),
            Extent = new Extent3D((uint)size, (uint)size, 1),
            MipLevels = 1,
            ArrayLayers = totalLayers,  // 6 faces per cube * arraySize
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = usage,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined
        };

        Silk.NET.Vulkan.Image image;
        var result = vk.CreateImage(device, &imageInfo, null, &image);
        if (result != Result.Success)
        {
            throw new InvalidOperationException($"Failed to create Vulkan cube map array image: {result}");
        }
        _image = image;

        // Get memory requirements
        MemoryRequirements memReqs;
        vk.GetImageMemoryRequirements(device, _image, &memReqs);

        // Allocate memory
        var allocInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memReqs.Size,
            MemoryTypeIndex = context.FindMemoryType(memReqs.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit)
        };

        DeviceMemory memory;
        result = vk.AllocateMemory(device, &allocInfo, null, &memory);
        if (result != Result.Success)
        {
            vk.DestroyImage(device, _image, null);
            throw new InvalidOperationException($"Failed to allocate Vulkan cube map array image memory: {result}");
        }
        _memory = memory;

        // Bind memory to image
        result = vk.BindImageMemory(device, _image, _memory, 0);
        if (result != Result.Success)
        {
            vk.FreeMemory(device, _memory, null);
            vk.DestroyImage(device, _image, null);
            throw new InvalidOperationException($"Failed to bind cube map array image memory: {result}");
        }

        // Create cube map array image view (for sampling all cubes at once as samplerCubeArray)
        var cubeArrayViewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = _image,
            ViewType = ImageViewType.TypeCubeArray,
            Format = format.ToVulkanFormat(),
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = format.GetAspectMask(),
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = totalLayers
            }
        };

        ImageView cubeArrayView;
        result = vk.CreateImageView(device, &cubeArrayViewInfo, null, &cubeArrayView);
        if (result != Result.Success)
        {
            vk.FreeMemory(device, _memory, null);
            vk.DestroyImage(device, _image, null);
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

                var faceViewInfo = new ImageViewCreateInfo
                {
                    SType = StructureType.ImageViewCreateInfo,
                    Image = _image,
                    ViewType = ImageViewType.Type2D,  // Individual face as 2D view
                    Format = format.ToVulkanFormat(),
                    SubresourceRange = new ImageSubresourceRange
                    {
                        AspectMask = format.GetAspectMask(),
                        BaseMipLevel = 0,
                        LevelCount = 1,
                        BaseArrayLayer = layerIndex,
                        LayerCount = 1
                    }
                };

                ImageView faceView;
                result = vk.CreateImageView(device, &faceViewInfo, null, &faceView);
                if (result != Result.Success)
                {
                    // Clean up previously created views
                    CleanupFaceViews(arrIdx, faceIdx, vk, device);
                    vk.DestroyImageView(device, _imageView, null);
                    vk.FreeMemory(device, _memory, null);
                    vk.DestroyImage(device, _image, null);
                    throw new InvalidOperationException($"Failed to create Vulkan cube array face image view [{arrIdx},{faceIdx}]: {result}");
                }
                _faceViews[arrIdx, faceIdx] = faceView;
            }
        }
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

        if (_currentLayout == ImageLayout.ShaderReadOnlyOptimal)
            return;

        // Transition all layers at once
        _context.TransitionImageLayout(
            _image,
            _currentLayout,
            ImageLayout.ShaderReadOnlyOptimal,
            _format.GetAspectMask(),
            baseArrayLayer: 0,
            layerCount: _arraySize * 6);
        _currentLayout = ImageLayout.ShaderReadOnlyOptimal;
    }

    /// <summary>
    /// Transitions a specific cube map in the array to attachment layout for rendering.
    /// </summary>
    /// <param name="arrayIndex">The array index of the cube map.</param>
    public void TransitionCubeToAttachment(uint arrayIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (arrayIndex >= _arraySize)
            throw new ArgumentOutOfRangeException(nameof(arrayIndex));

        var targetLayout = _format.IsDepthFormat()
            ? ImageLayout.DepthStencilAttachmentOptimal
            : ImageLayout.ColorAttachmentOptimal;

        // Transition all 6 faces of this cube map
        uint baseLayer = arrayIndex * 6;
        _context.TransitionImageLayout(
            _image,
            _currentLayout,
            targetLayout,
            _format.GetAspectMask(),
            baseArrayLayer: baseLayer,
            layerCount: 6);
    }

    /// <summary>
    /// Transitions a specific face of a cube map in the array.
    /// </summary>
    public void TransitionFaceToAttachment(uint arrayIndex, int faceIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (arrayIndex >= _arraySize)
            throw new ArgumentOutOfRangeException(nameof(arrayIndex));
        if (faceIndex < 0 || faceIndex >= 6)
            throw new ArgumentOutOfRangeException(nameof(faceIndex));

        var targetLayout = _format.IsDepthFormat()
            ? ImageLayout.DepthStencilAttachmentOptimal
            : ImageLayout.ColorAttachmentOptimal;

        uint layerIndex = arrayIndex * 6 + (uint)faceIndex;
        _context.TransitionImageLayout(
            _image,
            ImageLayout.Undefined,  // We don't track per-face layout
            targetLayout,
            _format.GetAspectMask(),
            baseArrayLayer: layerIndex,
            layerCount: 1);
    }

    public IntPtr GetFaceView(uint arrayIndex, int faceIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (arrayIndex >= _arraySize)
            throw new ArgumentOutOfRangeException(nameof(arrayIndex));
        if (faceIndex < 0 || faceIndex >= 6)
            throw new ArgumentOutOfRangeException(nameof(faceIndex), "Face index must be 0-5");

        return (IntPtr)_faceViews[arrayIndex, faceIndex].Handle;
    }

    public ImageView GetFaceViewHandle(uint arrayIndex, int faceIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (arrayIndex >= _arraySize)
            throw new ArgumentOutOfRangeException(nameof(arrayIndex));
        if (faceIndex < 0 || faceIndex >= 6)
            throw new ArgumentOutOfRangeException(nameof(faceIndex), "Face index must be 0-5");

        return _faceViews[arrayIndex, faceIndex];
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        var vk = _context.Vk;
        var device = _context.Device;

        // Destroy face views
        for (uint arrIdx = 0; arrIdx < _arraySize; arrIdx++)
        {
            for (int faceIdx = 0; faceIdx < 6; faceIdx++)
            {
                if (_faceViews[arrIdx, faceIdx].Handle != 0)
                {
                    vk.DestroyImageView(device, _faceViews[arrIdx, faceIdx], null);
                }
            }
        }

        // Destroy cube array view
        if (_imageView.Handle != 0)
        {
            vk.DestroyImageView(device, _imageView, null);
        }

        if (_image.Handle != 0)
        {
            vk.DestroyImage(device, _image, null);
        }

        if (_memory.Handle != 0)
        {
            vk.FreeMemory(device, _memory, null);
        }
    }
}
