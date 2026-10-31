using System;
using System.Collections.Generic;
using Beutl.Media;
using Silk.NET.Vulkan;

namespace Beutl.Graphics.Backend.Vulkan;

internal sealed unsafe class VulkanRenderPass3D : IRenderPass3D, IVulkanContextResource, IVulkanRenderPassSuspension
{
    private readonly VulkanContext _context;
    private readonly RenderPass _renderPass;
    private readonly Format[] _colorFormats;
    private readonly Format? _depthFormat;
    private readonly int _colorAttachmentCount;
    private readonly byte[] _pushConstantData = new byte[VulkanPipeline3D.MaxPushConstantsSize];
    private CommandBuffer _currentCommandBuffer;
    private VulkanPipeline3D? _currentPipeline;
    private VulkanFramebuffer3D? _currentFramebuffer;
    private RenderPass _resumeRenderPass;
    private readonly Dictionary<uint, (Silk.NET.Vulkan.Buffer Buffer, ulong Offset)> _boundVertexBuffers = [];
    private Silk.NET.Vulkan.Buffer _boundIndexBuffer;
    private ulong _boundIndexOffset;
    private IndexType _boundIndexType;
    private DescriptorSet _boundDescriptorSet;
    private PipelineLayout _boundDescriptorSetPipelineLayout;
    private PipelineLayout _pushConstantLayout;
    private uint _pushConstantSize;
    private Rect2D? _scissor;
    private bool _inRenderPass;
    private bool _suspended;
    private bool _disposed;

    public VulkanContext OwnerContext => _context;

    /// <summary>
    /// Creates a render pass with the specified color formats and optional depth format.
    /// </summary>
    public VulkanRenderPass3D(
        VulkanContext context,
        IReadOnlyList<Format> colorFormats,
        Format? depthFormat,
        AttachmentLoadOp colorLoadOp = AttachmentLoadOp.Clear,
        AttachmentLoadOp depthLoadOp = AttachmentLoadOp.Clear)
    {
        if (colorFormats.Count == 0 && !depthFormat.HasValue)
        {
            throw new ArgumentException("At least one attachment format is required", nameof(colorFormats));
        }

        _context = context;
        _colorFormats = [.. colorFormats];
        _depthFormat = depthFormat;
        _colorAttachmentCount = colorFormats.Count;

        _renderPass = CreateRenderPass(
            context,
            _colorFormats,
            depthFormat,
            ToVulkanLoadOp(colorLoadOp),
            ToVulkanLoadOp(depthLoadOp));
    }

    /// <summary>
    /// The pass instance this one resumes as after being suspended: the same attachments, loading what the
    /// suspended half stored instead of clearing over it.
    /// </summary>
    /// <remarks>
    /// Render pass compatibility ignores load and store ops, so a pipeline built for this pass stays valid
    /// across the split and nothing has to be rebuilt to cross it.
    /// </remarks>
    private RenderPass GetOrCreateResumeRenderPass()
    {
        if (_resumeRenderPass.Handle != 0)
            return _resumeRenderPass;

        _resumeRenderPass = CreateRenderPass(
            _context,
            _colorFormats,
            _depthFormat,
            Silk.NET.Vulkan.AttachmentLoadOp.Load,
            Silk.NET.Vulkan.AttachmentLoadOp.Load);
        return _resumeRenderPass;
    }

