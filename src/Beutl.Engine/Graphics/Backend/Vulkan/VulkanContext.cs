using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text.Json;
using Beutl.Graphics3D;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;
using SkiaSharp;

namespace Beutl.Graphics.Backend.Vulkan;

using Image = Silk.NET.Vulkan.Image;

internal sealed unsafe class VulkanContext : IGraphicsContext
{
    private static readonly ILogger s_logger = Log.CreateLogger<VulkanContext>();
    private static readonly AsyncLocal<TextureAllocationObservationScope?> s_textureAllocationObserver = new();
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
        _vulkanInstance = vulkanInstance;
        _vulkanDevice = new VulkanDevice(vulkanInstance.Vk, vulkanInstance.Instance, physicalDevice.Device);
        _vulkanCommandPool = new VulkanCommandPool(
            vulkanInstance.Vk,
            _vulkanDevice.Device,
            _vulkanDevice.GraphicsQueue,
            _vulkanDevice.GraphicsQueueFamilyIndex);
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

    /// <summary>
    /// Resolves a Vulkan entry point for Skia, substituting this context's proxies for the image
    /// create/destroy/bind calls it intercepts.
    /// </summary>
    internal IntPtr GetVulkanProcAddress(string name, IntPtr instance, IntPtr device)
    {
        if (name == "vkCreateImage")
            return _createImageProxy;
        if (name == "vkDestroyImage")
            return _destroyImageProxy;
        if (name == "vkBindImageMemory")
            return _bindImageMemoryProxy;
        if (name == "vkBindImageMemory2" && _bindImageMemory2Proxy != IntPtr.Zero)
            return _bindImageMemory2Proxy;
        if (name == "vkBindImageMemory2KHR" && _bindImageMemory2KhrProxy != IntPtr.Zero)
            return _bindImageMemory2KhrProxy;

        var vk = _vulkanInstance.Vk;

        if (device != IntPtr.Zero)
        {
            var deviceHandle = new Device(device);
            var addr = vk.GetDeviceProcAddr(deviceHandle, name);
            if (addr != IntPtr.Zero)
                return addr;
        }

        if (instance != IntPtr.Zero)
        {
            var instanceHandle = new Instance(instance);
            var addr = vk.GetInstanceProcAddr(instanceHandle, name);
            if (addr != IntPtr.Zero)
                return addr;
        }

        return vk.GetInstanceProcAddr(_vulkanInstance.Instance, name);
    }

    private T GetDeviceDelegate<T>(string name)
        where T : Delegate
        => TryGetDeviceDelegate<T>(name)
           ?? throw new InvalidOperationException($"Vulkan device function '{name}' is unavailable.");

    private T? TryGetDeviceDelegate<T>(string name)
        where T : Delegate
    {
        IntPtr address = _vulkanInstance.Vk.GetDeviceProcAddr(_vulkanDevice.Device, name);
        return address == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer<T>(address);
    }

    private unsafe Result CreateSkiaImage(
        Device device,
        ImageCreateInfo* createInfo,
        AllocationCallbacks* allocator,
        Image* image)
    {
        ImageCreateInfo initializedInfo = PrepareSkiaImageCreateInfo(*createInfo);

        Result result = _createImage(device, &initializedInfo, allocator, image);
        if (result == Result.Success)
        {
            lock (_skiaImagesLock)
                _skiaImages[image->Handle] = initializedInfo;
        }
        return result;
    }

    private unsafe void DestroySkiaImage(
        Device device,
        Image image,
        AllocationCallbacks* allocator)
    {
        lock (_skiaImagesLock)
            _skiaImages.Remove(image.Handle);
        _destroyImage(device, image, allocator);
    }

    private unsafe Result BindSkiaImageMemory(
        Device device,
        Image image,
        DeviceMemory memory,
        ulong memoryOffset)
    {
        Result result = _bindImageMemory(device, image, memory, memoryOffset);
        ImageCreateInfo createInfo;
        lock (_skiaImagesLock)
            _skiaImages.TryGetValue(image.Handle, out createInfo);
        if (result == Result.Success && RequiresTransparentInitialization(createInfo))
        {
            try
            {
                ClearSkiaImage(image, createInfo);
            }
            catch (Exception ex)
            {
                // Never let a managed exception cross the unmanaged Vulkan callback boundary.
                // Rejecting the bind makes Skia discard the allocation instead of observing
                // an image whose contents were never defined.
                s_logger.LogError(ex, "Failed to initialize a Skia Vulkan image.");
                return Result.ErrorInitializationFailed;
            }
        }
        return result;
    }

