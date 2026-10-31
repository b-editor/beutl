using System.Runtime.InteropServices;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;

namespace Beutl.Graphics.Backend.Vulkan;

internal sealed unsafe class VulkanDevice : IDisposable
{
    private static readonly ILogger s_logger = Log.CreateLogger<VulkanDevice>();
    private readonly Vk _vk;
    private readonly PhysicalDevice _physicalDevice;
    private readonly Device _device;
    private readonly Queue _graphicsQueue;
    private readonly uint _graphicsQueueFamilyIndex;
    private readonly string[] _enabledExtensions;
    private readonly PhysicalDeviceFeatures _enabledFeatures;
    private readonly bool _timelineSemaphores;
    private bool _disposed;

    public VulkanDevice(Vk vk, PhysicalDevice physicalDevice)
    {
        _vk = vk;
        _physicalDevice = physicalDevice;

        _graphicsQueueFamilyIndex = VulkanPhysicalDeviceQueries.FindGraphicsQueueFamily(
            _vk, _physicalDevice, out QueueFlags queueFlags);
        GraphicsQueueSupportsCompute = (queueFlags & QueueFlags.ComputeBit) != 0;
        _enabledExtensions = GetRequiredDeviceExtensions();
        _device = CreateDevice(_enabledExtensions, out _enabledFeatures, out _timelineSemaphores);

        _vk.GetDeviceQueue(_device, _graphicsQueueFamilyIndex, 0, out _graphicsQueue);

        PhysicalDeviceProperties properties;
        _vk.GetPhysicalDeviceProperties(_physicalDevice, &properties);
        var deviceName = Marshal.PtrToStringAnsi((IntPtr)properties.DeviceName);
        s_logger.LogInformation("Using GPU: {DeviceName}", deviceName);

        Limits = properties.Limits;

        // An intermediate is sampled and drawn into, so it has to satisfy both the image limit and the
        // framebuffer one. These are not the same number: a device can sample an image wider than it can
        // attach, and attaching is what a render target is for.
        MaxImageDimension2D = (int)properties.Limits.MaxImageDimension2D;
        MaxFramebufferWidth = (int)properties.Limits.MaxFramebufferWidth;
        MaxFramebufferHeight = (int)properties.Limits.MaxFramebufferHeight;
        MaxAttachmentDimension = (int)Math.Min(
            properties.Limits.MaxImageDimension2D,
            Math.Min(properties.Limits.MaxFramebufferWidth, properties.Limits.MaxFramebufferHeight));

        // A cube image is bounded by its own limit, not by the 2D one: a pass that renders into a cube
        // through per-face 2D attachments has to fit both, and the two may differ.
        MaxCubeFaceDimension = (int)properties.Limits.MaxImageDimensionCube;
        MaxFragmentShaderInputTextures = GetMaxFragmentShaderInputTextures(properties.Limits);
    }

    /// <summary>Gets the combined-image-sampler limit for one fragment stage and descriptor set.</summary>
    public int MaxFragmentShaderInputTextures { get; }

    internal static int GetMaxFragmentShaderInputTextures(PhysicalDeviceLimits limits)
    {
        uint maximum = Math.Min(limits.MaxPerStageDescriptorSamplers, limits.MaxPerStageDescriptorSampledImages);
        maximum = Math.Min(maximum, limits.MaxDescriptorSetSamplers);
        maximum = Math.Min(maximum, limits.MaxDescriptorSetSampledImages);
        maximum = Math.Min(maximum, limits.MaxPerStageResources);
        return (int)Math.Min(maximum, (uint)int.MaxValue);
    }

    /// <summary>Gets the largest 2D image this device can create and sample, attached or not.</summary>
    /// <remarks>
    /// A device may sample an image wider than it can attach, so a texture that is only ever sampled is
    /// bounded by this rather than by <see cref="MaxAttachmentDimension"/>.
    /// </remarks>
    public int MaxImageDimension2D { get; }

    /// <summary>Gets the widest framebuffer this device can build.</summary>
    /// <remarks>
    /// The two framebuffer limits may differ, so a framebuffer is measured axis by axis against them;
    /// <see cref="MaxAttachmentDimension"/> is the square budget a caller that owns a density fits into.
    /// </remarks>
    public int MaxFramebufferWidth { get; }

