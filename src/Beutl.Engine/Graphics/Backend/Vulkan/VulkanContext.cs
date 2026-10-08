using System.Runtime.InteropServices;
using System.Text.Json;
using Beutl.Graphics3D;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;
using SkiaSharp;

namespace Beutl.Graphics.Backend.Vulkan;

using Image = Silk.NET.Vulkan.Image;

internal sealed unsafe partial class VulkanContext : IGraphicsContext
{
    private static readonly ILogger s_logger = Log.CreateLogger<VulkanContext>();
    private static readonly ScopedObservers<TextureFormat> s_textureAllocationObservers = new();
    private readonly VulkanInstance _vulkanInstance;
    private readonly VulkanDevice _vulkanDevice;
    private readonly VulkanCommandPool _vulkanCommandPool;
    private readonly object _skiaImagesLock = new();
    private readonly Dictionary<ulong, ImageCreateInfo> _skiaImages = [];
    private readonly VkCreateImageDelegate _createImage;
    private readonly VkDestroyImageDelegate _destroyImage;
    private readonly VkBindImageMemoryDelegate _bindImageMemory;
    private readonly VkBindImageMemory2Delegate? _bindImageMemory2;
    private readonly VkBindImageMemory2Delegate? _bindImageMemory2Khr;
    private readonly VkCreateImageDelegate _createImageProxyDelegate;
    private readonly VkDestroyImageDelegate _destroyImageProxyDelegate;
    private readonly VkBindImageMemoryDelegate _bindImageMemoryProxyDelegate;
    private readonly VkBindImageMemory2Delegate? _bindImageMemory2ProxyDelegate;
    private readonly VkBindImageMemory2Delegate? _bindImageMemory2KhrProxyDelegate;
    private readonly IntPtr _createImageProxy;
    private readonly IntPtr _destroyImageProxy;
    private readonly IntPtr _bindImageMemoryProxy;
    private readonly IntPtr _bindImageMemory2Proxy;
    private readonly IntPtr _bindImageMemory2KhrProxy;
    private GRContext? _skiaContext;
    private GRVkBackendContext? _skiaBackendContext;
    private bool _disposed;

    public VulkanContext(VulkanInstance vulkanInstance, VulkanPhysicalDeviceInfo physicalDevice)
    {
        // Ganesh requires Vulkan 1.1 and aborts the process, rather than returning null, on an older device. Refusing
        // the device before anything is created on it lets GraphicsContextFactory fall back to CPU rendering instead
        // of sharing a context whose SkiaContext throws.
        if (!physicalDevice.IsMoltenVK && physicalDevice.ApiVersionInt < Vk.Version11)
        {
            throw new NotSupportedException(
                $"{physicalDevice.Name} supports Vulkan {physicalDevice.ApiVersion}, and Skia requires 1.1.");
        }

        // Check before allocating device resources. A stock native library cannot synchronize the
        // shared image state; let the caller report/fall back at context creation instead of returning
        // null render targets after the first missing interop entry point.
        if (!physicalDevice.IsMoltenVK)
            SkiaVulkanInterop.EnsureAvailable();

        _vulkanInstance = vulkanInstance;
        _vulkanDevice = new VulkanDevice(vulkanInstance.Vk, vulkanInstance.Instance, physicalDevice.Device);
        _vulkanCommandPool = new VulkanCommandPool(_vulkanDevice);
        _createImage = GetDeviceDelegate<VkCreateImageDelegate>("vkCreateImage");
        _destroyImage = GetDeviceDelegate<VkDestroyImageDelegate>("vkDestroyImage");
        _bindImageMemory = GetDeviceDelegate<VkBindImageMemoryDelegate>("vkBindImageMemory");
        // Skia's allocator picks whichever bind entry point the device exposes, so the core 1.1 and KHR
        // forms have to carry the same initialization contract as the 1.0 one. Either may be absent.
        _bindImageMemory2 = TryGetDeviceDelegate<VkBindImageMemory2Delegate>("vkBindImageMemory2");
        _bindImageMemory2Khr = TryGetDeviceDelegate<VkBindImageMemory2Delegate>("vkBindImageMemory2KHR");
        // Ganesh creates its filter layers and scratch images through these callbacks. Vulkan
        // leaves a newly bound image undefined, and SwiftShader can expose bytes from a previously
        // freed allocation, so make initialization part of image binding instead of relying on
        // every Skia caller to happen to overwrite the complete allocation.
        _createImageProxy = Marshal.GetFunctionPointerForDelegate(_createImageProxyDelegate = CreateSkiaImage);
        _destroyImageProxy = Marshal.GetFunctionPointerForDelegate(_destroyImageProxyDelegate = DestroySkiaImage);
        _bindImageMemoryProxy = Marshal.GetFunctionPointerForDelegate(_bindImageMemoryProxyDelegate = BindSkiaImageMemory);
        if (_bindImageMemory2 is not null)
        {
            _bindImageMemory2Proxy = Marshal.GetFunctionPointerForDelegate(
                _bindImageMemory2ProxyDelegate = BindSkiaImageMemory2);
        }

        if (_bindImageMemory2Khr is not null)
        {
            _bindImageMemory2KhrProxy = Marshal.GetFunctionPointerForDelegate(
                _bindImageMemory2KhrProxyDelegate = BindSkiaImageMemory2Khr);
        }

        if (!physicalDevice.IsMoltenVK)
        {
            InitializeSkiaVulkanContext();
        }

        s_logger.LogDebug("Vulkan context created successfully");
    }

