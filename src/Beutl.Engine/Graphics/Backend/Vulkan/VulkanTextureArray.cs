using System;
using Silk.NET.Vulkan;

namespace Beutl.Graphics.Backend.Vulkan;

/// <summary>
/// Vulkan implementation of <see cref="ITextureArray"/>.
/// Used for efficiently storing multiple shadow maps.
/// </summary>
internal sealed unsafe class VulkanTextureArray : ITextureArray
{
    private readonly VulkanContext _context;
    private readonly Silk.NET.Vulkan.Image _image;
    private readonly DeviceMemory _memory;
    private readonly ImageView _imageView;           // Array view for sampling all layers
    private readonly ImageView[] _layerViews;        // Individual layer views for framebuffer attachment
    private readonly int _width;
    private readonly int _height;
    private readonly uint _arraySize;
    private readonly TextureFormat _format;
    private ImageLayout _currentLayout = ImageLayout.Undefined;
    private readonly ImageLayout[] _layerLayouts;    // Track layout per layer
    private bool _disposed;

    public VulkanTextureArray(
        VulkanContext context,
        int width,
        int height,
        uint arraySize,
        TextureFormat format,
        ImageUsageFlags usage = ImageUsageFlags.SampledBit | ImageUsageFlags.DepthStencilAttachmentBit)
    {
        if (arraySize == 0)
            throw new ArgumentException("Array size must be greater than 0", nameof(arraySize));

        _context = context;
        _width = width;
        _height = height;
        _arraySize = arraySize;
        _format = format;
        _layerViews = new ImageView[arraySize];
        _layerLayouts = new ImageLayout[arraySize];

        var vk = context.Vk;
        var device = context.Device;

        // Create texture array image
        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = format.ToVulkanFormat(),
            Extent = new Extent3D((uint)width, (uint)height, 1),
            MipLevels = 1,
            ArrayLayers = arraySize,
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
            throw new InvalidOperationException($"Failed to create Vulkan texture array image: {result}");
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
            throw new InvalidOperationException($"Failed to allocate Vulkan texture array image memory: {result}");
        }
        _memory = memory;

        // Bind memory to image
        result = vk.BindImageMemory(device, _image, _memory, 0);
        if (result != Result.Success)
        {
            vk.FreeMemory(device, _memory, null);
            vk.DestroyImage(device, _image, null);
            throw new InvalidOperationException($"Failed to bind texture array image memory: {result}");
        }