    private unsafe Result BindSkiaImageMemory2(
        Device device,
        uint bindInfoCount,
        BindImageMemoryInfo* bindInfos)
        => BindSkiaImageMemoryBatch(_bindImageMemory2!, device, bindInfoCount, bindInfos);

    private unsafe Result BindSkiaImageMemory2Khr(
        Device device,
        uint bindInfoCount,
        BindImageMemoryInfo* bindInfos)
        => BindSkiaImageMemoryBatch(_bindImageMemory2Khr!, device, bindInfoCount, bindInfos);

    // vkBindImageMemory2 binds the whole batch or none of it, so initialization follows a successful call
    // and covers every image in the batch that the single-bind path would have cleared.
    private unsafe Result BindSkiaImageMemoryBatch(
        VkBindImageMemory2Delegate bind,
        Device device,
        uint bindInfoCount,
        BindImageMemoryInfo* bindInfos)
    {
        Result result = bind(device, bindInfoCount, bindInfos);
        if (result != Result.Success || bindInfos is null)
            return result;

        for (uint index = 0; index < bindInfoCount; index++)
        {
            Image image = bindInfos[index].Image;
            ImageCreateInfo createInfo;
            lock (_skiaImagesLock)
                _skiaImages.TryGetValue(image.Handle, out createInfo);
            if (!RequiresTransparentInitialization(createInfo))
                continue;

            try
            {
                ClearSkiaImage(image, createInfo);
            }
            catch (Exception ex)
            {
                // Never let a managed exception cross the unmanaged Vulkan callback boundary. The bind
                // already succeeded here, so the batch cannot be undone; reporting the failure makes Skia
                // discard the allocation rather than draw from memory whose contents were never defined.
                s_logger.LogError(ex, "Failed to initialize a Skia Vulkan image.");
                return Result.ErrorInitializationFailed;
            }
        }

        return result;
    }

    internal static ImageCreateInfo PrepareSkiaImageCreateInfo(ImageCreateInfo createInfo)
    {
        if ((createInfo.Usage & ImageUsageFlags.ColorAttachmentBit) != 0)
            createInfo.Usage |= ImageUsageFlags.TransferDstBit;
        return createInfo;
    }

    internal static bool RequiresTransparentInitialization(ImageCreateInfo createInfo)
        => createInfo.InitialLayout == ImageLayout.Undefined
           && (createInfo.Usage & ImageUsageFlags.ColorAttachmentBit) != 0;

    internal static ImageSubresourceRange CreateInitializationRange(ImageCreateInfo createInfo)
        => new()
        {
            AspectMask = ImageAspectFlags.ColorBit,
            BaseMipLevel = 0,
            LevelCount = createInfo.MipLevels,
            BaseArrayLayer = 0,
            LayerCount = createInfo.ArrayLayers,
        };