    private void InitializeSkiaVulkanContext()
    {
        try
        {
            _skiaBackendContext = new GRVkBackendContext
            {
                VkInstance = _vulkanInstance.Instance.Handle,
                VkPhysicalDevice = _vulkanDevice.PhysicalDevice.Handle,
                VkDevice = _vulkanDevice.Device.Handle,
                VkQueue = _vulkanDevice.GraphicsQueue.Handle,
                GraphicsQueueIndex = _vulkanDevice.GraphicsQueueFamilyIndex,
                // Left at 0, Skia assumes the loader's version, which can exceed what the instance was created for.
                MaxAPIVersion = VulkanInstance.InstanceApiVersion,
                GetProcedureAddress = GetVulkanProcAddress
            };

            _skiaContext = GRContext.CreateVulkan(_skiaBackendContext);

            if (_skiaContext == null)
            {
                s_logger.LogWarning("Failed to create SkiaSharp Vulkan context");
            }
        }
        catch (Exception ex)
        {
            s_logger.LogError(ex, "Failed to initialize SkiaSharp Vulkan backend");
        }
    }

    public GraphicsBackend Backend => GraphicsBackend.Vulkan;

    public GRContext SkiaContext => _skiaContext ?? throw new InvalidOperationException(
        "SkiaSharp Vulkan context is not initialized. Make sure the Vulkan context was created successfully.");

    public Vk Vk => _vulkanInstance.Vk;

    public Instance Instance => _vulkanInstance.Instance;

    public PhysicalDevice PhysicalDevice => _vulkanDevice.PhysicalDevice;

    public Device Device => _vulkanDevice.Device;

    /// <inheritdoc cref="VulkanDevice.SupportsTimelineSemaphores"/>
    public bool SupportsTimelineSemaphores => _vulkanDevice.SupportsTimelineSemaphores;

    /// <inheritdoc cref="VulkanDevice.SupportsShaderInt64"/>
    public bool SupportsShaderInt64 => _vulkanDevice.SupportsShaderInt64;

    /// <inheritdoc cref="VulkanDevice.SupportsShaderFloat64"/>
    public bool SupportsShaderFloat64 => _vulkanDevice.SupportsShaderFloat64;

    /// <inheritdoc cref="VulkanDevice.SupportsImageCubeArray"/>
    public bool SupportsImageCubeArray => _vulkanDevice.SupportsImageCubeArray;

    public uint GraphicsQueueFamilyIndex => _vulkanDevice.GraphicsQueueFamilyIndex;

    public IEnumerable<string> EnabledExtensions =>
        _vulkanInstance.EnabledExtensions.Concat(_vulkanDevice.EnabledExtensions);

    public bool Supports3DRendering => true;

    public int MaxAttachmentDimension => _vulkanDevice.MaxAttachmentDimension;

    /// <inheritdoc cref="VulkanDevice.MaxFragmentShaderInputTextures"/>
    internal int MaxFragmentShaderInputTextures => _vulkanDevice.MaxFragmentShaderInputTextures;

    /// <inheritdoc cref="VulkanDevice.MaxImageDimension2D"/>
    internal int MaxImageDimension2D => _vulkanDevice.MaxImageDimension2D;