    /// <summary>Gets the tallest framebuffer this device can build.</summary>
    public int MaxFramebufferHeight { get; }

    /// <summary>Gets the largest square this device can both sample and attach.</summary>
    public int MaxAttachmentDimension { get; }

    /// <summary>Gets the largest cube face this device can create and sample.</summary>
    public int MaxCubeFaceDimension { get; }

    public Vk Vk => _vk;

    public PhysicalDevice PhysicalDevice => _physicalDevice;

    public Device Device => _device;

    /// <summary>Whether <see cref="Device"/> has been destroyed and can no longer be called.</summary>
    /// <remarks>The handle keeps its old value after destruction, so consult this before issuing work.</remarks>
    public bool IsDisposed => _disposed;

    public Queue GraphicsQueue => _graphicsQueue;

    public uint GraphicsQueueFamilyIndex => _graphicsQueueFamilyIndex;

    public string[] EnabledExtensions => _enabledExtensions;

    /// <summary>Whether the logical device enabled timeline semaphores.</summary>
    /// <remarks>
    /// macOS relies on them: there Skia renders through Metal on another queue, and a timeline semaphore exported
    /// as an <c>MTLSharedEvent</c> orders the two queues on the GPU instead of the CPU. Elsewhere enabling the
    /// feature changes nothing until something submits with one.
    /// </remarks>
    public bool SupportsTimelineSemaphores => _timelineSemaphores;

    /// <summary>Whether the logical device enabled 64-bit integer arithmetic in shaders.</summary>
    public bool SupportsShaderInt64 => _enabledFeatures.ShaderInt64;

    /// <summary>Whether the logical device enabled 64-bit floating-point arithmetic in shaders.</summary>
    public bool SupportsShaderFloat64 => _enabledFeatures.ShaderFloat64;

    /// <summary>Whether the logical device enabled cube-array image views and sampling.</summary>
    public bool SupportsImageCubeArray => _enabledFeatures.ImageCubeArray;

    internal PhysicalDeviceFeatures EnabledFeatures => _enabledFeatures;

    /// <summary>The physical device's limits, read once when the device is created.</summary>
    internal PhysicalDeviceLimits Limits { get; }

    /// <summary>Whether the queue every command is recorded for can also dispatch compute work.</summary>
    internal bool GraphicsQueueSupportsCompute { get; }

    internal bool SupportsTriangleFans { get; private set; } = true;
    internal bool SupportsVertexAttributeBeyondStride { get; private set; } = true;
    internal uint VertexStrideAlignment { get; private set; }


    private string[] GetRequiredDeviceExtensions()
    {
        var extensions = new List<string>();

        HashSet<string> availableNames = VulkanPhysicalDeviceQueries.GetDeviceExtensionNames(_vk, _physicalDevice);

        if (availableNames.Contains("VK_KHR_swapchain"))
            extensions.Add("VK_KHR_swapchain");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            if (availableNames.Contains("VK_KHR_portability_subset"))
                extensions.Add("VK_KHR_portability_subset");

            if (availableNames.Contains("VK_EXT_metal_objects"))
                extensions.Add("VK_EXT_metal_objects");
        }

