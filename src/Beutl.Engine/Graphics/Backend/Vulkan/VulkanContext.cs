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
    private readonly VulkanInstance _vulkanInstance;
    private readonly VulkanDevice _vulkanDevice;
    private readonly VulkanCommandPool _vulkanCommandPool;
    private readonly object _skiaImagesLock = new();
    private readonly Dictionary<ulong, ImageCreateInfo> _skiaImages = [];
    private readonly VkCreateImageDelegate _createImage;
    private readonly VkDestroyImageDelegate _destroyImage;
    private readonly VkBindImageMemoryDelegate _bindImageMemory;
    private readonly VkCreateImageDelegate _createImageProxyDelegate;
    private readonly VkDestroyImageDelegate _destroyImageProxyDelegate;
    private readonly VkBindImageMemoryDelegate _bindImageMemoryProxyDelegate;
    private readonly IntPtr _createImageProxy;
    private readonly IntPtr _destroyImageProxy;
    private readonly IntPtr _bindImageMemoryProxy;
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
        // Ganesh creates its filter layers and scratch images through these callbacks. Vulkan
        // leaves a newly bound image undefined, and SwiftShader can expose bytes from a previously
        // freed allocation, so make initialization part of image binding instead of relying on
        // every Skia caller to happen to overwrite the complete allocation.
        _createImageProxy = Marshal.GetFunctionPointerForDelegate(_createImageProxyDelegate = CreateSkiaImage);
        _destroyImageProxy = Marshal.GetFunctionPointerForDelegate(_destroyImageProxyDelegate = DestroySkiaImage);
        _bindImageMemoryProxy = Marshal.GetFunctionPointerForDelegate(_bindImageMemoryProxyDelegate = BindSkiaImageMemory);

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

    private IntPtr GetVulkanProcAddress(string name, IntPtr instance, IntPtr device)
    {
        if (name == "vkCreateImage")
            return _createImageProxy;
        if (name == "vkDestroyImage")
            return _destroyImageProxy;
        if (name == "vkBindImageMemory")
            return _bindImageMemoryProxy;

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
    {
        IntPtr address = _vulkanInstance.Vk.GetDeviceProcAddr(_vulkanDevice.Device, name);
        if (address == IntPtr.Zero)
            throw new InvalidOperationException($"Vulkan device function '{name}' is unavailable.");
        return Marshal.GetDelegateForFunctionPointer<T>(address);
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
        _vulkanCommandPool.SubmitImmediateCommands(commandBuffer =>
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

    public GraphicsBackend Backend => GraphicsBackend.Vulkan;

    public GRContext SkiaContext => _skiaContext ?? throw new InvalidOperationException(
        "SkiaSharp Vulkan context is not initialized. Make sure the Vulkan context was created successfully.");

    public Vk Vk => _vulkanInstance.Vk;

    public Instance Instance => _vulkanInstance.Instance;

    public PhysicalDevice PhysicalDevice => _vulkanDevice.PhysicalDevice;

    public Device Device => _vulkanDevice.Device;

    public Queue GraphicsQueue => _vulkanDevice.GraphicsQueue;

    public uint GraphicsQueueFamilyIndex => _vulkanDevice.GraphicsQueueFamilyIndex;

    public IEnumerable<string> EnabledExtensions =>
        _vulkanInstance.EnabledExtensions.Concat(_vulkanDevice.EnabledExtensions);

    public bool Supports3DRendering => true;

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
        return new VulkanTexture2D(this, width, height, format, usage);
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

    public IRenderPass3D CreateRenderPass3D(
        IReadOnlyList<TextureFormat> colorFormats,
        TextureFormat depthFormat = TextureFormat.Depth32Float,
        AttachmentLoadOp colorLoadOp = AttachmentLoadOp.Clear,
        AttachmentLoadOp depthLoadOp = AttachmentLoadOp.Clear)
    {
        var vulkanColorFormats = colorFormats.Select(f => f.ToVulkanFormat()).ToList();
        return new VulkanRenderPass3D(this, vulkanColorFormats, depthFormat.ToVulkanFormat(), colorLoadOp, depthLoadOp);
    }

    public IFramebuffer3D CreateFramebuffer3D(IRenderPass3D renderPass, IReadOnlyList<ITexture2D> colorTextures, ITexture2D depthTexture)
    {
        var vulkanRenderPass = (VulkanRenderPass3D)renderPass;
        var vulkanColorTextures = colorTextures.Cast<VulkanTexture2D>().ToList();
        var vulkanDepthTexture = (VulkanTexture2D)depthTexture;
        return new VulkanFramebuffer3D(this, vulkanRenderPass.Handle, vulkanColorTextures, vulkanDepthTexture);
    }

    public IPipeline3D CreatePipeline3D(
        IRenderPass3D renderPass,
        byte[] vertexShaderSpirv,
        byte[] fragmentShaderSpirv,
        DescriptorBinding[] descriptorBindings,
        VertexInputDescription vertexInput,
        PipelineOptions? options = null)
    {
        var vulkanRenderPass = (VulkanRenderPass3D)renderPass;
        var vulkanBindings = descriptorBindings
            .Select(VulkanFlagConverter.ToVulkan)
            .ToArray();
        var vulkanVertexInput = VulkanFlagConverter.ToVulkan(vertexInput);
        var pipelineOptions = options ?? PipelineOptions.Default;

        return new VulkanPipeline3D(
            this,
            vulkanRenderPass.Handle,
            vertexShaderSpirv,
            fragmentShaderSpirv,
            vulkanVertexInput,
            vulkanBindings,
            vulkanRenderPass.ColorAttachmentCount,
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

    public IDescriptorSet CreateDescriptorSet(IPipeline3D pipeline, DescriptorPoolSize[] poolSizes)
    {
        var vulkanPipeline = (VulkanPipeline3D)pipeline;
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
        var vulkanSource = (VulkanBuffer)source;
        var vulkanDest = (VulkanBuffer)destination;

        SubmitImmediateCommands(cmd =>
        {
            var copyRegion = new BufferCopy { Size = size };
            Vk.CmdCopyBuffer(cmd, vulkanSource.Handle, vulkanDest.Handle, 1, &copyRegion);
        });
    }


    public unsafe void CopyTexture(ITexture2D source, ITexture2D destination)
    {
        var vulkanSource = (VulkanTexture2D)source;
        var vulkanDest = (VulkanTexture2D)destination;

        // Transition source to transfer source layout
        vulkanSource.TransitionTo(ImageLayout.TransferSrcOptimal);

        // Transition destination to transfer destination
        SubmitImmediateCommands(cmd =>
        {
            // Transition destination to transfer destination
            var barrier = new ImageMemoryBarrier
            {
                SType = StructureType.ImageMemoryBarrier,
                OldLayout = ImageLayout.Undefined,
                NewLayout = ImageLayout.TransferDstOptimal,
                SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                Image = vulkanDest.ImageHandle,
                SubresourceRange = new ImageSubresourceRange
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    BaseMipLevel = 0,
                    LevelCount = 1,
                    BaseArrayLayer = 0,
                    LayerCount = 1
                },
                SrcAccessMask = 0,
                DstAccessMask = AccessFlags.TransferWriteBit
            };

            Vk.CmdPipelineBarrier(
                cmd,
                PipelineStageFlags.TopOfPipeBit,
                PipelineStageFlags.TransferBit,
                0,
                0, null,
                0, null,
                1, &barrier);

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

            // Transition destination back to color attachment optimal
            barrier.OldLayout = ImageLayout.TransferDstOptimal;
            barrier.NewLayout = ImageLayout.ColorAttachmentOptimal;
            barrier.SrcAccessMask = AccessFlags.TransferWriteBit;
            barrier.DstAccessMask = AccessFlags.ColorAttachmentReadBit | AccessFlags.ColorAttachmentWriteBit;

            Vk.CmdPipelineBarrier(
                cmd,
                PipelineStageFlags.TransferBit,
                PipelineStageFlags.ColorAttachmentOutputBit,
                0,
                0, null,
                0, null,
                1, &barrier);
        });

        // Transition source back to shader read optimal
        vulkanSource.TransitionTo(ImageLayout.ShaderReadOnlyOptimal);
    }

    public unsafe void CopyTextureToCubeFace(ITexture2D source, ITextureCube destination, int faceIndex)
    {
        if (faceIndex < 0 || faceIndex >= 6)
            throw new ArgumentOutOfRangeException(nameof(faceIndex), "Face index must be 0-5");

        var vulkanSource = (VulkanTexture2D)source;
        var vulkanDest = (VulkanTextureCube)destination;

        // Transition source to transfer source layout
        vulkanSource.TransitionTo(ImageLayout.TransferSrcOptimal);

        SubmitImmediateCommands(cmd =>
        {
            // Transition cube face to transfer destination
            var barrier = new ImageMemoryBarrier
            {
                SType = StructureType.ImageMemoryBarrier,
                OldLayout = ImageLayout.Undefined,
                NewLayout = ImageLayout.TransferDstOptimal,
                SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                Image = vulkanDest.ImageHandle,
                SubresourceRange = new ImageSubresourceRange
                {
                    AspectMask = ImageAspectFlags.DepthBit,
                    BaseMipLevel = 0,
                    LevelCount = 1,
                    BaseArrayLayer = (uint)faceIndex,
                    LayerCount = 1
                },
                SrcAccessMask = 0,
                DstAccessMask = AccessFlags.TransferWriteBit
            };

            Vk.CmdPipelineBarrier(
                cmd,
                PipelineStageFlags.TopOfPipeBit,
                PipelineStageFlags.TransferBit,
                0,
                0, null,
                0, null,
                1, &barrier);

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

            // Transition cube face to shader read optimal
            barrier.OldLayout = ImageLayout.TransferDstOptimal;
            barrier.NewLayout = ImageLayout.ShaderReadOnlyOptimal;
            barrier.SrcAccessMask = AccessFlags.TransferWriteBit;
            barrier.DstAccessMask = AccessFlags.ShaderReadBit;

            Vk.CmdPipelineBarrier(
                cmd,
                PipelineStageFlags.TransferBit,
                PipelineStageFlags.FragmentShaderBit,
                0,
                0, null,
                0, null,
                1, &barrier);
        });

        // Transition source back to shader read optimal
        vulkanSource.TransitionTo(ImageLayout.ShaderReadOnlyOptimal);
    }

    public unsafe void CopyTextureToArrayLayer(ITexture2D source, ITextureArray destination, int layerIndex)
    {
        if (layerIndex < 0 || layerIndex >= (int)destination.ArraySize)
            throw new ArgumentOutOfRangeException(nameof(layerIndex), $"Layer index must be 0-{destination.ArraySize - 1}");

        var vulkanSource = (VulkanTexture2D)source;
        var vulkanDest = (VulkanTextureArray)destination;

        // Determine aspect mask based on format
        var aspectMask = source.Format.IsDepthFormat()
            ? ImageAspectFlags.DepthBit
            : ImageAspectFlags.ColorBit;

        // Transition source to transfer source layout
        vulkanSource.TransitionTo(ImageLayout.TransferSrcOptimal);

        SubmitImmediateCommands(cmd =>
        {
            // Transition array layer to transfer destination
            var barrier = new ImageMemoryBarrier
            {
                SType = StructureType.ImageMemoryBarrier,
                OldLayout = ImageLayout.Undefined,
                NewLayout = ImageLayout.TransferDstOptimal,
                SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                Image = vulkanDest.ImageHandle,
                SubresourceRange = new ImageSubresourceRange
                {
                    AspectMask = aspectMask,
                    BaseMipLevel = 0,
                    LevelCount = 1,
                    BaseArrayLayer = (uint)layerIndex,
                    LayerCount = 1
                },
                SrcAccessMask = 0,
                DstAccessMask = AccessFlags.TransferWriteBit
            };

            Vk.CmdPipelineBarrier(
                cmd,
                PipelineStageFlags.TopOfPipeBit,
                PipelineStageFlags.TransferBit,
                0,
                0, null,
                0, null,
                1, &barrier);

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

            // Transition array layer to shader read optimal
            barrier.OldLayout = ImageLayout.TransferDstOptimal;
            barrier.NewLayout = ImageLayout.ShaderReadOnlyOptimal;
            barrier.SrcAccessMask = AccessFlags.TransferWriteBit;
            barrier.DstAccessMask = AccessFlags.ShaderReadBit;

            Vk.CmdPipelineBarrier(
                cmd,
                PipelineStageFlags.TransferBit,
                PipelineStageFlags.FragmentShaderBit,
                0,
                0, null,
                0, null,
                1, &barrier);
        });

        // Transition source back to shader read optimal
        vulkanSource.TransitionTo(ImageLayout.ShaderReadOnlyOptimal);
    }

    public unsafe void CopyTextureToCubeArrayFace(ITexture2D source, ITextureCubeArray destination, int arrayIndex, int faceIndex)
    {
        if (arrayIndex < 0 || arrayIndex >= (int)destination.ArraySize)
            throw new ArgumentOutOfRangeException(nameof(arrayIndex), $"Array index must be 0-{destination.ArraySize - 1}");
        if (faceIndex < 0 || faceIndex >= 6)
            throw new ArgumentOutOfRangeException(nameof(faceIndex), "Face index must be 0-5");

        var vulkanSource = (VulkanTexture2D)source;
        var vulkanDest = (VulkanTextureCubeArray)destination;

        // Determine aspect mask based on format
        var aspectMask = source.Format.IsDepthFormat()
            ? ImageAspectFlags.DepthBit
            : ImageAspectFlags.ColorBit;

        // Calculate the layer index in the cube array (arrayIndex * 6 + faceIndex)
        uint layerIndex = (uint)(arrayIndex * 6 + faceIndex);

        // Transition source to transfer source layout
        vulkanSource.TransitionTo(ImageLayout.TransferSrcOptimal);

        SubmitImmediateCommands(cmd =>
        {
            // Transition cube array face to transfer destination
            var barrier = new ImageMemoryBarrier
            {
                SType = StructureType.ImageMemoryBarrier,
                OldLayout = ImageLayout.Undefined,
                NewLayout = ImageLayout.TransferDstOptimal,
                SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                Image = vulkanDest.ImageHandle,
                SubresourceRange = new ImageSubresourceRange
                {
                    AspectMask = aspectMask,
                    BaseMipLevel = 0,
                    LevelCount = 1,
                    BaseArrayLayer = layerIndex,
                    LayerCount = 1
                },
                SrcAccessMask = 0,
                DstAccessMask = AccessFlags.TransferWriteBit
            };

            Vk.CmdPipelineBarrier(
                cmd,
                PipelineStageFlags.TopOfPipeBit,
                PipelineStageFlags.TransferBit,
                0,
                0, null,
                0, null,
                1, &barrier);

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

            // Transition cube array face to shader read optimal
            barrier.OldLayout = ImageLayout.TransferDstOptimal;
            barrier.NewLayout = ImageLayout.ShaderReadOnlyOptimal;
            barrier.SrcAccessMask = AccessFlags.TransferWriteBit;
            barrier.DstAccessMask = AccessFlags.ShaderReadBit;

            Vk.CmdPipelineBarrier(
                cmd,
                PipelineStageFlags.TransferBit,
                PipelineStageFlags.FragmentShaderBit,
                0,
                0, null,
                0, null,
                1, &barrier);
        });

        // Transition source back to shader read optimal
        vulkanSource.TransitionTo(ImageLayout.ShaderReadOnlyOptimal);
    }

    public void WaitIdle()
    {
        _vulkanDevice.WaitIdle();
    }

    public void SubmitImmediateCommands(Action<CommandBuffer> record)
    {
        _vulkanCommandPool.SubmitImmediateCommands(record);
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

    public CommandBuffer AllocateCommandBuffer()
    {
        return _vulkanCommandPool.AllocateCommandBuffer();
    }

    public void SubmitCommandBuffer(CommandBuffer commandBuffer)
    {
        _vulkanCommandPool.SubmitCommandBuffer(commandBuffer);
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

        _vulkanDevice.WaitIdle();

        _skiaContext?.Dispose();
        _skiaContext = null;
        _skiaBackendContext?.Dispose();
        _skiaBackendContext = null;

        _vulkanCommandPool.Dispose();
        _vulkanDevice.Dispose();
    }
}
