using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Vulkan;
using Beutl.Media;
using Silk.NET.Vulkan;
using DescriptorType = Beutl.Graphics.Backend.DescriptorType;

namespace Beutl.Graphics.Shaders;

internal delegate ProgramCacheLease<ShaderGraphPipeline> ShaderGraphProgramAcquirer(
    VulkanContext context, ShaderPassDescription pass);

internal sealed unsafe class ShaderGraphPipeline : IDisposable
{
    private readonly VulkanContext _context;
    private readonly Dictionary<ShaderStage, ShaderInterface> _interfaces = [];
    private readonly Dictionary<int, VulkanSampler> _samplers = [];
    private readonly PhysicalDeviceLimits _limits;
    private IRenderPass3D? _renderPass;
    private VulkanPipeline3D? _graphics;
    private VulkanComputePipeline? _compute;
    private bool _disposed;

    public long RetainedByteSize { get; private set; }

    public static ProgramCache<ShaderGraphPipeline> CreateCache()
        => new(static program => program.RetainedByteSize, 16 * 1024 * 1024);

    public static ProgramCacheLease<ShaderGraphPipeline> Acquire(ProgramCache<ShaderGraphPipeline> cache,
        ProgramCacheContextKey key, VulkanContext context, ShaderPassDescription pass)
        => cache.GetOrCreate(ShaderGraphDescription.GetPipelineIdentity(pass), key, (context, pass),
            static state => new ShaderGraphPipeline(state.context, state.pass));

    public ShaderGraphPipeline(VulkanContext context, ShaderPassDescription pass)
    {
        _context = context;
        context.Vk.GetPhysicalDeviceProperties(context.PhysicalDevice, out PhysicalDeviceProperties properties);
        _limits = properties.Limits;
        var stages = new Dictionary<ShaderStage, byte[]>();
        try
        {
            using var compiler = new VulkanShaderCompiler();
            foreach ((ShaderStage stage, ShaderSource source) in pass.Program.Stages)
            {
                byte[] bytes = compiler.CompileToSpirv(source.Text, stage);
                stages.Add(stage, bytes);
                RetainedByteSize = checked(RetainedByteSize + bytes.Length);
                var shaderInterface = new ShaderInterface(bytes, stage);
                _interfaces.Add(stage, shaderInterface);
                foreach (uint capability in shaderInterface.Capabilities)
                {
                    // Extended storage formats are checked individually through their format features.
                    bool supported = capability is 0 or 1 or 49 or 50 or 51
                        || capability == 10 && context.SupportsShaderFloat64
                        || capability == 11 && context.SupportsShaderInt64;
                    if (!supported)
                        throw new NotSupportedException($"Pass '{pass.Name}' requires unsupported SPIR-V capability {capability}.");
                }
            }
            Validate(pass);
            ShaderStage visibility = pass.Program.IsCompute ? ShaderStage.Compute : ShaderStage.Vertex | ShaderStage.Fragment;
            DescriptorBinding[] bindings = pass.Bindings
                .Select(b => new DescriptorBinding((uint)b.Binding, b.Type, 1, visibility)).ToArray();
            if (pass.Program.IsCompute)
            {
                uint count = 0;
                context.Vk.GetPhysicalDeviceQueueFamilyProperties(context.PhysicalDevice, &count, null);
                var families = new QueueFamilyProperties[count];
                fixed (QueueFamilyProperties* data = families)
                    context.Vk.GetPhysicalDeviceQueueFamilyProperties(context.PhysicalDevice, &count, data);
                if ((families[context.GraphicsQueueFamilyIndex].QueueFlags & QueueFlags.ComputeBit) == 0)
                    throw new NotSupportedException("The rendering queue does not support compute dispatch.");
                _compute = new VulkanComputePipeline(context, stages[ShaderStage.Compute], bindings);
            }
            else
            {
                _renderPass = context.CreateRenderPass3D(
                    pass.Attachments.Select(a => a.Image.Format).ToArray(), null, pass.Attachments[0].Load);
                _graphics = (VulkanPipeline3D)context.CreatePipeline3D(_renderPass,
                    stages[ShaderStage.Vertex], stages[ShaderStage.Fragment], bindings,
                    VertexInputDescription.Empty, pass.Blend.ToPipelineOptions());
            }
            foreach (ShaderPassBinding binding in pass.Bindings)
            {
                if (binding.Type != DescriptorType.CombinedImageSampler)
                    continue;
                SamplerState sampler = binding.Sampler;
                _samplers.Add(binding.Binding, new VulkanSampler(context, sampler.MinFilter, sampler.MagFilter,
                    sampler.AddressU, sampler.AddressV, transparentBorder: true));
            }
        }
        catch (Exception ex)
        {
            Dispose();
            throw new InvalidOperationException($"Shader pass '{pass.Name}' could not be compiled: {ex.Message}", ex);
        }
    }