    private static RenderPass CreateRenderPass(
        VulkanContext context,
        IReadOnlyList<Format> colorFormats,
        Format? depthFormat,
        Silk.NET.Vulkan.AttachmentLoadOp colorLoadOp,
        Silk.NET.Vulkan.AttachmentLoadOp depthLoadOp)
    {
        var vk = context.Vk;
        var device = context.Device;
        int totalAttachments = colorFormats.Count + (depthFormat.HasValue ? 1 : 0);
        var attachments = stackalloc AttachmentDescription[totalAttachments];
        var colorAttachmentRefs = stackalloc AttachmentReference[colorFormats.Count];

        for (int i = 0; i < colorFormats.Count; i++)
        {
            attachments[i] = new AttachmentDescription
            {
                Format = colorFormats[i],
                Samples = SampleCountFlags.Count1Bit,
                LoadOp = colorLoadOp,
                StoreOp = AttachmentStoreOp.Store,
                StencilLoadOp = Silk.NET.Vulkan.AttachmentLoadOp.DontCare,
                StencilStoreOp = AttachmentStoreOp.DontCare,
                InitialLayout = ImageLayout.ColorAttachmentOptimal,
                FinalLayout = ImageLayout.ColorAttachmentOptimal
            };

            colorAttachmentRefs[i] = new AttachmentReference
            {
                Attachment = (uint)i,
                Layout = ImageLayout.ColorAttachmentOptimal
            };
        }

        var depthAttachmentRef = default(AttachmentReference);
        if (depthFormat is Format actualDepthFormat)
        {
            attachments[colorFormats.Count] = new AttachmentDescription
            {
                Format = actualDepthFormat,
                Samples = SampleCountFlags.Count1Bit,
                LoadOp = depthLoadOp,
                StoreOp = AttachmentStoreOp.Store,
                StencilLoadOp = depthLoadOp,
                StencilStoreOp = AttachmentStoreOp.Store,
                InitialLayout = ImageLayout.DepthStencilAttachmentOptimal,
                FinalLayout = ImageLayout.DepthStencilAttachmentOptimal
            };

            depthAttachmentRef = new AttachmentReference
            {
                Attachment = (uint)colorFormats.Count,
                Layout = ImageLayout.DepthStencilAttachmentOptimal
            };
        }

        var subpass = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = (uint)colorFormats.Count,
            PColorAttachments = colorAttachmentRefs,
            PDepthStencilAttachment = depthFormat.HasValue ? &depthAttachmentRef : null
        };

        PipelineStageFlags attachmentStages = PipelineStageFlags.ColorAttachmentOutputBit;
        AccessFlags attachmentReads = AccessFlags.ColorAttachmentReadBit;
        AccessFlags attachmentWrites = AccessFlags.ColorAttachmentWriteBit;
        if (depthFormat.HasValue)
        {
            attachmentStages |= PipelineStageFlags.EarlyFragmentTestsBit | PipelineStageFlags.LateFragmentTestsBit;
            attachmentReads |= AccessFlags.DepthStencilAttachmentReadBit;
            attachmentWrites |= AccessFlags.DepthStencilAttachmentWriteBit;
        }

        // Keep this dependency identical for the initial and LOAD-op variants so they remain compatible,
        // while making attachment writes visible when a suspended pass resumes and reads them.
        var dependency = new SubpassDependency
        {
            SrcSubpass = Vk.SubpassExternal,
            DstSubpass = 0,
            SrcStageMask = attachmentStages,
            SrcAccessMask = attachmentWrites,
            DstStageMask = attachmentStages,
            DstAccessMask = attachmentReads | attachmentWrites,
            DependencyFlags = DependencyFlags.ByRegionBit
        };

