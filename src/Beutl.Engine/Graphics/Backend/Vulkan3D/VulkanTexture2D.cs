using System;
using Silk.NET.Vulkan;

namespace Beutl.Graphics.Backend.Vulkan3D;

/// <summary>
/// Vulkan implementation of <see cref="ITexture2D"/>.
/// </summary>
internal sealed unsafe class VulkanTexture2D : ITexture2D
{
    private readonly VulkanContext _context;
    private readonly Silk.NET.Vulkan.Image _image;
    private readonly DeviceMemory _memory;
    private readonly ImageView _imageView;
    private readonly int _width;
    private readonly int _height;
    private readonly TextureFormat _format;
    private ImageLayout _currentLayout = ImageLayout.Undefined;
    private bool _disposed;

    public VulkanTexture2D(
        VulkanContext context,
        int width,
        int height,
        TextureFormat format,
        ImageUsageFlags usage = ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit)
    {
        _context = context;
        _width = width;
        _height = height;
        _format = format;

        var vk = context.Vk;
        var device = context.Device;

        // Create image
        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = format.ToVulkanFormat(),
            Extent = new Extent3D((uint)width, (uint)height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
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
            throw new InvalidOperationException($"Failed to create Vulkan image: {result}");
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
            throw new InvalidOperationException($"Failed to allocate Vulkan image memory: {result}");
        }
        _memory = memory;

        // Bind memory to image
        result = vk.BindImageMemory(device, _image, _memory, 0);
        if (result != Result.Success)
        {
            vk.FreeMemory(device, _memory, null);
            vk.DestroyImage(device, _image, null);
            throw new InvalidOperationException($"Failed to bind image memory: {result}");
        }

        // Create image view
        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = _image,
            ViewType = ImageViewType.Type2D,
            Format = format.ToVulkanFormat(),
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = format.GetAspectMask(),
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1
            }
        };

        ImageView imageView;
        result = vk.CreateImageView(device, &viewInfo, null, &imageView);
        if (result != Result.Success)
        {
            vk.FreeMemory(device, _memory, null);
            vk.DestroyImage(device, _image, null);
            throw new InvalidOperationException($"Failed to create Vulkan image view: {result}");
        }
        _imageView = imageView;
    }

    public int Width => _width;

    public int Height => _height;

    public TextureFormat Format => _format;

    public IntPtr NativeHandle => (IntPtr)_image.Handle;

    public Silk.NET.Vulkan.Image ImageHandle => _image;

    public ImageView ImageViewHandle => _imageView;

    public void Upload(ReadOnlySpan<byte> data)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Create staging buffer
        var stagingBuffer = new VulkanBuffer(
            _context,
            (ulong)data.Length,
            BufferUsageFlags.TransferSrcBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);

        try
        {
            // Copy data to staging buffer
            stagingBuffer.Upload(data);

            // Transition to transfer destination
            TransitionTo(ImageLayout.TransferDstOptimal);

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
                        AspectMask = ImageAspectFlags.ColorBit,
                        MipLevel = 0,
                        BaseArrayLayer = 0,
                        LayerCount = 1
                    },
                    ImageOffset = new Offset3D(0, 0, 0),
                    ImageExtent = new Extent3D((uint)_width, (uint)_height, 1)
                };

                _context.Vk.CmdCopyBufferToImage(cmd, stagingBuffer.Handle, _image, ImageLayout.TransferDstOptimal, 1, &region);
            });

            // Transition to shader read
            TransitionTo(ImageLayout.ShaderReadOnlyOptimal);
        }
        finally
        {
            stagingBuffer.Dispose();
        }
    }

    public void TransitionTo(ImageLayout layout)
    {
        if (_currentLayout == layout)
            return;

        _context.TransitionImageLayout(_image, _currentLayout, layout, _format.GetAspectMask());
        _currentLayout = layout;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        var vk = _context.Vk;
        var device = _context.Device;

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