        // Create array image view (for sampling all layers at once as sampler2DArray)
        var arrayViewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = _image,
            ViewType = ImageViewType.Type2DArray,
            Format = format.ToVulkanFormat(),
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = format.GetAspectMask(),
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = arraySize
            }
        };

        ImageView arrayView;
        result = vk.CreateImageView(device, &arrayViewInfo, null, &arrayView);
        if (result != Result.Success)
        {
            vk.FreeMemory(device, _memory, null);
            vk.DestroyImage(device, _image, null);
            throw new InvalidOperationException($"Failed to create Vulkan texture array image view: {result}");
        }
        _imageView = arrayView;

        // Create individual layer views (for framebuffer attachment)
        for (uint i = 0; i < arraySize; i++)
        {
            var layerViewInfo = new ImageViewCreateInfo
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = _image,
                ViewType = ImageViewType.Type2D,  // Individual layer as 2D view
                Format = format.ToVulkanFormat(),
                SubresourceRange = new ImageSubresourceRange
                {
                    AspectMask = format.GetAspectMask(),
                    BaseMipLevel = 0,
                    LevelCount = 1,
                    BaseArrayLayer = i,
                    LayerCount = 1
                }
            };

            ImageView layerView;
            result = vk.CreateImageView(device, &layerViewInfo, null, &layerView);
            if (result != Result.Success)
            {
                // Clean up previously created views
                for (uint j = 0; j < i; j++)
                {
                    vk.DestroyImageView(device, _layerViews[j], null);
                }
                vk.DestroyImageView(device, _imageView, null);
                vk.FreeMemory(device, _memory, null);
                vk.DestroyImage(device, _image, null);
                throw new InvalidOperationException($"Failed to create Vulkan texture array layer image view {i}: {result}");
            }
            _layerViews[i] = layerView;
            _layerLayouts[i] = ImageLayout.Undefined;
        }
    }

    public int Width => _width;

    public int Height => _height;

    public uint ArraySize => _arraySize;

    public TextureFormat Format => _format;

    public IntPtr NativeHandle => (IntPtr)_image.Handle;

    public Silk.NET.Vulkan.Image ImageHandle => _image;

    public ImageView ImageViewHandle => _imageView;

    public void TransitionLayerToAttachment(uint layerIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (layerIndex >= _arraySize)
            throw new ArgumentOutOfRangeException(nameof(layerIndex));

        var targetLayout = _format.IsDepthFormat()
            ? ImageLayout.DepthStencilAttachmentOptimal
            : ImageLayout.ColorAttachmentOptimal;

        if (_layerLayouts[layerIndex] == targetLayout)
            return;

        _context.TransitionImageLayout(
            _image,
            _layerLayouts[layerIndex],
            targetLayout,
            _format.GetAspectMask(),
            baseArrayLayer: layerIndex,
            layerCount: 1);

        _layerLayouts[layerIndex] = targetLayout;
    }

    public void TransitionLayerToSampled(uint layerIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (layerIndex >= _arraySize)
            throw new ArgumentOutOfRangeException(nameof(layerIndex));

        if (_layerLayouts[layerIndex] == ImageLayout.ShaderReadOnlyOptimal)
            return;

        _context.TransitionImageLayout(
            _image,
            _layerLayouts[layerIndex],
            ImageLayout.ShaderReadOnlyOptimal,
            _format.GetAspectMask(),
            baseArrayLayer: layerIndex,
            layerCount: 1);

        _layerLayouts[layerIndex] = ImageLayout.ShaderReadOnlyOptimal;
    }

    public void TransitionAllToSampled()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Transition all layers at once
        _context.TransitionImageLayout(
            _image,
            _currentLayout,
            ImageLayout.ShaderReadOnlyOptimal,
            _format.GetAspectMask(),
            baseArrayLayer: 0,
            layerCount: _arraySize);

        _currentLayout = ImageLayout.ShaderReadOnlyOptimal;
        for (uint i = 0; i < _arraySize; i++)
        {
            _layerLayouts[i] = ImageLayout.ShaderReadOnlyOptimal;
        }
    }

    public void UploadLayer(uint layerIndex, ReadOnlySpan<byte> data)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (layerIndex >= _arraySize)
            throw new ArgumentOutOfRangeException(nameof(layerIndex));

        // Create staging buffer
        using var stagingBuffer = new VulkanBuffer(
            _context,
            (ulong)data.Length,
            BufferUsage.TransferSource,
            MemoryProperty.HostVisible | MemoryProperty.HostCoherent);

        // Copy data to staging buffer
        stagingBuffer.Upload(data);

        // Transition layer to transfer destination
        _context.TransitionImageLayout(
            _image,
            _layerLayouts[layerIndex],
            ImageLayout.TransferDstOptimal,
            _format.GetAspectMask(),
            baseArrayLayer: layerIndex,
            layerCount: 1);

        // Copy buffer to image
        _context.SubmitImmediateCommands(cmd =>
        {
            var region = new BufferImageCopy
            {
                BufferOffset = 0,
                BufferRowLength = 0,
                BufferImageHeight = 0,
                ImageSubresource = new ImageSubresourceLayers
                {
                    AspectMask = _format.GetAspectMask(),
                    MipLevel = 0,
                    BaseArrayLayer = layerIndex,
                    LayerCount = 1
                },
                ImageOffset = new Offset3D(0, 0, 0),
                ImageExtent = new Extent3D((uint)_width, (uint)_height, 1)
            };

            // ReSharper disable once AccessToDisposedClosure
            _context.Vk.CmdCopyBufferToImage(cmd, stagingBuffer.Handle, _image, ImageLayout.TransferDstOptimal, 1, &region);
        });

        // Transition to shader read
        _context.TransitionImageLayout(
            _image,
            ImageLayout.TransferDstOptimal,
            ImageLayout.ShaderReadOnlyOptimal,
            _format.GetAspectMask(),
            baseArrayLayer: layerIndex,
            layerCount: 1);

        _layerLayouts[layerIndex] = ImageLayout.ShaderReadOnlyOptimal;
    }

    public IntPtr GetLayerView(uint layerIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (layerIndex >= _arraySize)
            throw new ArgumentOutOfRangeException(nameof(layerIndex));

        return (IntPtr)_layerViews[layerIndex].Handle;
    }

    public ImageView GetLayerViewHandle(uint layerIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (layerIndex >= _arraySize)
            throw new ArgumentOutOfRangeException(nameof(layerIndex));

        return _layerViews[layerIndex];
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        var vk = _context.Vk;
        var device = _context.Device;

        // Destroy layer views
        for (uint i = 0; i < _arraySize; i++)
        {
            if (_layerViews[i].Handle != 0)
            {
                vk.DestroyImageView(device, _layerViews[i], null);
            }
        }

        // Destroy array view
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
