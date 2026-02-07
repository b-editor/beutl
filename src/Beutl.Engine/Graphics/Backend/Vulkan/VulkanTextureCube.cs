using System;
using Silk.NET.Vulkan;

namespace Beutl.Graphics.Backend.Vulkan;

/// <summary>
/// Vulkan implementation of <see cref="ITextureCube"/>.
/// Used for point light shadow maps.
/// </summary>
internal sealed unsafe class VulkanTextureCube : ITextureCube
{
    private readonly VulkanContext _context;
    private readonly Silk.NET.Vulkan.Image _image;
    private readonly DeviceMemory _memory;
    private readonly ImageView _imageView;           // Cube map view for sampling
    private readonly ImageView[] _faceViews;         // Individual face views for framebuffer attachment
    private readonly int _size;
    private readonly TextureFormat _format;
    private ImageLayout _currentLayout = ImageLayout.Undefined;
    private bool _disposed;

    public VulkanTextureCube(
        VulkanContext context,
        int size,
        TextureFormat format,
        ImageUsageFlags usage = ImageUsageFlags.SampledBit | ImageUsageFlags.DepthStencilAttachmentBit)
    {
        _context = context;
        _size = size;
        _format = format;
        _faceViews = new ImageView[6];

        var vk = context.Vk;
        var device = context.Device;

        // Create cube map image
        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            Flags = ImageCreateFlags.CreateCubeCompatibleBit,  // Enable cube map compatibility
            ImageType = ImageType.Type2D,
            Format = format.ToVulkanFormat(),
            Extent = new Extent3D((uint)size, (uint)size, 1),
            MipLevels = 1,
            ArrayLayers = 6,  // 6 faces for cube map
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
            throw new InvalidOperationException($"Failed to create Vulkan cube map image: {result}");
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
            throw new InvalidOperationException($"Failed to allocate Vulkan cube map image memory: {result}");
        }
        _memory = memory;

        // Bind memory to image
        result = vk.BindImageMemory(device, _image, _memory, 0);
        if (result != Result.Success)
        {
            vk.FreeMemory(device, _memory, null);
            vk.DestroyImage(device, _image, null);
            throw new InvalidOperationException($"Failed to bind cube map image memory: {result}");
        }

        // Create cube map image view (for sampling all 6 faces at once)
        var cubeViewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = _image,
            ViewType = ImageViewType.TypeCube,
            Format = format.ToVulkanFormat(),
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = format.GetAspectMask(),
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 6
            }
        };

        ImageView cubeView;
        result = vk.CreateImageView(device, &cubeViewInfo, null, &cubeView);
        if (result != Result.Success)
        {
            vk.FreeMemory(device, _memory, null);
            vk.DestroyImage(device, _image, null);
            throw new InvalidOperationException($"Failed to create Vulkan cube map image view: {result}");
        }
        _imageView = cubeView;

        // Create individual face views (for framebuffer attachment)
        for (int i = 0; i < 6; i++)
        {
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
                    BaseArrayLayer = (uint)i,  // Face index
                    LayerCount = 1
                }
            };

            ImageView faceView;
            result = vk.CreateImageView(device, &faceViewInfo, null, &faceView);
            if (result != Result.Success)
            {
                // Clean up previously created views
                for (int j = 0; j < i; j++)
                {
                    vk.DestroyImageView(device, _faceViews[j], null);
                }
                vk.DestroyImageView(device, _imageView, null);
                vk.FreeMemory(device, _memory, null);
                vk.DestroyImage(device, _image, null);
                throw new InvalidOperationException($"Failed to create Vulkan cube face image view {i}: {result}");
            }
            _faceViews[i] = faceView;
        }
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

        if (_currentLayout == targetLayout)
            return;

        TransitionAllFaces(targetLayout);
    }

    public void TransitionToSampled()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_currentLayout == ImageLayout.ShaderReadOnlyOptimal)
            return;

        TransitionAllFaces(ImageLayout.ShaderReadOnlyOptimal);
    }

    private void TransitionAllFaces(ImageLayout newLayout)
    {
        // Transition all 6 faces at once
        _context.TransitionImageLayout(
            _image,
            _currentLayout,
            newLayout,
            _format.GetAspectMask(),
            baseArrayLayer: 0,
            layerCount: 6);
        _currentLayout = newLayout;
    }

    public void UploadFace(int faceIndex, ReadOnlySpan<byte> data)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (faceIndex < 0 || faceIndex >= 6)
            throw new ArgumentOutOfRangeException(nameof(faceIndex), "Face index must be 0-5");

        // Create staging buffer
        using var stagingBuffer = new VulkanBuffer(
            _context,
            (ulong)data.Length,
            BufferUsage.TransferSource,
            MemoryProperty.HostVisible | MemoryProperty.HostCoherent);

        // Copy data to staging buffer
        stagingBuffer.Upload(data);

        // Transition face to transfer destination
        _context.TransitionImageLayout(
            _image,
            _currentLayout,
            ImageLayout.TransferDstOptimal,
            _format.GetAspectMask(),
            baseArrayLayer: (uint)faceIndex,
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
                    BaseArrayLayer = (uint)faceIndex,
                    LayerCount = 1
                },
                ImageOffset = new Offset3D(0, 0, 0),
                ImageExtent = new Extent3D((uint)_size, (uint)_size, 1)
            };

            // ReSharper disable once AccessToDisposedClosure
            _context.Vk.CmdCopyBufferToImage(cmd, stagingBuffer.Handle, _image, ImageLayout.TransferDstOptimal, 1, &region);
        });

        // Transition back to shader read
        _context.TransitionImageLayout(
            _image,
            ImageLayout.TransferDstOptimal,
            ImageLayout.ShaderReadOnlyOptimal,
            _format.GetAspectMask(),
            baseArrayLayer: (uint)faceIndex,
            layerCount: 1);
    }

    public IntPtr GetFaceView(int faceIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (faceIndex < 0 || faceIndex >= 6)
            throw new ArgumentOutOfRangeException(nameof(faceIndex), "Face index must be 0-5");

        return (IntPtr)_faceViews[faceIndex].Handle;
    }

    public ImageView GetFaceViewHandle(int faceIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (faceIndex < 0 || faceIndex >= 6)
            throw new ArgumentOutOfRangeException(nameof(faceIndex), "Face index must be 0-5");

        return _faceViews[faceIndex];
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        var vk = _context.Vk;
        var device = _context.Device;

        // Destroy face views
        for (int i = 0; i < 6; i++)
        {
            if (_faceViews[i].Handle != 0)
            {
                vk.DestroyImageView(device, _faceViews[i], null);
            }
        }

        // Destroy cube map view
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
