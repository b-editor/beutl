using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Vulkan;
using Beutl.Media;
using Silk.NET.Vulkan;
using AttachmentLoadOp = Beutl.Graphics.Backend.AttachmentLoadOp;
using DescriptorType = Beutl.Graphics.Backend.DescriptorType;
using PrimitiveTopology = Beutl.Graphics.Backend.PrimitiveTopology;

namespace Beutl.Graphics.Shaders;

internal delegate ProgramCacheLease<ShaderGraphPipeline> ShaderGraphProgramAcquirer(
    VulkanContext context, ShaderPassDescription pass, ShaderProgramIdentity identity);

internal sealed unsafe class ShaderGraphPipeline : IDisposable
{
    private readonly VulkanContext _context;
    private readonly Dictionary<ShaderStage, ShaderInterface> _interfaces = [];
    private readonly Dictionary<int, VulkanSampler> _samplers = [];
    private readonly PhysicalDeviceLimits _limits;
    private readonly Silk.NET.Vulkan.DescriptorPoolSize[] _pools;
    private VulkanRenderPass3D? _renderPass;
    private VulkanPipeline3D? _graphics;
    private VulkanComputePipeline? _compute;
    private bool _disposed;

    public long RetainedByteSize { get; private set; }

    public static ProgramCache<ShaderGraphPipeline> CreateCache()
        => new(static program => program.RetainedByteSize, 16 * 1024 * 1024);

    /// <summary>Leases the pipeline for <paramref name="pass"/> under the identity its graph computed once.</summary>
    public static ProgramCacheLease<ShaderGraphPipeline> Acquire(ProgramCache<ShaderGraphPipeline> cache,
        ProgramCacheContextKey key, VulkanContext context, ShaderPassDescription pass, ShaderProgramIdentity identity)
        => cache.GetOrCreate(identity, key, (context, pass),
            static state => new ShaderGraphPipeline(state.context, state.pass));