        return extensions.ToArray();
    }

    private Device CreateDevice(
        string[] extensions,
        out PhysicalDeviceFeatures enabledFeatures,
        out bool timelineSemaphores)
    {
        float queuePriority = 1.0f;
        var queueCreateInfo = new DeviceQueueCreateInfo
        {
            SType = StructureType.DeviceQueueCreateInfo,
            QueueFamilyIndex = _graphicsQueueFamilyIndex,
            QueueCount = 1,
            PQueuePriorities = &queuePriority
        };

        // Vulkan features must be enabled before use even when the physical device advertises them.
        // 64-bit specialization constants need shaderInt64/shaderFloat64; point shadows need imageCubeArray.
        PhysicalDeviceFeatures available;
        _vk.GetPhysicalDeviceFeatures(_physicalDevice, &available);
        var features = new PhysicalDeviceFeatures
        {
            ShaderInt64 = available.ShaderInt64,
            ShaderFloat64 = available.ShaderFloat64,
            ImageCubeArray = available.ImageCubeArray,
            VertexPipelineStoresAndAtomics = available.VertexPipelineStoresAndAtomics,
            FragmentStoresAndAtomics = available.FragmentStoresAndAtomics,
            ShaderStorageImageExtendedFormats = available.ShaderStorageImageExtendedFormats,
            FullDrawIndexUint32 = available.FullDrawIndexUint32,
            GeometryShader = available.GeometryShader,
            TessellationShader = available.TessellationShader,
            ShaderTessellationAndGeometryPointSize = available.ShaderTessellationAndGeometryPointSize,
            MultiDrawIndirect = available.MultiDrawIndirect,
            DrawIndirectFirstInstance = available.DrawIndirectFirstInstance,
        };
        enabledFeatures = features;

        var createInfo = new DeviceCreateInfo
        {
            SType = StructureType.DeviceCreateInfo,
            QueueCreateInfoCount = 1,
            PQueueCreateInfos = &queueCreateInfo,
            PEnabledFeatures = &features
        };

        var timelineFeatures = new PhysicalDeviceTimelineSemaphoreFeatures
        {
            SType = StructureType.PhysicalDeviceTimelineSemaphoreFeatures,
        };
        timelineSemaphores = false;
        PhysicalDeviceProperties deviceProperties;
        _vk.GetPhysicalDeviceProperties(_physicalDevice, &deviceProperties);
        // The feature structure is core only from Vulkan 1.2; an older device would need the KHR extension.
        if (deviceProperties.ApiVersion >= Vk.Version12)
        {
            var availableFeatures2 = new PhysicalDeviceFeatures2
            {
                SType = StructureType.PhysicalDeviceFeatures2,
                PNext = &timelineFeatures,
            };
            _vk.GetPhysicalDeviceFeatures2(_physicalDevice, &availableFeatures2);
            timelineSemaphores = timelineFeatures.TimelineSemaphore;
            if (timelineSemaphores)
            {
                timelineFeatures.PNext = null;
                createInfo.PNext = &timelineFeatures;
            }
        }

        var portability = new PhysicalDevicePortabilitySubsetFeaturesKHR
        {
            SType = StructureType.PhysicalDevicePortabilitySubsetFeaturesKhr,
        };
        if (extensions.Contains("VK_KHR_portability_subset"))
        {
            var features2 = new PhysicalDeviceFeatures2
            {
                SType = StructureType.PhysicalDeviceFeatures2,
                PNext = &portability,
            };
            _vk.GetPhysicalDeviceFeatures2(_physicalDevice, &features2);
            SupportsTriangleFans = portability.TriangleFans;
            SupportsVertexAttributeBeyondStride = portability.VertexAttributeAccessBeyondStride;
            portability = new()
            {
                SType = StructureType.PhysicalDevicePortabilitySubsetFeaturesKhr,
                TriangleFans = portability.TriangleFans,
                VertexAttributeAccessBeyondStride = portability.VertexAttributeAccessBeyondStride,
                PNext = createInfo.PNext,
            };
            createInfo.PNext = &portability;
            var portabilityProperties = new PhysicalDevicePortabilitySubsetPropertiesKHR
            {
                SType = StructureType.PhysicalDevicePortabilitySubsetPropertiesKhr,
            };
            var properties2 = new PhysicalDeviceProperties2
            {
                SType = StructureType.PhysicalDeviceProperties2,
                PNext = &portabilityProperties,
            };
            _vk.GetPhysicalDeviceProperties2(_physicalDevice, &properties2);
            VertexStrideAlignment = Math.Max(1, portabilityProperties.MinVertexInputBindingStrideAlignment);
        }

        using var extensionNames = new VulkanNameArray(extensions);
        fixed (byte** ppExtensions = extensionNames.Pointers)
        {
            createInfo.EnabledExtensionCount = extensionNames.Count;
            createInfo.PpEnabledExtensionNames = ppExtensions;

            Device device;
            var result = _vk.CreateDevice(_physicalDevice, &createInfo, null, &device);
            if (result != Result.Success)
            {
                throw new InvalidOperationException($"Failed to create Vulkan device: {result}");
            }

            return device;
        }
    }

    public void WaitIdle()
    {
        _vk.DeviceWaitIdle(_device);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _vk.DestroyDevice(_device, null);
    }
}