    /// <inheritdoc cref="VulkanDevice.MaxFramebufferWidth"/>
    internal int MaxFramebufferWidth => _vulkanDevice.MaxFramebufferWidth;

    /// <inheritdoc cref="VulkanDevice.MaxFramebufferHeight"/>
    internal int MaxFramebufferHeight => _vulkanDevice.MaxFramebufferHeight;

    /// <summary>
    /// Refuses an extent this device cannot make of an image created with attachment usage, which every
    /// texture here is; see <see cref="DeviceExtentLimits.ThrowIfCannotMakeAttachableImage"/>.
    /// </summary>
    internal void ThrowIfCannotMakeAttachableImage(int maxImageDimension, int width, int height)
        => DeviceExtentLimits.ThrowIfCannotMakeAttachableImage(
            maxImageDimension, MaxFramebufferWidth, MaxFramebufferHeight, width, height);

    public int MaxCubeFaceDimension => _vulkanDevice.MaxCubeFaceDimension;

    internal static IDisposable ObserveTextureAllocations(Action<TextureFormat> observer)
        => s_textureAllocationObservers.Observe(observer);

    internal static void RecordTextureAllocation(TextureFormat format)
        => s_textureAllocationObservers.Record(format);

    public void WaitIdle()
    {
        _vulkanCommandPool.Flush(waitForCompletion: true);
        _vulkanDevice.WaitIdle();
    }

    public void RecordCommands(Action<CommandBuffer> record)
    {
        _vulkanCommandPool.RecordCommands(record);
    }

    internal void SubmitIsolatedCommands(Action<CommandBuffer> record)
    {
        _vulkanCommandPool.SubmitIsolatedCommands(record);
    }

    public void TransitionImageLayout(Image image, ImageLayout oldLayout, ImageLayout newLayout, ImageAspectFlags aspectMask)
    {
        _vulkanCommandPool.TransitionImageLayout(image, oldLayout, newLayout, aspectMask);
    }

    public void TransitionImageLayout(
        Image image,
        ImageLayout oldLayout,
        ImageLayout newLayout,
        ImageAspectFlags aspectMask,
        uint baseArrayLayer,
        uint layerCount)
    {
        _vulkanCommandPool.TransitionImageLayout(image, oldLayout, newLayout, aspectMask, baseArrayLayer, layerCount);
    }

    public CommandBuffer GetRecordingCommandBuffer()
    {
        return _vulkanCommandPool.GetRecordingCommandBuffer();
    }

    public void FlushCommands(bool waitForCompletion)
    {
        _vulkanCommandPool.Flush(waitForCompletion);
    }

    /// <inheritdoc cref="VulkanCommandPool.SubmitSignalingTimeline"/>
    public void SubmitSignalingTimeline(Silk.NET.Vulkan.Semaphore timeline, ulong value)
    {
        _vulkanCommandPool.SubmitSignalingTimeline(timeline, value);
    }

    /// <inheritdoc cref="VulkanCommandPool.ThrowIfRenderPassActive"/>
    public void ThrowIfRenderPassActive()
    {
        _vulkanCommandPool.ThrowIfRenderPassActive();
    }

    /// <inheritdoc cref="VulkanCommandPool.BeginRenderPassScope(IVulkanRenderPassSuspension)"/>
    public void BeginRenderPassScope(IVulkanRenderPassSuspension owner)
    {
        _vulkanCommandPool.BeginRenderPassScope(owner);
    }

    /// <inheritdoc cref="VulkanCommandPool.EndRenderPassScope(IVulkanRenderPassSuspension)"/>
    public void EndRenderPassScope(IVulkanRenderPassSuspension owner)
    {
        _vulkanCommandPool.EndRenderPassScope(owner);
    }

    public void DeferRelease(Action release)
    {
        _vulkanCommandPool.DeferRelease(release);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            _vulkanCommandPool.Flush(waitForCompletion: true);
            _vulkanDevice.WaitIdle();
        }
        finally
        {
            try
            {
                _skiaContext?.Dispose();
                _skiaContext = null;
                _skiaBackendContext?.Dispose();
                _skiaBackendContext = null;
            }
            finally
            {
                try
                {
                    _vulkanCommandPool.Dispose();
                }
                finally
                {
                    _vulkanDevice.Dispose();
                }
            }
        }
    }
}