    public ShaderGraphPipeline(VulkanContext context, ShaderPassDescription pass)
    {
        _context = context;
        _limits = context.Limits;
        // Binding types are part of the pipeline identity, so every pass leasing this pipeline needs these pools.
        _pools = pass.Bindings.GroupBy(b => b.Type)
            .Select(g => new Silk.NET.Vulkan.DescriptorPoolSize(VulkanFlagConverter.ToVulkan(g.Key), (uint)g.Count())).ToArray();
        var stages = new Dictionary<ShaderStage, byte[]>();
        try
        {
            using var compiler = new VulkanShaderCompiler();
            foreach ((ShaderStage stage, ShaderSource source) in pass.Program!.Stages)
            {
                byte[] bytes = compiler.CompileToSpirv(source.Text, stage);
                stages.Add(stage, bytes);
                RetainedByteSize = checked(RetainedByteSize + bytes.Length);
                var shaderInterface = new ShaderInterface(bytes, stage, pass.Program.SpecializationConstants);
                _interfaces.Add(stage, shaderInterface);
                foreach (uint capability in shaderInterface.Capabilities)
                {
                    // Extended storage formats are checked individually through their format features.
                    bool supported = capability is 0 or 1 or 50 or 51
                        || capability == 49 && context.EnabledFeatures.ShaderStorageImageExtendedFormats
                        || capability == 2 && context.EnabledFeatures.GeometryShader
                        || capability == 3 && context.EnabledFeatures.TessellationShader
                        || capability is 23 or 24 && context.EnabledFeatures.ShaderTessellationAndGeometryPointSize
                        || capability is 34 or 45 && context.SupportsImageCubeArray
                        || capability == 10 && context.SupportsShaderFloat64
                        || capability == 11 && context.SupportsShaderInt64;
                    if (!supported)
                        throw new NotSupportedException($"Pass '{pass.Name}' requires unsupported SPIR-V capability {capability}.");
                }
            }
            Validate(pass);
            ShaderStage visibility = pass.Program!.Stages.Keys.Aggregate(ShaderStage.None, (all, stage) => all | stage);
            DescriptorBinding[] bindings = pass.Bindings
                .Select(b => new DescriptorBinding((uint)b.Binding, b.Type, 1, visibility)).ToArray();
            if (pass.Program!.IsCompute)
            {
                if (!context.GraphicsQueueSupportsCompute)
                    throw new NotSupportedException("The rendering queue does not support compute dispatch.");
                _compute = new VulkanComputePipeline(context, stages[ShaderStage.Compute], bindings, pass.Program.SpecializationConstants);
            }
            else
            {
                _renderPass = (VulkanRenderPass3D)context.CreateRenderPass3D(
                    pass.Attachments.Select(a => a.Image.Format).ToArray(), pass.DepthAttachment?.Image.Format,
                    pass.Attachments.FirstOrDefault()?.Load ?? AttachmentLoadOp.Clear,
                    pass.DepthAttachment?.Load ?? AttachmentLoadOp.Clear);
                PipelineOptions options = pass.Blend.ToPipelineOptions();
                options.Topology = pass.Draw.Topology;
                options.DepthTestEnabled = pass.DepthStencil.TestEnabled;
                options.DepthWriteEnabled = pass.DepthStencil.WriteEnabled;
                options.DepthCompare = pass.DepthStencil.Compare;
                options.StencilEnabled = pass.DepthStencil.StencilEnabled;
                options.StencilFront = pass.DepthStencil.Front;
                options.StencilBack = pass.DepthStencil.Back;
                options.CullMode = pass.CullMode;
                options.FrontFace = pass.FrontFace;
                _graphics = new VulkanPipeline3D(context, _renderPass.Handle, stages,
                    VulkanFlagConverter.ToVulkan(pass.Draw.GetVertexInput()), bindings.Select(VulkanFlagConverter.ToVulkan).ToArray(),
                    pass.Program.SpecializationConstants, pass.Attachments.Length, pass.DepthAttachment is not null, VulkanPipelineFixedFunctionState.From(options),
                    (uint)pass.PatchControlPoints);
            }
            foreach (ShaderPassBinding binding in pass.Bindings)
            {
                if (binding.Type != DescriptorType.CombinedImageSampler)
                    continue;
                SamplerState sampler = binding.Sampler;
                _samplers.Add(binding.Binding, new VulkanSampler(context, sampler.MinFilter, sampler.MagFilter,
                    sampler.AddressU, sampler.AddressV, transparentBorder: true, sampler.AddressW,
                    sampler.MipmapFilter, sampler.MinLod, sampler.MaxLod, sampler.Compare,
                    ((ShaderImage)binding.Resource).Format.GetNumericType() != NumericType.Float));
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
        ShaderInterface.ValidateProgram(_interfaces, pass);
        foreach ((ShaderStage stage, ShaderInterface shaderInterface) in _interfaces)
        {
            bool storesSupported = stage switch
            {
                ShaderStage.Vertex or ShaderStage.Geometry or ShaderStage.TessellationControl or ShaderStage.TessellationEvaluation
                    => _context.EnabledFeatures.VertexPipelineStoresAndAtomics,
                ShaderStage.Fragment => _context.EnabledFeatures.FragmentStoresAndAtomics,
                _ => true,
            };
            if (!storesSupported && shaderInterface.Bindings.Any(binding => !binding.ReadOnly))
                throw new NotSupportedException($"Pass '{pass.Name}': this device does not support storage writes in the {stage} stage.");
        }
        if (!pass.Program!.IsCompute)
        {
            ValidateGraphicsStages(pass);
            ValidateDraw(pass);
        }
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
                if (binding.Type == DescriptorType.CombinedImageSampler && !binding.Sampler.Compare.HasValue
                    && (binding.Sampler.MinFilter == SamplerFilter.Linear || binding.Sampler.MagFilter == SamplerFilter.Linear
                        || binding.Sampler.MipmapFilter == SamplerFilter.Linear))
                    flags |= FormatFeatureFlags.SampledImageFilterLinearBit;
                ValidateFormat(image.Format, flags, pass.Name);
            }
        }
        foreach (ShaderColorAttachment attachment in pass.Attachments)
            ValidateFormat(attachment.Image.Format, FormatFeatureFlags.ColorAttachmentBit
                | (pass.Blend.Enabled ? FormatFeatureFlags.ColorAttachmentBlendBit : 0), pass.Name);
        if (pass.DepthAttachment is { } depth)
            ValidateFormat(depth.Image.Format, FormatFeatureFlags.DepthStencilAttachmentBit, pass.Name);
        if (pass.Program!.IsCompute)
        {
            ShaderInterface shader = _interfaces[ShaderStage.Compute];
            (uint x, uint y, uint z) = shader.LocalSize;
            PhysicalDeviceLimits limits = _limits;
            if (x == 0 || y == 0 || z == 0
                || x > limits.MaxComputeWorkGroupSize[0] || y > limits.MaxComputeWorkGroupSize[1]
                || z > limits.MaxComputeWorkGroupSize[2] || (ulong)x * y * z > limits.MaxComputeWorkGroupInvocations
                || shader.SharedMemorySize > limits.MaxComputeSharedMemorySize)
                throw new NotSupportedException($"Pass '{pass.Name}' exceeds compute local-size or shared-memory limits.");
            (uint gx, uint gy, uint gz) = pass.Draw.Indirect is null ? ResolveGroups(pass) : (0u, 0u, 0u);
            if (gx > limits.MaxComputeWorkGroupCount[0] || gy > limits.MaxComputeWorkGroupCount[1] || gz > limits.MaxComputeWorkGroupCount[2])
                throw new NotSupportedException($"Pass '{pass.Name}' exceeds maxComputeWorkGroupCount.");
        }
    }