    private unsafe void ClearSkiaImage(Image image, ImageCreateInfo createInfo)
    {
        _vulkanCommandPool.SubmitIsolatedCommands(commandBuffer =>
        {
            ImageSubresourceRange range = CreateInitializationRange(createInfo);
            var barrier = new ImageMemoryBarrier
            {
                SType = StructureType.ImageMemoryBarrier,
                OldLayout = ImageLayout.Undefined,
                NewLayout = ImageLayout.TransferDstOptimal,
                SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                Image = image,
                SubresourceRange = range,
                SrcAccessMask = 0,
                DstAccessMask = AccessFlags.TransferWriteBit,
            };
            Vk.CmdPipelineBarrier(
                commandBuffer,
                PipelineStageFlags.TopOfPipeBit,
                PipelineStageFlags.TransferBit,
                0,
                0, null,
                0, null,
                1, &barrier);

            var transparent = new ClearColorValue(0, 0, 0, 0);
            Vk.CmdClearColorImage(
                commandBuffer,
                image,
                ImageLayout.TransferDstOptimal,
                &transparent,
                1,
                &range);
        });
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private unsafe delegate Result VkCreateImageDelegate(
        Device device,
        ImageCreateInfo* createInfo,
        AllocationCallbacks* allocator,
        Image* image);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private unsafe delegate void VkDestroyImageDelegate(
        Device device,
        Image image,
        AllocationCallbacks* allocator);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private unsafe delegate Result VkBindImageMemoryDelegate(
        Device device,
        Image image,
        DeviceMemory memory,
        ulong memoryOffset);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private unsafe delegate Result VkBindImageMemory2Delegate(
        Device device,
        uint bindInfoCount,
        BindImageMemoryInfo* bindInfos);

    public GraphicsBackend Backend => GraphicsBackend.Vulkan;

    public GRContext SkiaContext => _skiaContext ?? throw new InvalidOperationException(
        "SkiaSharp Vulkan context is not initialized. Make sure the Vulkan context was created successfully.");

    public Vk Vk => _vulkanInstance.Vk;

    public Instance Instance => _vulkanInstance.Instance;

    public PhysicalDevice PhysicalDevice => _vulkanDevice.PhysicalDevice;

    public Device Device => _vulkanDevice.Device;

    /// <inheritdoc cref="VulkanDevice.SupportsShaderInt64"/>
    public bool SupportsShaderInt64 => _vulkanDevice.SupportsShaderInt64;

    /// <inheritdoc cref="VulkanDevice.SupportsShaderFloat64"/>
    public bool SupportsShaderFloat64 => _vulkanDevice.SupportsShaderFloat64;

    public Queue GraphicsQueue => _vulkanDevice.GraphicsQueue;

    public uint GraphicsQueueFamilyIndex => _vulkanDevice.GraphicsQueueFamilyIndex;

    public IEnumerable<string> EnabledExtensions =>
        _vulkanInstance.EnabledExtensions.Concat(_vulkanDevice.EnabledExtensions);

    public bool Supports3DRendering => true;

    internal static IDisposable ObserveTextureAllocations(Action<TextureFormat> observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        var scope = new TextureAllocationObservationScope(observer, s_textureAllocationObserver.Value);
        s_textureAllocationObserver.Value = scope;
        return scope;
    }

    internal static void RecordTextureAllocation(TextureFormat format)
    {
        for (TextureAllocationObservationScope? scope = s_textureAllocationObserver.Value;
             scope is not null;
             scope = scope.Parent)
        {
            try
            {
                scope.Observer(format);
            }
            catch
            {
                // Diagnostics must never affect texture allocation.
            }
        }
    }

    public ITexture2D CreateTexture2D(int width, int height, TextureFormat format)
    {
        ImageUsageFlags usage;
        if (format.IsDepthFormat())
        {
            usage = ImageUsageFlags.DepthStencilAttachmentBit | ImageUsageFlags.SampledBit |
                    ImageUsageFlags.TransferSrcBit | ImageUsageFlags.TransferDstBit;
        }
        else
        {
            usage = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.SampledBit |
                    ImageUsageFlags.TransferSrcBit | ImageUsageFlags.TransferDstBit;
        }
        var texture = new VulkanTexture2D(this, width, height, format, usage);
        RecordTextureAllocation(format);
        return texture;
    }

    public ITextureCube CreateTextureCube(int size, TextureFormat format)
    {
        var usage = format.IsDepthFormat()
            ? ImageUsageFlags.DepthStencilAttachmentBit | ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit
            : ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.SampledBit | ImageUsageFlags.TransferSrcBit;
        return new VulkanTextureCube(this, size, format, usage);
    }

    public ITextureArray CreateTextureArray(int width, int height, uint arraySize, TextureFormat format)
    {
        var usage = format.IsDepthFormat()
            ? ImageUsageFlags.DepthStencilAttachmentBit | ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit
            : ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.SampledBit | ImageUsageFlags.TransferSrcBit;
        return new VulkanTextureArray(this, width, height, arraySize, format, usage);
    }

    public ITextureCubeArray CreateTextureCubeArray(int size, uint arraySize, TextureFormat format)
    {
        var usage = format.IsDepthFormat()
            ? ImageUsageFlags.DepthStencilAttachmentBit | ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit
            : ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.SampledBit | ImageUsageFlags.TransferSrcBit;
        return new VulkanTextureCubeArray(this, size, arraySize, format, usage);
    }

    public IBuffer CreateBuffer(ulong size, BufferUsage usage, MemoryProperty memoryProperty)
    {
        return new VulkanBuffer(this, size, usage, memoryProperty);
    }

    public IShaderCompiler CreateShaderCompiler()
    {
        return new VulkanShaderCompiler();
    }

    /// <summary>
    /// Resolves a caller-supplied backend resource to its concrete type after confirming this context created
    /// it.
    /// </summary>
    /// <remarks>
    /// A Vulkan handle names nothing outside the device that produced it, and mixing two contexts' framebuffers,
    /// pipelines, descriptors, or copy operands is undefined behaviour the driver need not report. Rejecting the
    /// resource here turns that into an argument error before any handle reaches a native call.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="resource"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="resource"/> is not a <typeparamref name="TResource"/>, or belongs to another context.
    /// </exception>
    internal TResource RequireOwned<TResource>(object? resource, string parameterName)
        where TResource : class, IVulkanContextResource
    {
        ArgumentNullException.ThrowIfNull(resource, parameterName);
        if (resource is not TResource owned)
        {
            throw new ArgumentException(
                $"'{resource.GetType().Name}' is not a {typeof(TResource).Name} created by the Vulkan backend.",
                parameterName);
        }

        if (!ReferenceEquals(owned.OwnerContext, this))
        {
            throw new ArgumentException(
                $"The {typeof(TResource).Name} was created by a different Vulkan context; its handles are only "
                + "valid on the device that created it.",
                parameterName);
        }

        return owned;
    }

    public IRenderPass3D CreateRenderPass3D(
        IReadOnlyList<TextureFormat> colorFormats,
        TextureFormat? depthFormat,
        AttachmentLoadOp colorLoadOp = AttachmentLoadOp.Clear,
        AttachmentLoadOp depthLoadOp = AttachmentLoadOp.Clear)
    {
        if (colorFormats.Any(static format => format.IsDepthFormat()))
        {
            throw new ArgumentException("Color attachments cannot use a depth format.", nameof(colorFormats));
        }

        if (depthFormat is TextureFormat actualDepthFormat && !actualDepthFormat.IsDepthFormat())
        {
            throw new ArgumentException("The depth attachment must use a depth format.", nameof(depthFormat));
        }

        var vulkanColorFormats = colorFormats.Select(f => f.ToVulkanFormat()).ToList();
        Format? vulkanDepthFormat = depthFormat?.ToVulkanFormat();
        return new VulkanRenderPass3D(this, vulkanColorFormats, vulkanDepthFormat, colorLoadOp, depthLoadOp);
    }

    public IFramebuffer3D CreateFramebuffer3D(
        IRenderPass3D renderPass,
        IReadOnlyList<ITexture2D> colorTextures,
        ITexture2D? depthTexture)
    {
        var vulkanRenderPass = RequireOwned<VulkanRenderPass3D>(renderPass, nameof(renderPass));
        List<VulkanTexture2D> vulkanColorTextures = colorTextures
            .Select(texture => RequireOwned<VulkanTexture2D>(texture, nameof(colorTextures)))
            .ToList();
        VulkanTexture2D? vulkanDepthTexture = depthTexture is null
            ? null
            : RequireOwned<VulkanTexture2D>(depthTexture, nameof(depthTexture));
        return new VulkanFramebuffer3D(this, vulkanRenderPass, vulkanColorTextures, vulkanDepthTexture);
    }

    public IPipeline3D CreatePipeline3D(
        IRenderPass3D renderPass,
        byte[] vertexShaderSpirv,
        byte[] fragmentShaderSpirv,
        DescriptorBinding[] descriptorBindings,
        VertexInputDescription vertexInput,
        PipelineOptions? options = null)
    {
        var vulkanRenderPass = RequireOwned<VulkanRenderPass3D>(renderPass, nameof(renderPass));
        var vulkanBindings = descriptorBindings
            .Select(VulkanFlagConverter.ToVulkan)
            .ToArray();
        var vulkanVertexInput = VulkanFlagConverter.ToVulkan(vertexInput);
        var pipelineOptions = options ?? PipelineOptions.Default;
        ImmutableArray<SpecializationConstant> specializationConstants =
            ValidateSpecializationConstants(pipelineOptions.SpecializationConstants, nameof(options));
        ValidateSpecializationConstantPrecision(specializationConstants, nameof(options));

        if (!vulkanRenderPass.HasDepthAttachment
            && (pipelineOptions.DepthTestEnabled || pipelineOptions.DepthWriteEnabled))
        {
            throw new ArgumentException(
                "A pipeline without a depth attachment cannot enable depth testing or depth writes.",
                nameof(options));
        }

        return new VulkanPipeline3D(
            this,
            vulkanRenderPass.Handle,
            vertexShaderSpirv,
            fragmentShaderSpirv,
            vulkanVertexInput,
            vulkanBindings,
            specializationConstants,
            vulkanRenderPass.ColorAttachmentCount,
            vulkanRenderPass.HasDepthAttachment,
            pipelineOptions.DepthTestEnabled,
            pipelineOptions.DepthWriteEnabled,
            VulkanFlagConverter.ToVulkan(pipelineOptions.CullMode),
            VulkanFlagConverter.ToVulkan(pipelineOptions.FrontFace),
            pipelineOptions.BlendEnabled,
            VulkanFlagConverter.ToVulkan(pipelineOptions.SrcColorBlendFactor),
            VulkanFlagConverter.ToVulkan(pipelineOptions.DstColorBlendFactor),
            VulkanFlagConverter.ToVulkan(pipelineOptions.SrcAlphaBlendFactor),
            VulkanFlagConverter.ToVulkan(pipelineOptions.DstAlphaBlendFactor),
            VulkanFlagConverter.ToVulkan(pipelineOptions.ColorBlendOp),
            VulkanFlagConverter.ToVulkan(pipelineOptions.AlphaBlendOp));
    }

    internal static ImmutableArray<SpecializationConstant> ValidateSpecializationConstants(
        ImmutableArray<SpecializationConstant> constants,
        string parameterName)
    {
        if (constants.IsDefaultOrEmpty)
            return [];

        const ShaderStage supportedStages = ShaderStage.Vertex | ShaderStage.Fragment;
        var occupiedIds = new HashSet<(ShaderStage Stage, uint ConstantId)>();

        foreach (SpecializationConstant constant in constants)
        {
            if (constant.SizeInBytes is not (sizeof(uint) or sizeof(ulong)))
            {
                throw new ArgumentException(
                    $"Specialization constant {constant.ConstantId} has an invalid scalar size.",
                    parameterName);
            }

            if (constant.Stages == ShaderStage.None
                || (constant.Stages & ~supportedStages) != ShaderStage.None)
            {
                throw new ArgumentException(
                    $"Specialization constant {constant.ConstantId} must target only vertex or fragment stages.",
                    parameterName);
            }

            foreach (ShaderStage stage in new[] { ShaderStage.Vertex, ShaderStage.Fragment })
            {
                if ((constant.Stages & stage) != stage)
                    continue;

                if (!occupiedIds.Add((stage, constant.ConstantId)))
                {
                    throw new ArgumentException(
                        $"Specialization constant {constant.ConstantId} is specified more than once for the {stage} stage.",
                        parameterName);
                }
            }
        }

        return constants;
    }

    /// <summary>
    /// Rejects a 64-bit specialization constant this device cannot specialize with.
    /// </summary>
    /// <remarks>
    /// Reported here rather than left to <c>vkCreateGraphicsPipelines</c>, whose failure names neither the
    /// constant nor the missing feature.
    /// </remarks>
    private void ValidateSpecializationConstantPrecision(
        ImmutableArray<SpecializationConstant> constants,
        string parameterName)
    {
        foreach (SpecializationConstant constant in constants)
        {
            if (constant.RequiresShaderInt64 && !SupportsShaderInt64)
            {
                throw new ArgumentException(
                    $"Specialization constant {constant.ConstantId} is a 64-bit integer, which this Vulkan "
                    + "device does not support (shaderInt64).",
                    parameterName);
            }

            if (constant.RequiresShaderFloat64 && !SupportsShaderFloat64)
            {
                throw new ArgumentException(
                    $"Specialization constant {constant.ConstantId} is a 64-bit float, which this Vulkan "
                    + "device does not support (shaderFloat64).",
                    parameterName);
            }
        }
    }

    private sealed class TextureAllocationObservationScope(
        Action<TextureFormat> observer,
        TextureAllocationObservationScope? parent) : IDisposable
    {
        private bool _disposed;

        public Action<TextureFormat> Observer { get; } = observer;

        public TextureAllocationObservationScope? Parent { get; } = parent;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            if (ReferenceEquals(s_textureAllocationObserver.Value, this))
            {
                s_textureAllocationObserver.Value = Parent;
            }
        }
    }

