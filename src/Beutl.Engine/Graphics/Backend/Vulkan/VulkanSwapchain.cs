using Beutl.Logging;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;

namespace Beutl.Graphics.Backend.Vulkan;

using Image = Silk.NET.Vulkan.Image;
using Semaphore = Silk.NET.Vulkan.Semaphore;

/// <summary>
/// Manages a Vulkan swapchain with HDR format negotiation.
/// </summary>
internal sealed unsafe class VulkanSwapchain : IDisposable
{
    private static readonly ILogger s_logger = Log.CreateLogger<VulkanSwapchain>();

    private readonly Vk _vk;
    private readonly PhysicalDevice _physicalDevice;
    private readonly Device _device;
    private readonly SurfaceKHR _surface;
    private readonly KhrSwapchain _khrSwapchain;
    private readonly KhrSurface _khrSurface;
    private readonly uint _queueFamilyIndex;

    private SwapchainKHR _swapchain;
    private Image[] _images = [];
    private ImageView[] _imageViews = [];
    private Format _format;
    private ColorSpaceKHR _colorSpace;
    private Extent2D _extent;
    private bool _isHdr;
    private bool _disposed;

    public VulkanSwapchain(
        Vk vk,
        Instance instance,
        PhysicalDevice physicalDevice,
        Device device,
        uint queueFamilyIndex,
        SurfaceKHR surface,
        uint width,
        uint height)
    {
        _vk = vk;
        _physicalDevice = physicalDevice;
        _device = device;
        _queueFamilyIndex = queueFamilyIndex;
        _surface = surface;

        if (!vk.TryGetInstanceExtension(instance, out _khrSurface!))
            throw new InvalidOperationException("VK_KHR_surface extension is not available");

        if (!vk.TryGetDeviceExtension(instance, device, out _khrSwapchain!))
            throw new InvalidOperationException("VK_KHR_swapchain extension is not available");

        Create(width, height, default);
    }

    public bool IsHdr => _isHdr;
    public Format Format => _format;
    public ColorSpaceKHR ColorSpace => _colorSpace;
    public Extent2D Extent => _extent;
    public int ImageCount => _images.Length;
    public Image[] Images => _images;
    public ImageView[] ImageViews => _imageViews;
    public SwapchainKHR Handle => _swapchain;

    public void Recreate(uint width, uint height)
    {
        _vk.DeviceWaitIdle(_device);

        var oldSwapchain = _swapchain;
        DestroyImageViews();

        Create(width, height, oldSwapchain);

        _khrSwapchain.DestroySwapchain(_device, oldSwapchain, null);
    }

    public Result AcquireNextImage(Semaphore imageAvailable, out uint imageIndex)
    {
        uint index = 0;
        var result = _khrSwapchain.AcquireNextImage(_device, _swapchain, ulong.MaxValue, imageAvailable, default, &index);
        imageIndex = index;
        return result;
    }

    public Result Present(Queue queue, Semaphore renderFinished, uint imageIndex)
    {
        fixed (SwapchainKHR* pSwapchain = &_swapchain)
        {
            var presentInfo = new PresentInfoKHR
            {
                SType = StructureType.PresentInfoKhr,
                WaitSemaphoreCount = 1,
                PWaitSemaphores = &renderFinished,
                SwapchainCount = 1,
                PSwapchains = pSwapchain,
                PImageIndices = &imageIndex
            };

            return _khrSwapchain.QueuePresent(queue, &presentInfo);
        }
    }

    private void Create(uint width, uint height, SwapchainKHR oldSwapchain)
    {
        // Query surface capabilities
        SurfaceCapabilitiesKHR capabilities;
        _khrSurface.GetPhysicalDeviceSurfaceCapabilities(_physicalDevice, _surface, &capabilities);

        // Select format (prefer HDR)
        SelectFormat(out _format, out _colorSpace, out _isHdr);

        // Select extent
        _extent = SelectExtent(capabilities, width, height);

        // Select image count (prefer double buffering, allow triple)
        uint imageCount = capabilities.MinImageCount + 1;
        if (capabilities.MaxImageCount > 0 && imageCount > capabilities.MaxImageCount)
            imageCount = capabilities.MaxImageCount;

        // Select present mode (prefer Mailbox for low latency, fallback to FIFO)
        var presentMode = SelectPresentMode();

        var createInfo = new SwapchainCreateInfoKHR
        {
            SType = StructureType.SwapchainCreateInfoKhr,
            Surface = _surface,
            MinImageCount = imageCount,
            ImageFormat = _format,
            ImageColorSpace = _colorSpace,
            ImageExtent = _extent,
            ImageArrayLayers = 1,
            ImageUsage = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferDstBit,
            ImageSharingMode = SharingMode.Exclusive,
            PreTransform = capabilities.CurrentTransform,
            CompositeAlpha = CompositeAlphaFlagsKHR.OpaqueBitKhr,
            PresentMode = presentMode,
            Clipped = Vk.True,
            OldSwapchain = oldSwapchain
        };

        SwapchainKHR swapchain;
        var result = _khrSwapchain.CreateSwapchain(_device, &createInfo, null, &swapchain);
        if (result != Result.Success)
            throw new InvalidOperationException($"Failed to create Vulkan swapchain: {result}");

        _swapchain = swapchain;

        // Get swapchain images
        uint count = 0;
        _khrSwapchain.GetSwapchainImages(_device, _swapchain, &count, null);
        _images = new Image[count];
        fixed (Image* pImages = _images)
        {
            _khrSwapchain.GetSwapchainImages(_device, _swapchain, &count, pImages);
        }

        // Create image views
        _imageViews = new ImageView[_images.Length];
        for (int i = 0; i < _images.Length; i++)
        {
            var viewInfo = new ImageViewCreateInfo
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = _images[i],
                ViewType = ImageViewType.Type2D,
                Format = _format,
                Components = new ComponentMapping
                {
                    R = ComponentSwizzle.Identity,
                    G = ComponentSwizzle.Identity,
                    B = ComponentSwizzle.Identity,
                    A = ComponentSwizzle.Identity
                },
                SubresourceRange = new ImageSubresourceRange
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    BaseMipLevel = 0,
                    LevelCount = 1,
                    BaseArrayLayer = 0,
                    LayerCount = 1
                }
            };