    private void ValidateGraphicsStages(ShaderPassDescription pass)
    {
        bool tessellation = _interfaces.TryGetValue(ShaderStage.TessellationControl, out ShaderInterface? control);
        if (tessellation != (pass.Draw.Topology == PrimitiveTopology.PatchList))
            throw new ArgumentException($"Pass '{pass.Name}': tessellation stages require patch topology, and patches require tessellation stages.");
        if (tessellation && (pass.PatchControlPoints > _limits.MaxTessellationPatchSize
                || control!.ExecutionModes.GetValueOrDefault(26u) > _limits.MaxTessellationPatchSize))
            throw new NotSupportedException($"Pass '{pass.Name}' exceeds the tessellation patch size limit.");
        if (_interfaces.TryGetValue(ShaderStage.Geometry, out ShaderInterface? geometry))
        {
            uint primitive = pass.Draw.Topology switch
            {
                PrimitiveTopology.PointList => 19,
                PrimitiveTopology.LineList or PrimitiveTopology.LineStrip => 20,
                PrimitiveTopology.LineListAdjacency or PrimitiveTopology.LineStripAdjacency => 21,
                PrimitiveTopology.TriangleListAdjacency or PrimitiveTopology.TriangleStripAdjacency => 23,
                _ => 22,
            };
            if (tessellation)
            {
                var evaluation = _interfaces[ShaderStage.TessellationEvaluation].ExecutionModes;
                primitive = evaluation.ContainsKey(10) ? 19u : evaluation.ContainsKey(25) ? 20u : 22u;
            }
            if (!geometry.ExecutionModes.ContainsKey(primitive))
                throw new ArgumentException($"Pass '{pass.Name}': geometry input primitive does not match the preceding stage or topology.");
            if (geometry.ExecutionModes.GetValueOrDefault(0u, 1u) > _limits.MaxGeometryShaderInvocations
                || geometry.ExecutionModes.GetValueOrDefault(26u) > _limits.MaxGeometryOutputVertices)
                throw new NotSupportedException($"Pass '{pass.Name}' exceeds geometry invocation or output vertex limits.");
        }
        else if (pass.Draw.Topology is PrimitiveTopology.LineListAdjacency or PrimitiveTopology.LineStripAdjacency
                 or PrimitiveTopology.TriangleListAdjacency or PrimitiveTopology.TriangleStripAdjacency)
            throw new ArgumentException($"Pass '{pass.Name}': adjacency topology requires a geometry stage.");
    }

