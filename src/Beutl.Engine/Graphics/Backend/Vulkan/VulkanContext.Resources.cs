using System.Collections.Immutable;
using Silk.NET.Vulkan;

namespace Beutl.Graphics.Backend.Vulkan;

using Image = Silk.NET.Vulkan.Image;

internal sealed unsafe partial class VulkanContext
{
    public ITexture2D CreateTexture2D(int width, int height, TextureFormat format)
    {
        // The driver does not refuse an extent it cannot make: SwiftShader answers success past its
        // framebuffer limit, MoltenVK aborts the process. The usage below carries an attachment bit, which
        // makes the framebuffer limits apply at creation whether the texture is ever attached or not.
        ThrowIfCannotMakeAttachableImage(MaxImageDimension2D, width, height);

        ImageUsageFlags usage;
        if (format.IsDepthFormat())
        {
            usage = ImageUsageFlags.DepthStencilAttachmentBit | ImageUsageFlags.SampledBit |
                    ImageUsageFlags.TransferSrcBit | ImageUsageFlags.TransferDstBit;
        }
        else
        {
            usage = VulkanTexture2D.ColorTextureUsage;
        }
        var texture = new VulkanTexture2D(this, width, height, format, usage);
        RecordTextureAllocation(format);
        return texture;
    }

    public ITextureCube CreateTextureCube(int size, TextureFormat format)
    {
        ThrowIfCannotMakeAttachableImage(MaxCubeFaceDimension, size, size);

        var usage = GetLayeredTextureUsage(format);
        return new VulkanTextureCube(this, size, format, usage);
    }

    public ITextureArray CreateTextureArray(int width, int height, uint arraySize, TextureFormat format)
    {
        ThrowIfCannotMakeAttachableImage(MaxImageDimension2D, width, height);

        var usage = GetLayeredTextureUsage(format);
        return new VulkanTextureArray(this, width, height, arraySize, format, usage);
    }

    public ITextureCubeArray CreateTextureCubeArray(int size, uint arraySize, TextureFormat format)
    {
        ThrowIfCannotMakeAttachableImage(MaxCubeFaceDimension, size, size);

        var usage = GetLayeredTextureUsage(format);
        return new VulkanTextureCubeArray(this, size, arraySize, format, usage);
    }

    /// <summary>
    /// The usage of a cube, array or cube-array texture. A layered depth texture, unlike a 2D one, is not a copy
    /// source.
    /// </summary>
    private static ImageUsageFlags GetLayeredTextureUsage(TextureFormat format)
    {
        return format.IsDepthFormat()
            ? ImageUsageFlags.DepthStencilAttachmentBit | ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit
            : VulkanTexture2D.ColorTextureUsage;
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

        // A texture is bounded by the image limit when it is made, because it may only ever be sampled.
        // Attaching it is what the framebuffer limits govern, and the driver does not enforce those either:
        // SwiftShader builds a framebuffer past its own limit and answers success. The two framebuffer
        // limits may differ, so each axis is measured against its own rather than against the square
        // budget the render-target paths fit their density into.
        foreach (ITexture2D texture in colorTextures)
            DeviceExtentLimits.ThrowIfCannotBuildFramebuffer(
                MaxFramebufferWidth, MaxFramebufferHeight, texture.Width, texture.Height);
        if (depthTexture is not null)
            DeviceExtentLimits.ThrowIfCannotBuildFramebuffer(
                MaxFramebufferWidth, MaxFramebufferHeight, depthTexture.Width, depthTexture.Height);

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
            VulkanPipelineFixedFunctionState.From(pipelineOptions));
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

    public IDescriptorSet CreateDescriptorSet(IPipeline3D pipeline, DescriptorPoolSize[] poolSizes)
    {
        var vulkanPipeline = RequireOwned<VulkanPipeline3D>(pipeline, nameof(pipeline));
        var vulkanPoolSizes = poolSizes
            .Select(VulkanFlagConverter.ToVulkan)
            .ToArray();
        return new VulkanDescriptorSet(
            this,
            vulkanPipeline.DescriptorSetLayoutHandle,
            vulkanPipeline.DescriptorBindings,
            vulkanPoolSizes);
    }

    public ISampler CreateSampler(
        SamplerFilter minFilter = SamplerFilter.Linear,
        SamplerFilter magFilter = SamplerFilter.Linear,
        SamplerAddressMode addressModeU = SamplerAddressMode.ClampToEdge,
        SamplerAddressMode addressModeV = SamplerAddressMode.ClampToEdge)
    {
        return new VulkanSampler(this, minFilter, magFilter, addressModeU, addressModeV);
    }

    /// <summary>
    /// Finds a suitable memory type for the given requirements.
    /// </summary>
    public uint FindMemoryType(uint typeFilter, MemoryPropertyFlags properties)
        => VulkanPhysicalDeviceQueries.FindMemoryType(Vk, PhysicalDevice, typeFilter, properties);