    public IDescriptorSet CreateDescriptorSet(IPipeline3D pipeline, DescriptorPoolSize[] poolSizes)
    {
        var vulkanPipeline = RequireOwned<VulkanPipeline3D>(pipeline, nameof(pipeline));
        var vulkanPoolSizes = poolSizes
            .Select(VulkanFlagConverter.ToVulkan)
            .ToArray();
        return new VulkanDescriptorSet(this, vulkanPipeline.DescriptorSetLayoutHandle, vulkanPoolSizes);
    }

    public ISampler CreateSampler(
        SamplerFilter minFilter = SamplerFilter.Linear,
        SamplerFilter magFilter = SamplerFilter.Linear,
        SamplerAddressMode addressModeU = SamplerAddressMode.ClampToEdge,
        SamplerAddressMode addressModeV = SamplerAddressMode.ClampToEdge)
    {
        return new VulkanSampler(this, minFilter, magFilter, addressModeU, addressModeV);
    }

    public unsafe void CopyBuffer(IBuffer source, IBuffer destination, ulong size)
    {
        var vulkanSource = RequireOwned<VulkanBuffer>(source, nameof(source));
        var vulkanDest = RequireOwned<VulkanBuffer>(destination, nameof(destination));

        RecordCommands(cmd =>
        {
            var copyRegion = new BufferCopy { Size = size };
            Vk.CmdCopyBuffer(cmd, vulkanSource.Handle, vulkanDest.Handle, 1, &copyRegion);
        });
    }