    public void Validate(ShaderPassDescription pass)
    {
        var writableBindings = _interfaces.Values.SelectMany(shader => shader.Bindings)
            .Where(binding => !binding.ReadOnly).Select(binding => binding.Slot).ToHashSet();
        foreach ((ShaderStage stage, ShaderInterface shaderInterface) in _interfaces)
        {
            shaderInterface.Validate(pass, stage, writableBindings);
            bool storesSupported = stage switch
            {
                ShaderStage.Vertex => _context.SupportsVertexStores,
                ShaderStage.Fragment => _context.SupportsFragmentStores,
                _ => true,
            };
            if (!storesSupported && shaderInterface.Bindings.Any(binding => !binding.ReadOnly))
                throw new NotSupportedException($"Pass '{pass.Name}': this device does not support storage writes in the {stage} stage.");
        }
        if (!pass.Program.IsCompute)
            _interfaces[ShaderStage.Fragment].ValidateInputs(_interfaces[ShaderStage.Vertex], pass.Name);
        if (pass.Attachments.Length > _limits.MaxColorAttachments)
            throw new NotSupportedException($"Pass '{pass.Name}' exceeds maxColorAttachments ({_limits.MaxColorAttachments}).");
        foreach (IGrouping<DescriptorType, ShaderPassBinding> group in pass.Bindings.GroupBy(b => b.Type))
        {
            uint limit = group.Key switch
            {
                DescriptorType.CombinedImageSampler => Math.Min(
                    Math.Min(_limits.MaxPerStageDescriptorSamplers, _limits.MaxPerStageDescriptorSampledImages),
                    Math.Min(_limits.MaxDescriptorSetSamplers, _limits.MaxDescriptorSetSampledImages)),
                DescriptorType.StorageBuffer => Math.Min(_limits.MaxPerStageDescriptorStorageBuffers, _limits.MaxDescriptorSetStorageBuffers),
                DescriptorType.StorageImage => Math.Min(_limits.MaxPerStageDescriptorStorageImages, _limits.MaxDescriptorSetStorageImages),
                DescriptorType.UniformBuffer => Math.Min(_limits.MaxPerStageDescriptorUniformBuffers, _limits.MaxDescriptorSetUniformBuffers),
                _ => 0,
            };
            if (group.Count() > limit)
                throw new NotSupportedException($"Pass '{pass.Name}' exceeds the {group.Key} descriptor limit ({limit}).");
        }
        if (pass.Bindings.Length > _limits.MaxPerStageResources)
            throw new NotSupportedException($"Pass '{pass.Name}' exceeds maxPerStageResources.");
        foreach (ShaderPassBinding binding in pass.Bindings)
        {
            if (binding.Resource is ShaderBuffer buffer)
            {
                uint limit = binding.Type == DescriptorType.UniformBuffer ? _limits.MaxUniformBufferRange : _limits.MaxStorageBufferRange;
                if (buffer.ByteLength > limit)
                    throw new NotSupportedException($"Pass '{pass.Name}', binding {binding.Binding}: buffer range exceeds {limit} bytes.");
            }
            else if (!binding.Metadata && binding.Resource is ShaderImage image)
            {
                FormatFeatureFlags flags = binding.Type == DescriptorType.StorageImage
                    ? FormatFeatureFlags.StorageImageBit : FormatFeatureFlags.SampledImageBit;
                if (binding.Type == DescriptorType.CombinedImageSampler
                    && (binding.Sampler.MinFilter == SamplerFilter.Linear || binding.Sampler.MagFilter == SamplerFilter.Linear))
                    flags |= FormatFeatureFlags.SampledImageFilterLinearBit;
                ValidateFormat(image.Format, flags, pass.Name);
            }
        }
        foreach (ShaderColorAttachment attachment in pass.Attachments)
            ValidateFormat(attachment.Image.Format, FormatFeatureFlags.ColorAttachmentBit
                | (pass.Blend.Enabled ? FormatFeatureFlags.ColorAttachmentBlendBit : 0), pass.Name);
        if (pass.Program.IsCompute)
        {
            ShaderInterface shader = _interfaces[ShaderStage.Compute];
            (uint x, uint y, uint z) = shader.LocalSize;
            PhysicalDeviceLimits limits = _limits;
            if (x == 0 || y == 0 || z == 0
                || x > limits.MaxComputeWorkGroupSize[0] || y > limits.MaxComputeWorkGroupSize[1]
                || z > limits.MaxComputeWorkGroupSize[2] || (ulong)x * y * z > limits.MaxComputeWorkGroupInvocations
                || shader.SharedMemorySize > limits.MaxComputeSharedMemorySize)
                throw new NotSupportedException($"Pass '{pass.Name}' exceeds compute local-size or shared-memory limits.");
            (uint gx, uint gy, uint gz) = ResolveGroups(pass);
            if (gx > limits.MaxComputeWorkGroupCount[0] || gy > limits.MaxComputeWorkGroupCount[1] || gz > limits.MaxComputeWorkGroupCount[2])
                throw new NotSupportedException($"Pass '{pass.Name}' exceeds maxComputeWorkGroupCount.");
        }
    }