        var renderPassInfo = new RenderPassCreateInfo
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = (uint)totalAttachments,
            PAttachments = attachments,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 1,
            PDependencies = &dependency
        };

        RenderPass created;
        Result result = vk.CreateRenderPass(device, &renderPassInfo, null, &created);
        if (result != Result.Success)
        {
            throw new InvalidOperationException($"Failed to create render pass: {result}");
        }

        return created;
    }

    public RenderPass Handle => _renderPass;

    public int ColorAttachmentCount => _colorAttachmentCount;

    public IReadOnlyList<Format> ColorFormats => _colorFormats;

    public Format? DepthFormat => _depthFormat;

    public bool HasDepthAttachment => _depthFormat.HasValue;

    public void Begin(IFramebuffer3D framebuffer, ReadOnlySpan<Color> clearColors, float clearDepth = 1.0f)
        => Begin(framebuffer, clearColors, clearDepth, 0);

    internal void Begin(IFramebuffer3D framebuffer, ReadOnlySpan<Color> clearColors, float clearDepth, uint clearStencil)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_inRenderPass)
        {
            throw new InvalidOperationException("Render pass already begun");
        }

        var vulkanFramebuffer = _context.RequireOwned<VulkanFramebuffer3D>(framebuffer, nameof(framebuffer));
        if (!vulkanFramebuffer.IsCompatibleWith(this))
        {
            throw new ArgumentException("The framebuffer was created for a different render pass.", nameof(framebuffer));
        }

        // Rejected before the first barrier, but the batch is not claimed until the command that opens the
        // pass: a claimed scope sends every barrier through the suspend path, and the preparation below
        // runs before there is an instance to suspend.
        _context.ThrowIfRenderPassActive();

        vulkanFramebuffer.PrepareForRendering();

        // Barriers, copies, and consecutive render passes share one command buffer until an
        // external consumer requires submission.
        _currentCommandBuffer = _context.GetRecordingCommandBuffer();

        int totalClearValues = _colorAttachmentCount + (HasDepthAttachment ? 1 : 0);
        var clearValues = stackalloc ClearValue[totalClearValues];

        for (int i = 0; i < _colorAttachmentCount; i++)
        {
            if (i < clearColors.Length)
            {
                var color = clearColors[i].ToLinearPremultiplied();
                clearValues[i].Color = new ClearColorValue(color.X, color.Y, color.Z, color.W);
            }
            else
            {
                clearValues[i].Color = new ClearColorValue(0, 0, 0, 0);
            }
        }
        if (HasDepthAttachment)
        {
            clearValues[_colorAttachmentCount].DepthStencil = new ClearDepthStencilValue(clearDepth, clearStencil);
        }

        var renderPassBeginInfo = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _renderPass,
            Framebuffer = vulkanFramebuffer.Handle,
            RenderArea = FullFramebufferArea(vulkanFramebuffer),
            ClearValueCount = (uint)totalClearValues,
            PClearValues = clearValues
        };

        ForgetRecordedState();
        _context.BeginRenderPassScope(this);
        _context.Vk.CmdBeginRenderPass(_currentCommandBuffer, &renderPassBeginInfo, SubpassContents.Inline);
        SetFullFramebufferViewport(vulkanFramebuffer);

        _currentFramebuffer = vulkanFramebuffer;
        _inRenderPass = true;
    }

    private void SetFullFramebufferViewport(VulkanFramebuffer3D framebuffer)
    {
        var viewport = new Viewport
        {
            X = 0,
            Y = 0,
            Width = framebuffer.Width,
            Height = framebuffer.Height,
            MinDepth = 0,
            MaxDepth = 1
        };
        _context.Vk.CmdSetViewport(_currentCommandBuffer, 0, 1, &viewport);

        var scissor = FullFramebufferArea(framebuffer);
        _context.Vk.CmdSetScissor(_currentCommandBuffer, 0, 1, &scissor);
    }

    private static Rect2D FullFramebufferArea(VulkanFramebuffer3D framebuffer)
        => new()
        {
            Offset = new Offset2D(0, 0),
            Extent = new Extent2D((uint)framebuffer.Width, (uint)framebuffer.Height)
        };

    bool IVulkanRenderPassSuspension.TrySuspend()
    {
        if (!_inRenderPass || _suspended)
            return false;

        _context.Vk.CmdEndRenderPass(_currentCommandBuffer);
        _suspended = true;
        return true;
    }

    void IVulkanRenderPassSuspension.Resume()
    {
        VulkanFramebuffer3D framebuffer = _currentFramebuffer!;

        // The batch this pass was recording into may have been submitted while it was suspended - that is
        // what a synchronous flush does - so the instance resumes on whatever batch is recording now.
        _currentCommandBuffer = _context.GetRecordingCommandBuffer();

        var renderPassBeginInfo = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = GetOrCreateResumeRenderPass(),
            Framebuffer = framebuffer.Handle,
            RenderArea = FullFramebufferArea(framebuffer),
            ClearValueCount = 0,
            PClearValues = null
        };

        _context.Vk.CmdBeginRenderPass(_currentCommandBuffer, &renderPassBeginInfo, SubpassContents.Inline);
        SetFullFramebufferViewport(framebuffer);
        _suspended = false;
        RebindRecordedState();
    }

    /// <summary>
    /// Puts back everything a draw reads that lives on the command buffer rather than in an object.
    /// </summary>
    /// <remarks>
    /// A synchronous flush during a suspension resumes the instance on a new command buffer, so this replays the
    /// bindings recorded since Begin, pipeline first and push constants last, as the spec requires.
    /// </remarks>
    private void RebindRecordedState()
    {
        Vk vk = _context.Vk;

        if (_currentPipeline is { } pipeline)
        {
            vk.CmdBindPipeline(_currentCommandBuffer, PipelineBindPoint.Graphics, pipeline.Handle);
        }

        if (_boundDescriptorSet.Handle != 0)
        {
            DescriptorSet set = _boundDescriptorSet;
            vk.CmdBindDescriptorSets(
                _currentCommandBuffer,
                PipelineBindPoint.Graphics,
                _boundDescriptorSetPipelineLayout,
                0,
                1,
                &set,
                0,
                null);
        }

        foreach ((uint binding, var value) in _boundVertexBuffers)
        {
            Silk.NET.Vulkan.Buffer vertexBuffer = value.Buffer;
            ulong offset = value.Offset;
            vk.CmdBindVertexBuffers(_currentCommandBuffer, binding, 1, &vertexBuffer, &offset);
        }

        if (_boundIndexBuffer.Handle != 0)
        {
            vk.CmdBindIndexBuffer(_currentCommandBuffer, _boundIndexBuffer, _boundIndexOffset, _boundIndexType);
        }

        // Resume set the full framebuffer area; a narrower scissor recorded since Begin replaces it.
        if (_scissor is { } scissor)
        {
            vk.CmdSetScissor(_currentCommandBuffer, 0, 1, &scissor);
        }

        if (_pushConstantSize != 0)
        {
            fixed (byte* data = _pushConstantData)
            {
                vk.CmdPushConstants(
                    _currentCommandBuffer,
                    _pushConstantLayout,
                    VulkanPipeline3D.PushConstantStages,
                    0,
                    _pushConstantSize,
                    data);
            }
        }
    }

    /// <summary>Drops the bindings recorded for an instance that is no longer recording.</summary>
    private void ForgetRecordedState()
    {
        _currentPipeline = null;
        _boundVertexBuffers.Clear();
        _boundIndexBuffer = default;
        _boundIndexOffset = 0;
        _boundIndexType = IndexType.Uint32;
        _boundDescriptorSet = default;
        _boundDescriptorSetPipelineLayout = default;
        _pushConstantLayout = default;
        _pushConstantSize = 0;
        _scissor = null;
    }

    /// <summary>Gives back the batch Begin claimed and forgets the instance, once its end has been recorded.</summary>
    private void ReleaseRenderPassScope()
    {
        _context.EndRenderPassScope(this);
        _inRenderPass = false;
        _suspended = false;
        _currentFramebuffer = null;
        ForgetRecordedState();
    }

    private void ThrowIfNotInRenderPass()
    {
        if (!_inRenderPass)
        {
            throw new InvalidOperationException("Render pass not begun");
        }
    }

    public void End()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        ThrowIfNotInRenderPass();

        _context.Vk.CmdEndRenderPass(_currentCommandBuffer);
        ReleaseRenderPassScope();
    }

    public void BindPipeline(IPipeline3D pipeline)
    {
        ThrowIfNotInRenderPass();

        var vulkanPipeline = _context.RequireOwned<VulkanPipeline3D>(pipeline, nameof(pipeline));
        if (!vulkanPipeline.IsCompatibleWith(this))
        {
            throw new ArgumentException("The pipeline was created for a different render pass.", nameof(pipeline));
        }

        _currentPipeline = vulkanPipeline;
        _context.Vk.CmdBindPipeline(_currentCommandBuffer, PipelineBindPoint.Graphics, vulkanPipeline.Handle);
    }

    public void BindVertexBuffer(IBuffer buffer)
        => BindVertexBuffer(0, buffer, 0);

    internal void BindVertexBuffer(uint binding, IBuffer buffer, ulong offset)
    {
        ThrowIfNotInRenderPass();

        var vulkanBuffer = _context.RequireOwned<VulkanBuffer>(buffer, nameof(buffer));
        var bufferHandle = vulkanBuffer.Handle;
        _boundVertexBuffers[binding] = (bufferHandle, offset);
        _context.Vk.CmdBindVertexBuffers(_currentCommandBuffer, binding, 1, &bufferHandle, &offset);
    }

    public void BindIndexBuffer(IBuffer buffer)
        => BindIndexBuffer(buffer, IndexType.Uint32, 0);

    internal void BindIndexBuffer(IBuffer buffer, IndexType type, ulong offset)
    {
        ThrowIfNotInRenderPass();

        var vulkanBuffer = _context.RequireOwned<VulkanBuffer>(buffer, nameof(buffer));
        _boundIndexBuffer = vulkanBuffer.Handle;
        _boundIndexType = type;
        _boundIndexOffset = offset;
        _context.Vk.CmdBindIndexBuffer(_currentCommandBuffer, vulkanBuffer.Handle, offset, type);
    }

    /// <remarks>
    /// Rejects on layout handle identity, stricter than Vulkan's rule that identically defined layouts are
    /// interchangeable, because the managed layer does not hold the stage flags that rule also compares.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="descriptorSet"/> was allocated from a different pipeline's descriptor set layout.
    /// </exception>
    public void BindDescriptorSet(IPipeline3D pipeline, IDescriptorSet descriptorSet)
    {
        ThrowIfNotInRenderPass();

        var vulkanPipeline = _context.RequireOwned<VulkanPipeline3D>(pipeline, nameof(pipeline));
        var vulkanDescriptorSet = _context.RequireOwned<VulkanDescriptorSet>(descriptorSet, nameof(descriptorSet));
        if (vulkanDescriptorSet.Layout.Handle != vulkanPipeline.DescriptorSetLayoutHandle.Handle)
        {
            throw new ArgumentException(
                "The descriptor set was allocated from a different pipeline's descriptor set layout, so the "
                + "pipeline layout it would be bound against does not describe it. Allocate the set from the "
                + "pipeline it is bound with.",
                nameof(descriptorSet));
        }

        var set = vulkanDescriptorSet.Handle;
        _boundDescriptorSet = set;
        _boundDescriptorSetPipelineLayout = vulkanPipeline.PipelineLayoutHandle;
        _context.Vk.CmdBindDescriptorSets(
            _currentCommandBuffer,
            PipelineBindPoint.Graphics,
            vulkanPipeline.PipelineLayoutHandle,
            0,
            1,
            &set,
            0,
            null);
    }

    /// <remarks>
    /// Drawing through a layout the set was not bound against violates <c>VUID-vkCmdDraw-None-08600</c>. It is
    /// checked here rather than in <see cref="BindDescriptorSet"/> because the spec lets a set be bound with any
    /// pipeline bound; a set that was never bound is left alone.
    /// </remarks>
    private void ValidateDrawState()
    {
        if (_currentPipeline is not { } pipeline)
        {
            throw new InvalidOperationException("No pipeline bound");
        }

        if (_boundDescriptorSet.Handle != 0
            && _boundDescriptorSetPipelineLayout.Handle != pipeline.PipelineLayoutHandle.Handle)
        {
            throw new InvalidOperationException(
                "The bound descriptor set was programmed against a different pipeline's layout than the one "
                + "this draw reads it through, so the shader would resolve it against declarations it was "
                + "never written for. Bind the descriptor set with the pipeline that draws with it.");
        }
    }

    public void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int vertexOffset = 0, uint firstInstance = 0)
    {
        ThrowIfNotInRenderPass();

        ValidateDrawState();

        _context.Vk.CmdDrawIndexed(_currentCommandBuffer, indexCount, instanceCount, firstIndex, vertexOffset, firstInstance);
    }

    public void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0)
    {
        ThrowIfNotInRenderPass();

        ValidateDrawState();

        _context.Vk.CmdDraw(_currentCommandBuffer, vertexCount, instanceCount, firstVertex, firstInstance);
    }

    internal void DrawIndirect(IBuffer commands, ulong offset, uint count, uint stride, bool indexed)
    {
        ThrowIfNotInRenderPass();
        ValidateDrawState();
        var buffer = _context.RequireOwned<VulkanBuffer>(commands, nameof(commands));
        if (indexed)
            _context.Vk.CmdDrawIndexedIndirect(_currentCommandBuffer, buffer.Handle, offset, count, stride);
        else
            _context.Vk.CmdDrawIndirect(_currentCommandBuffer, buffer.Handle, offset, count, stride);
    }

    /// <summary>Limits rasterization to <paramref name="area"/> until the pass ends, including across a resume.</summary>
    internal void SetScissor(PixelRect area)
    {
        ThrowIfNotInRenderPass();

        var scissor = new Rect2D(new Offset2D(area.X, area.Y), new Extent2D((uint)area.Width, (uint)area.Height));
        _scissor = scissor;
        _context.Vk.CmdSetScissor(_currentCommandBuffer, 0, 1, &scissor);
    }

    public void SetPushConstants<T>(T data) where T : unmanaged
        => SetPushConstants(new ReadOnlySpan<byte>(&data, sizeof(T)));

    internal void SetPushConstants(ReadOnlySpan<byte> data)
    {
        ThrowIfNotInRenderPass();

        if (_currentPipeline == null)
        {
            throw new InvalidOperationException("No pipeline bound");
        }

        var size = (uint)data.Length;
        if (size > VulkanPipeline3D.MaxPushConstantsSize)
        {
            throw new ArgumentException(
                $"Push constants size {size} exceeds maximum of {VulkanPipeline3D.MaxPushConstantsSize} bytes");
        }

        data.CopyTo(_pushConstantData);
        _pushConstantLayout = _currentPipeline.PipelineLayoutHandle;
        _pushConstantSize = size;

        fixed (byte* bytes = data)
        {
            _context.Vk.CmdPushConstants(
                _currentCommandBuffer,
                _currentPipeline.PipelineLayoutHandle,
                VulkanPipeline3D.PushConstantStages,
                0,
                size,
                bytes);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Dispose can run without End, under a throwing `using`. The scope Begin claimed belongs to the context, so
        // keeping it would reject every later Begin there and suspend this pass after its handles are destroyed.
        if (_inRenderPass)
        {
            if (!_suspended)
                _context.Vk.CmdEndRenderPass(_currentCommandBuffer);

            ReleaseRenderPassScope();
        }

        RenderPass renderPass = _renderPass;
        RenderPass resumeRenderPass = _resumeRenderPass;
        _context.DeferRelease(() =>
        {
            _context.Vk.DestroyRenderPass(_context.Device, renderPass, null);
            if (resumeRenderPass.Handle != 0)
                _context.Vk.DestroyRenderPass(_context.Device, resumeRenderPass, null);
        });
    }

    private static Silk.NET.Vulkan.AttachmentLoadOp ToVulkanLoadOp(AttachmentLoadOp loadOp)
    {
        return loadOp switch
        {
            AttachmentLoadOp.Load => Silk.NET.Vulkan.AttachmentLoadOp.Load,
            AttachmentLoadOp.Clear => Silk.NET.Vulkan.AttachmentLoadOp.Clear,
            AttachmentLoadOp.DontCare => Silk.NET.Vulkan.AttachmentLoadOp.DontCare,
            _ => Silk.NET.Vulkan.AttachmentLoadOp.Clear
        };
    }
}