    public unsafe void CopyTexture(ITexture2D source, ITexture2D destination)
    {
        var vulkanSource = RequireOwned<VulkanTexture2D>(source, nameof(source));
        var vulkanDest = RequireOwned<VulkanTexture2D>(destination, nameof(destination));

        // Transition source to transfer source layout
        vulkanSource.TransitionTo(ImageLayout.TransferSrcOptimal);

        // Track both layouts through the deferred recording batch.
        vulkanDest.TransitionTo(ImageLayout.TransferDstOptimal);

        RecordCommands(cmd =>
        {
            // Use blit for format conversion (RGBA8 -> BGRA8)
            var blitRegion = new ImageBlit
            {
                SrcSubresource = new ImageSubresourceLayers
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    MipLevel = 0,
                    BaseArrayLayer = 0,
                    LayerCount = 1
                },
                DstSubresource = new ImageSubresourceLayers
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    MipLevel = 0,
                    BaseArrayLayer = 0,
                    LayerCount = 1
                }
            };

            blitRegion.SrcOffsets[0] = new Offset3D(0, 0, 0);
            blitRegion.SrcOffsets[1] = new Offset3D(source.Width, source.Height, 1);
            blitRegion.DstOffsets[0] = new Offset3D(0, 0, 0);
            blitRegion.DstOffsets[1] = new Offset3D(destination.Width, destination.Height, 1);

            Vk.CmdBlitImage(
                cmd,
                vulkanSource.ImageHandle,
                ImageLayout.TransferSrcOptimal,
                vulkanDest.ImageHandle,
                ImageLayout.TransferDstOptimal,
                1,
                &blitRegion,
                Filter.Nearest);
        });

        vulkanDest.MarkContentsUnknown();

        vulkanDest.TransitionTo(ImageLayout.ColorAttachmentOptimal);

        // Transition source back to shader read optimal
        vulkanSource.TransitionTo(ImageLayout.ShaderReadOnlyOptimal);
    }

    public unsafe void CopyTextureToCubeFace(ITexture2D source, ITextureCube destination, int faceIndex)
    {
        if (faceIndex < 0 || faceIndex >= 6)
            throw new ArgumentOutOfRangeException(nameof(faceIndex), "Face index must be 0-5");

        var vulkanSource = RequireOwned<VulkanTexture2D>(source, nameof(source));
        var vulkanDest = RequireOwned<VulkanTextureCube>(destination, nameof(destination));

        // Transition source to transfer source layout
        vulkanSource.TransitionTo(ImageLayout.TransferSrcOptimal);
        vulkanDest.TransitionFaceToTransferDestination(faceIndex);

        RecordCommands(cmd =>
        {
            // Copy from 2D texture to cube face
            var copyRegion = new ImageCopy
            {
                SrcSubresource = new ImageSubresourceLayers
                {
                    AspectMask = ImageAspectFlags.DepthBit,
                    MipLevel = 0,
                    BaseArrayLayer = 0,
                    LayerCount = 1
                },
                SrcOffset = new Offset3D(0, 0, 0),
                DstSubresource = new ImageSubresourceLayers
                {
                    AspectMask = ImageAspectFlags.DepthBit,
                    MipLevel = 0,
                    BaseArrayLayer = (uint)faceIndex,
                    LayerCount = 1
                },
                DstOffset = new Offset3D(0, 0, 0),
                Extent = new Extent3D((uint)source.Width, (uint)source.Height, 1)
            };

            Vk.CmdCopyImage(
                cmd,
                vulkanSource.ImageHandle,
                ImageLayout.TransferSrcOptimal,
                vulkanDest.ImageHandle,
                ImageLayout.TransferDstOptimal,
                1,
                &copyRegion);
        });

        vulkanDest.TransitionFaceToSampled(faceIndex);

        // Transition source back to shader read optimal
        vulkanSource.TransitionTo(ImageLayout.ShaderReadOnlyOptimal);
    }

    public unsafe void CopyTextureToArrayLayer(ITexture2D source, ITextureArray destination, int layerIndex)
    {
        if (layerIndex < 0 || layerIndex >= (int)destination.ArraySize)
            throw new ArgumentOutOfRangeException(nameof(layerIndex), $"Layer index must be 0-{destination.ArraySize - 1}");

        var vulkanSource = RequireOwned<VulkanTexture2D>(source, nameof(source));
        var vulkanDest = RequireOwned<VulkanTextureArray>(destination, nameof(destination));

        // Determine aspect mask based on format
        var aspectMask = source.Format.IsDepthFormat()
            ? ImageAspectFlags.DepthBit
            : ImageAspectFlags.ColorBit;

        // Transition source to transfer source layout
        vulkanSource.TransitionTo(ImageLayout.TransferSrcOptimal);
        vulkanDest.TransitionLayerToTransferDestination((uint)layerIndex);

        RecordCommands(cmd =>
        {
            // Copy from 2D texture to array layer
            var copyRegion = new ImageCopy
            {
                SrcSubresource = new ImageSubresourceLayers
                {
                    AspectMask = aspectMask,
                    MipLevel = 0,
                    BaseArrayLayer = 0,
                    LayerCount = 1
                },
                SrcOffset = new Offset3D(0, 0, 0),
                DstSubresource = new ImageSubresourceLayers
                {
                    AspectMask = aspectMask,
                    MipLevel = 0,
                    BaseArrayLayer = (uint)layerIndex,
                    LayerCount = 1
                },
                DstOffset = new Offset3D(0, 0, 0),
                Extent = new Extent3D((uint)source.Width, (uint)source.Height, 1)
            };

            Vk.CmdCopyImage(
                cmd,
                vulkanSource.ImageHandle,
                ImageLayout.TransferSrcOptimal,
                vulkanDest.ImageHandle,
                ImageLayout.TransferDstOptimal,
                1,
                &copyRegion);
        });

        vulkanDest.TransitionLayerToSampled((uint)layerIndex);

        // Transition source back to shader read optimal
        vulkanSource.TransitionTo(ImageLayout.ShaderReadOnlyOptimal);
    }

    public unsafe void CopyTextureToCubeArrayFace(ITexture2D source, ITextureCubeArray destination, int arrayIndex, int faceIndex)
    {
        if (arrayIndex < 0 || arrayIndex >= (int)destination.ArraySize)
            throw new ArgumentOutOfRangeException(nameof(arrayIndex), $"Array index must be 0-{destination.ArraySize - 1}");
        if (faceIndex < 0 || faceIndex >= 6)
            throw new ArgumentOutOfRangeException(nameof(faceIndex), "Face index must be 0-5");

        var vulkanSource = RequireOwned<VulkanTexture2D>(source, nameof(source));
        var vulkanDest = RequireOwned<VulkanTextureCubeArray>(destination, nameof(destination));

        // Determine aspect mask based on format
        var aspectMask = source.Format.IsDepthFormat()
            ? ImageAspectFlags.DepthBit
            : ImageAspectFlags.ColorBit;

        // Calculate the layer index in the cube array (arrayIndex * 6 + faceIndex)
        uint layerIndex = (uint)(arrayIndex * 6 + faceIndex);

        // Transition source to transfer source layout
        vulkanSource.TransitionTo(ImageLayout.TransferSrcOptimal);
        vulkanDest.TransitionFaceToTransferDestination((uint)arrayIndex, faceIndex);

        RecordCommands(cmd =>
        {
            // Copy from 2D texture to cube array face
            var copyRegion = new ImageCopy
            {
                SrcSubresource = new ImageSubresourceLayers
                {
                    AspectMask = aspectMask,
                    MipLevel = 0,
                    BaseArrayLayer = 0,
                    LayerCount = 1
                },
                SrcOffset = new Offset3D(0, 0, 0),
                DstSubresource = new ImageSubresourceLayers
                {
                    AspectMask = aspectMask,
                    MipLevel = 0,
                    BaseArrayLayer = layerIndex,
                    LayerCount = 1
                },
                DstOffset = new Offset3D(0, 0, 0),
                Extent = new Extent3D((uint)source.Width, (uint)source.Height, 1)
            };

            Vk.CmdCopyImage(
                cmd,
                vulkanSource.ImageHandle,
                ImageLayout.TransferSrcOptimal,
                vulkanDest.ImageHandle,
                ImageLayout.TransferDstOptimal,
                1,
                &copyRegion);
        });

        vulkanDest.TransitionFaceToSampled((uint)arrayIndex, faceIndex);

        // Transition source back to shader read optimal
        vulkanSource.TransitionTo(ImageLayout.ShaderReadOnlyOptimal);
    }

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

    public void TransitionImageLayout(Image image, ImageLayout oldLayout, ImageLayout newLayout)
    {
        _vulkanCommandPool.TransitionImageLayout(image, oldLayout, newLayout);
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

    /// <inheritdoc cref="VulkanCommandPool.BeginRenderPassScope(object)"/>
    public void BeginRenderPassScope(object owner)
    {
        _vulkanCommandPool.BeginRenderPassScope(owner);
    }

    /// <inheritdoc cref="VulkanCommandPool.EndRenderPassScope(object)"/>
    public void EndRenderPassScope(object owner)
    {
        _vulkanCommandPool.EndRenderPassScope(owner);
    }

    public void DeferRelease(Action release)
    {
        _vulkanCommandPool.DeferRelease(release);
    }


    /// <summary>
    /// Finds a suitable memory type for the given requirements.
    /// </summary>
    public unsafe uint FindMemoryType(uint typeFilter, MemoryPropertyFlags properties)
    {
        PhysicalDeviceMemoryProperties memProps;
        Vk.GetPhysicalDeviceMemoryProperties(PhysicalDevice, &memProps);

        for (uint i = 0; i < memProps.MemoryTypeCount; i++)
        {
            if ((typeFilter & (1u << (int)i)) != 0 &&
                (memProps.MemoryTypes[(int)i].PropertyFlags & properties) == properties)
            {
                return i;
            }
        }

        throw new InvalidOperationException("Failed to find suitable memory type");
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