    private void ValidateFormat(TextureFormat format, FormatFeatureFlags required, string pass)
    {
        _context.Vk.GetPhysicalDeviceFormatProperties(_context.PhysicalDevice, format.ToVulkanFormat(), out FormatProperties properties);
        if ((properties.OptimalTilingFeatures & required) != required)
            throw new NotSupportedException($"Pass '{pass}': format {format} does not support {required}.");
    }

    private (uint X, uint Y, uint Z) ResolveGroups(ShaderPassDescription pass)
    {
        var size = _interfaces[ShaderStage.Compute].LocalSize;
        return pass.DispatchThreads
            ? (RoundUp(pass.Work.X, size.X), RoundUp(pass.Work.Y, size.Y), RoundUp(pass.Work.Z, size.Z))
            : ((uint)pass.Work.X, (uint)pass.Work.Y, (uint)pass.Work.Z);

        static uint RoundUp(int value, uint divisor) => checked((uint)(((ulong)value + divisor - 1) / divisor));
    }

    public void Execute(ShaderPassDescription pass, IReadOnlyDictionary<int, ITexture2D> images,
        IReadOnlyDictionary<int, IBuffer> buffers, IReadOnlyDictionary<int, ShaderImageFrame> frames)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Validate(pass); // Runtime parameter sizes and buffer ranges are deliberately absent from the pipeline key.
        var pools = pass.Bindings.GroupBy(b => b.Type)
            .Select(g => new Silk.NET.Vulkan.DescriptorPoolSize(VulkanFlagConverter.ToVulkan(g.Key), (uint)g.Count())).ToArray();
        using var descriptors = new VulkanDescriptorSet(_context,
            _compute?.DescriptorLayout ?? _graphics!.DescriptorSetLayoutHandle,
            _compute?.Bindings ?? _graphics!.DescriptorBindings, pools);
        var parameterBuffers = new List<IBuffer>();
        try
        {
            // One image can be bound both as a sampler and as storage. Every descriptor for it must
            // name the same layout for the entire pass; transitioning in binding order would invalidate
            // whichever descriptor was written first.
            var layouts = new Dictionary<int, ImageLayout>();
            foreach (var group in pass.Bindings.Where(b => !b.Metadata && b.Resource is ShaderImage)
                         .GroupBy(b => b.Resource.Id))
            {
                ImageLayout layout = group.Any(b => b.Type == DescriptorType.StorageImage)
                    ? ImageLayout.General : ImageLayout.ShaderReadOnlyOptimal;
                VulkanTexture2D image = _context.RequireOwned<VulkanTexture2D>(images[group.Key], nameof(images));
                if (group.Any(b => (b.Access & ResourceAccess.Write) != 0))
                    image.MarkContentsUnknown();
                image.TransitionTo(layout);
                layouts.Add(group.Key, layout);
            }
            foreach (ShaderPassBinding binding in pass.Bindings)
            {
                switch (binding.Type)
                {
                    case DescriptorType.CombinedImageSampler:
                        descriptors.UpdateTexture(binding.Binding, images[binding.Resource.Id],
                            _samplers[binding.Binding], layouts[binding.Resource.Id]);
                        break;
                    case DescriptorType.StorageImage:
                        descriptors.UpdateStorageImage(binding.Binding, images[binding.Resource.Id]);
                        break;
                    case DescriptorType.StorageBuffer:
                        descriptors.UpdateStorageBuffer(binding.Binding, buffers[binding.Resource.Id]);
                        break;
                    case DescriptorType.UniformBuffer:
                        if (binding.Metadata)
                        {
                            IBuffer parameters = _context.CreateBuffer(48, BufferUsage.UniformBuffer,
                                MemoryProperty.HostVisible | MemoryProperty.HostCoherent);
                            parameterBuffers.Add(parameters);
                            System.Numerics.Vector4[] values = frames[binding.Resource.Id].GetParameters();
                            parameters.Upload<System.Numerics.Vector4>(values);
                            descriptors.UpdateBuffer(binding.Binding, parameters);
                        }
                        else
                            descriptors.UpdateBuffer(binding.Binding, buffers[binding.Resource.Id]);
                        break;
                }
            }
            SynchronizeResources(_context);
            if (_compute is { } compute)
            {
                CommandBuffer command = _context.GetRecordingCommandBuffer();
                _context.Vk.CmdBindPipeline(command, PipelineBindPoint.Compute, compute.Handle);
                DescriptorSet set = descriptors.Handle;
                _context.Vk.CmdBindDescriptorSets(command, PipelineBindPoint.Compute, compute.Layout, 0, 1, &set, 0, null);
                SetConstants(command, compute.Layout, ShaderStageFlags.ComputeBit, pass);
                (uint x, uint y, uint z) = ResolveGroups(pass);
                _context.Vk.CmdDispatch(command, x, y, z);
            }
            else
            {
                ITexture2D[] targets = pass.Attachments.Select(a => images[a.Image.Id]).ToArray();
                if (targets.Any(t => t.Width != targets[0].Width || t.Height != targets[0].Height))
                    throw new InvalidOperationException($"Pass '{pass.Name}' requires matching attachment dimensions.");
                foreach (ITexture2D target in targets)
                    target.PrepareForRender();
                using IFramebuffer3D framebuffer = _context.CreateFramebuffer3D(_renderPass!, targets, null);
                _renderPass!.Begin(framebuffer, new Color[targets.Length]);
                try
                {
                    _renderPass.BindPipeline(_graphics!);
                    _renderPass.BindDescriptorSet(_graphics!, descriptors);
                    SetConstants(_context.GetRecordingCommandBuffer(), _graphics!.PipelineLayoutHandle,
                        VulkanPipeline3D.PushConstantStages, pass);
                    _renderPass.Draw((uint)pass.Work.X, (uint)pass.Work.Y);
                }
                finally
                {
                    _renderPass.End();
                }
            }
        }
        finally
        {
            foreach (IBuffer buffer in parameterBuffers)
                buffer.Dispose();
        }
    }

    private void SetConstants(CommandBuffer command, PipelineLayout layout, ShaderStageFlags stages, ShaderPassDescription pass)
    {
        if (pass.Constants.IsEmpty)
            return;
        fixed (byte* bytes = pass.Constants.AsSpan())
            _context.Vk.CmdPushConstants(command, layout, stages, 0, (uint)pass.Constants.Length, bytes);
    }

    internal static void SynchronizeResources(VulkanContext context)
    {
        context.ThrowIfRenderPassActive();
        var barrier = new MemoryBarrier
        {
            SType = StructureType.MemoryBarrier,
            SrcAccessMask = AccessFlags.MemoryReadBit | AccessFlags.MemoryWriteBit,
            DstAccessMask = AccessFlags.MemoryReadBit | AccessFlags.MemoryWriteBit,
        };
        context.Vk.CmdPipelineBarrier(context.GetRecordingCommandBuffer(), PipelineStageFlags.AllCommandsBit,
            PipelineStageFlags.AllCommandsBit, 0, 1, &barrier, 0, null, 0, null);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _graphics?.Dispose();
        _compute?.Dispose();
        _renderPass?.Dispose();
        foreach (VulkanSampler sampler in _samplers.Values)
            sampler.Dispose();
    }
}