    private void ValidateDraw(ShaderPassDescription pass)
    {
        ShaderDrawDescription draw = pass.Draw;
        draw.Validate(pass.Name, pass.Work.X, pass.Work.Y);
        if (draw.Indirect is { } indirect
            && (indirect.Count > _limits.MaxDrawIndirectCount || indirect.Count > 1 && !_context.EnabledFeatures.MultiDrawIndirect))
            throw new NotSupportedException($"Pass '{pass.Name}' exceeds the enabled indirect draw count limit.");
        if (draw.Topology == PrimitiveTopology.TriangleFan && !_context.SupportsTriangleFans)
            throw new NotSupportedException($"Pass '{pass.Name}': this device does not support triangle fans.");
        if (draw.Bindings.Length > _limits.MaxVertexInputBindings
            || draw.Bindings.Any(b => b.Binding >= _limits.MaxVertexInputBindings || b.Stride > _limits.MaxVertexInputBindingStride)
            || draw.Vertices.Any(v => v.Binding >= _limits.MaxVertexInputBindings))
            throw new NotSupportedException($"Pass '{pass.Name}' exceeds vertex binding or stride limits.");
        if (draw.Attributes.Length > _limits.MaxVertexInputAttributes
            || draw.Attributes.Any(a => a.Location >= _limits.MaxVertexInputAttributes || a.Offset > _limits.MaxVertexInputAttributeOffset))
            throw new NotSupportedException($"Pass '{pass.Name}' exceeds vertex attribute location or offset limits.");
        uint alignment = _context.VertexStrideAlignment;
        if (alignment > 0 && draw.Bindings.Any(b => b.Stride < alignment || b.Stride % alignment != 0))
            throw new NotSupportedException($"Pass '{pass.Name}': this device requires vertex strides in positive multiples of {alignment} bytes.");
        foreach (VertexAttributeDescription attribute in draw.Attributes)
        {
            _context.Vk.GetPhysicalDeviceFormatProperties(_context.PhysicalDevice,
                VulkanFlagConverter.ToVulkan(attribute.Format), out FormatProperties properties);
            if ((properties.BufferFeatures & FormatFeatureFlags.VertexBufferBit) == 0)
                throw new NotSupportedException($"Pass '{pass.Name}': format {attribute.Format} does not support vertex input.");
            if (!_context.SupportsVertexAttributeBeyondStride
                && (ulong)attribute.Offset + (uint)ShaderDrawDescription.AttributeSize(attribute.Format)
                    > draw.Bindings.First(b => b.Binding == attribute.Binding).Stride)
                throw new NotSupportedException($"Pass '{pass.Name}': this device requires vertex attributes to fit within the binding stride.");
        }
    }

    private void ValidateFormat(TextureFormat format, FormatFeatureFlags required, string pass)
    {
        if (!SupportsOptimalTiling(_context, format, required))
            throw new NotSupportedException($"Pass '{pass}': format {format} does not support {required}.");
    }

    /// <summary>Whether optimally tiled images of <paramref name="format"/> have every <paramref name="required"/> feature.</summary>
    internal static bool SupportsOptimalTiling(VulkanContext context, TextureFormat format, FormatFeatureFlags required)
    {
        context.Vk.GetPhysicalDeviceFormatProperties(context.PhysicalDevice, format.ToVulkanFormat(), out FormatProperties properties);
        return (properties.OptimalTilingFeatures & required) == required;
    }

    private (uint X, uint Y, uint Z) ResolveGroups(ShaderPassDescription pass)
    {
        var size = _interfaces[ShaderStage.Compute].LocalSize;
        return pass.DispatchThreads
            ? (RoundUp(pass.Work.X, size.X), RoundUp(pass.Work.Y, size.Y), RoundUp(pass.Work.Z, size.Z))
            : ((uint)pass.Work.X, (uint)pass.Work.Y, (uint)pass.Work.Z);

        static uint RoundUp(int value, uint divisor) => checked((uint)(((ulong)value + divisor - 1) / divisor));
    }