            ImageView view;
            result = _vk.CreateImageView(_device, &viewInfo, null, &view);
            if (result != Result.Success)
                throw new InvalidOperationException($"Failed to create swapchain image view: {result}");

            _imageViews[i] = view;
        }

        s_logger.LogInformation(
            "Created swapchain: {Width}x{Height}, Format={Format}, ColorSpace={ColorSpace}, HDR={IsHdr}, Images={ImageCount}",
            _extent.Width, _extent.Height, _format, _colorSpace, _isHdr, _images.Length);
    }

    private void SelectFormat(out Format format, out ColorSpaceKHR colorSpace, out bool isHdr)
    {
        uint formatCount = 0;
        _khrSurface.GetPhysicalDeviceSurfaceFormats(_physicalDevice, _surface, &formatCount, null);

        var formats = new SurfaceFormatKHR[formatCount];
        fixed (SurfaceFormatKHR* pFormats = formats)
        {
            _khrSurface.GetPhysicalDeviceSurfaceFormats(_physicalDevice, _surface, &formatCount, pFormats);
        }

        // Log available formats
        foreach (var f in formats)
        {
            s_logger.LogDebug("Available surface format: {Format} / {ColorSpace}", f.Format, f.ColorSpace);
        }

        // Prefer HDR: RGBA16Float + Extended sRGB Linear
        foreach (var f in formats)
        {
            if (f.Format == Format.R16G16B16A16Sfloat &&
                f.ColorSpace == ColorSpaceKHR.SpaceExtendedSrgbLinearExt)
            {
                format = f.Format;
                colorSpace = f.ColorSpace;
                isHdr = true;
                s_logger.LogInformation("Selected HDR format: R16G16B16A16_SFLOAT + EXTENDED_SRGB_LINEAR");
                return;
            }
        }

        // Fallback: B8G8R8A8_SRGB
        foreach (var f in formats)
        {
            if (f.Format == Format.B8G8R8A8Srgb &&
                f.ColorSpace == ColorSpaceKHR.SpaceSrgbNonlinearKhr)
            {
                format = f.Format;
                colorSpace = f.ColorSpace;
                isHdr = false;
                s_logger.LogInformation("Selected SDR format: B8G8R8A8_SRGB + SRGB_NONLINEAR");
                return;
            }
        }

        // Last resort: first available format
        if (formats.Length > 0)
        {
            format = formats[0].Format;
            colorSpace = formats[0].ColorSpace;
            isHdr = false;
            s_logger.LogInformation("Selected fallback format: {Format} / {ColorSpace}", format, colorSpace);
            return;
        }

        throw new InvalidOperationException("No surface formats available");
    }

    private PresentModeKHR SelectPresentMode()
    {
        uint modeCount = 0;
        _khrSurface.GetPhysicalDeviceSurfacePresentModes(_physicalDevice, _surface, &modeCount, null);

        var modes = new PresentModeKHR[modeCount];
        fixed (PresentModeKHR* pModes = modes)
        {
            _khrSurface.GetPhysicalDeviceSurfacePresentModes(_physicalDevice, _surface, &modeCount, pModes);
        }

        // Prefer Mailbox (low-latency, no tearing)
        foreach (var mode in modes)
        {
            if (mode == PresentModeKHR.MailboxKhr)
                return PresentModeKHR.MailboxKhr;
        }

        // FIFO is always guaranteed
        return PresentModeKHR.FifoKhr;
    }

    private static Extent2D SelectExtent(SurfaceCapabilitiesKHR capabilities, uint requestedWidth, uint requestedHeight)
    {
        if (capabilities.CurrentExtent.Width != uint.MaxValue)
            return capabilities.CurrentExtent;

        return new Extent2D
        {
            Width = Math.Clamp(requestedWidth, capabilities.MinImageExtent.Width, capabilities.MaxImageExtent.Width),
            Height = Math.Clamp(requestedHeight, capabilities.MinImageExtent.Height, capabilities.MaxImageExtent.Height)
        };
    }

    private void DestroyImageViews()
    {
        foreach (var view in _imageViews)
        {
            if (view.Handle != 0)
                _vk.DestroyImageView(_device, view, null);
        }

        _imageViews = [];
        _images = [];
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _vk.DeviceWaitIdle(_device);
        DestroyImageViews();

        if (_swapchain.Handle != 0)
            _khrSwapchain.DestroySwapchain(_device, _swapchain, null);
    }
}