    /// <summary>
    /// Creates the image behind a texture: 2D, one mip level, optimal tiling and exclusive sharing, starting
    /// in <see cref="ImageLayout.Undefined"/>.
    /// </summary>
    /// <param name="resourceName">
    /// Names the image in the thrown message, e.g. <c>"cube map image"</c>.
    /// </param>
    internal unsafe Image CreateTextureImage(
        TextureFormat format,
        int width,
        int height,
        uint arrayLayers,
        ImageUsageFlags usage,
        ImageCreateFlags flags,
        void* pNext,
        string resourceName)
    {
        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            PNext = pNext,
            Flags = flags,
            ImageType = ImageType.Type2D,
            Format = format.ToVulkanFormat(),
            Extent = new Extent3D((uint)width, (uint)height, 1),
            MipLevels = 1,
            ArrayLayers = arrayLayers,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = usage,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined
        };

        Image image;
        Result result = Vk.CreateImage(Device, &imageInfo, null, &image);
        if (result != Result.Success)
        {
            throw new InvalidOperationException($"Failed to create Vulkan {resourceName}: {result}");
        }

        return image;
    }

    /// <summary>
    /// Allocates device-local memory sized for <paramref name="image"/> and binds it. On failure the image
    /// is destroyed and nothing stays allocated, so the caller never has to unwind a partial binding.
    /// </summary>
    /// <param name="resourceName">
    /// Names the image in the thrown message, e.g. <c>"cube map image"</c>.
    /// </param>
    internal unsafe DeviceMemory AllocateAndBindImageMemory(
        Image image,
        string resourceName,
        out ulong allocationSize)
    {
        var vk = Vk;
        var device = Device;

        MemoryRequirements memReqs;
        vk.GetImageMemoryRequirements(device, image, &memReqs);
        allocationSize = memReqs.Size;

        var allocInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memReqs.Size,
            MemoryTypeIndex = FindMemoryType(memReqs.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit)
        };

        DeviceMemory memory;
        Result result = vk.AllocateMemory(device, &allocInfo, null, &memory);
        if (result != Result.Success)
        {
            vk.DestroyImage(device, image, null);
            throw new InvalidOperationException($"Failed to allocate Vulkan {resourceName} memory: {result}");
        }

        result = vk.BindImageMemory(device, image, memory, 0);
        if (result != Result.Success)
        {
            vk.DestroyImage(device, image, null);
            vk.FreeMemory(device, memory, null);
            throw new InvalidOperationException($"Failed to bind {resourceName} memory: {result}");
        }

        return memory;
    }

    /// <summary>
    /// Creates a view of exactly one array layer as a plain 2D image - the whole of a single-layer
    /// texture, and the shape a framebuffer attachment needs onto an array slice or a cube face.
    /// </summary>
    internal Result TryCreateSingleLayerView(
        Image image,
        TextureFormat format,
        uint arrayLayer,
        out ImageView view)
        => TryCreateImageView(image, format, ImageViewType.Type2D, arrayLayer, 1, out view);

    /// <summary>
    /// Creates a view of <paramref name="layerCount"/> array layers of <paramref name="image"/>, starting at
    /// <paramref name="baseArrayLayer"/>.
    /// </summary>
    /// <remarks>
    /// Returns the result instead of throwing because every caller has its own partially built views to
    /// release before it can report the failure.
    /// </remarks>
    internal unsafe Result TryCreateImageView(
        Image image,
        TextureFormat format,
        ImageViewType viewType,
        uint baseArrayLayer,
        uint layerCount,
        out ImageView view)
    {
        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = image,
            ViewType = viewType,
            Format = format.ToVulkanFormat(),
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = format.GetAspectMask(),
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = baseArrayLayer,
                LayerCount = layerCount
            }
        };

        ImageView created;
        Result result = Vk.CreateImageView(Device, &viewInfo, null, &created);
        view = created;
        return result;
    }

    /// <summary>
    /// Stages <paramref name="data"/> and records its copy into one array layer of the destination.
    /// </summary>
    /// <remarks>
    /// The caller owns the transitions: the layer must be in <see cref="ImageLayout.TransferDstOptimal"/>
    /// when the recorded batch runs.
    /// </remarks>
    internal unsafe void UploadToImageLayer(
        ReadOnlySpan<byte> data,
        Image destinationImage,
        ImageAspectFlags aspectMask,
        uint destinationArrayLayer,
        uint width,
        uint height)
    {
        using var stagingBuffer = new VulkanBuffer(
            this,
            (ulong)data.Length,
            BufferUsage.TransferSource,
            MemoryProperty.HostVisible | MemoryProperty.HostCoherent);

        stagingBuffer.Upload(data);

        RecordCommands(cmd =>
        {
            var region = new BufferImageCopy
            {
                BufferOffset = 0,
                BufferRowLength = 0,
                BufferImageHeight = 0,
                ImageSubresource = new ImageSubresourceLayers
                {
                    AspectMask = aspectMask,
                    MipLevel = 0,
                    BaseArrayLayer = destinationArrayLayer,
                    LayerCount = 1
                },
                ImageOffset = new Offset3D(0, 0, 0),
                ImageExtent = new Extent3D(width, height, 1)
            };

            // ReSharper disable once AccessToDisposedClosure
            Vk.CmdCopyBufferToImage(
                cmd, stagingBuffer.Handle, destinationImage, ImageLayout.TransferDstOptimal, 1, &region);
        });
    }
}