    /// <remarks>The caller validates <paramref name="pass"/> first: runtime parameter sizes and buffer ranges are
    /// deliberately absent from the pipeline key, so a cached pipeline has only checked the pass it was created for.</remarks>
    public void Execute(ShaderPassDescription pass, IReadOnlyDictionary<int, ITexture2D> images,
        IReadOnlyDictionary<int, IBuffer> buffers, IReadOnlyDictionary<int, ShaderImageFrame> frames, Beutl.Media.PixelRect? scissor = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var descriptors = new VulkanDescriptorSet(_context,
            _compute?.DescriptorLayout ?? _graphics!.DescriptorSetLayoutHandle,
            _compute?.Bindings ?? _graphics!.DescriptorBindings, _pools);
        var parameterBuffers = new List<IBuffer>();
        try
        {
            // One image can be bound both as a sampler and as storage. Every descriptor for it must
            // name the same layout for the entire pass; transitioning in binding order would invalidate
            // whichever descriptor was written first.
            var layouts = new Dictionary<int, ImageLayout>();
            foreach (var group in pass.Bindings.Where(b => !b.Metadata && b.Resource is ShaderImage)
                         .GroupBy(b => ((ShaderImage)b.Resource).Root.Id))
            {
                ImageLayout layout = group.Any(b => b.Type == DescriptorType.StorageImage)
                    ? ImageLayout.General : ImageLayout.ShaderReadOnlyOptimal;
                foreach (ShaderPassBinding binding in group)
                {
                    VulkanTexture2D image = _context.RequireOwned<VulkanTexture2D>(images[binding.Resource.Id], nameof(images));
                    if ((binding.Access & ResourceAccess.Write) != 0) image.MarkContentsUnknown();
                    image.TransitionTo(layout);
                    layouts[binding.Resource.Id] = layout;
                }
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
                            IBuffer parameters = _context.CreateBuffer(ShaderImageFrame.ParameterSize, BufferUsage.UniformBuffer,
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
                if (!pass.Constants.IsEmpty)
                {
                    Span<byte> constants = stackalloc byte[pass.Constants.Length];
                    WriteConstants(constants, pass, frames);
                    fixed (byte* bytes = constants)
                        _context.Vk.CmdPushConstants(command, compute.Layout, ShaderStageFlags.ComputeBit, 0, (uint)constants.Length, bytes);
                }
                if (pass.Draw.Indirect is { } indirect)
                    _context.Vk.CmdDispatchIndirect(command,
                        _context.RequireOwned<VulkanBuffer>(buffers[indirect.Buffer.Id], nameof(buffers)).Handle, (ulong)indirect.Offset);
                else
                {
                    (uint x, uint y, uint z) = ResolveGroups(pass);
                    _context.Vk.CmdDispatch(command, x, y, z);
                }
            }
            else
            {
                ITexture2D[] targets = pass.Attachments.Select(a => images[a.Image.Id]).ToArray();
                if (targets.Any(t => t.Width != targets[0].Width || t.Height != targets[0].Height))
                    throw new InvalidOperationException($"Pass '{pass.Name}' requires matching attachment dimensions.");
                ITexture2D? depthTarget = pass.DepthAttachment is { } depth ? images[depth.Image.Id] : null;
                using IFramebuffer3D framebuffer = _context.CreateFramebuffer3D(_renderPass!, targets, depthTarget);
                _renderPass!.Begin(framebuffer, new Color[targets.Length], pass.DepthAttachment?.ClearDepth ?? 1,
                    pass.DepthAttachment?.ClearStencil ?? 0);
                try
                {
                    _renderPass.BindPipeline(_graphics!);
                    _renderPass.BindDescriptorSet(_graphics!, descriptors);
                    if (scissor is { } area)
                        _renderPass.SetScissor(area);
                    if (!pass.Constants.IsEmpty)
                    {
                        Span<byte> constants = stackalloc byte[pass.Constants.Length];
                        WriteConstants(constants, pass, frames);
                        _renderPass.SetPushConstants(constants);
                    }
                    foreach (ShaderVertexBuffer vertex in pass.Draw.Vertices)
                        _renderPass.BindVertexBuffer((uint)vertex.Binding, buffers[vertex.Buffer.Id], (ulong)vertex.Offset);
                    if (pass.Draw.Indices is { } indices)
                    {
                        _renderPass.BindIndexBuffer(buffers[indices.Buffer.Id],
                            indices.Format == IndexFormat.UInt16 ? IndexType.Uint16 : IndexType.Uint32, (ulong)indices.Offset);
                    }
                    if (pass.Draw.Indirect is { } indirect)
                        _renderPass.DrawIndirect(buffers[indirect.Buffer.Id], (ulong)indirect.Offset, (uint)indirect.Count,
                            (uint)indirect.Stride, pass.Draw.Indices is not null);
                    else if (pass.Draw.Indices is not null)
                    {
                        _renderPass.DrawIndexed((uint)pass.Work.X, (uint)pass.Work.Y, (uint)pass.Draw.FirstElement,
                            pass.Draw.VertexOffset, (uint)pass.Draw.FirstInstance);
                    }
                    else
                        _renderPass.Draw((uint)pass.Work.X, (uint)pass.Work.Y,
                            (uint)pass.Draw.FirstElement, (uint)pass.Draw.FirstInstance);
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

    private static void WriteConstants(Span<byte> constants, ShaderPassDescription pass, IReadOnlyDictionary<int, ShaderImageFrame> frames)
    {
        pass.Constants.AsSpan().CopyTo(constants);
        foreach (ShaderRuntimeConstant constant in pass.RuntimeConstants)
            constant.Write(constants, frames);
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
