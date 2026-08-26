using System;
using System.Collections.Generic;
using Beutl.Media;
using Silk.NET.Vulkan;

namespace Beutl.Graphics.Backend.Vulkan;

/// <summary>
/// Vulkan implementation of <see cref="IRenderPass3D"/> with MRT support.
/// </summary>
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
    private Silk.NET.Vulkan.Buffer _boundVertexBuffer;
    private Silk.NET.Vulkan.Buffer _boundIndexBuffer;
    private DescriptorSet _boundDescriptorSet;
    private PipelineLayout _boundDescriptorSetLayout;
    private PipelineLayout _pushConstantLayout;
    private uint _pushConstantSize;
    private bool _inRenderPass;
    private bool _suspended;
    private bool _disposed;

    public VulkanContext OwnerContext => _context;

    /// <summary>
    /// Creates a render pass with the specified color formats and optional depth format.
    /// </summary>
    /// <param name="context">The Vulkan context.</param>
    /// <param name="colorFormats">Formats for each color attachment.</param>
    /// <param name="depthFormat">Format for the depth attachment, or null for a color-only pass.</param>
    /// <param name="colorLoadOp">The load operation for color attachments.</param>
    /// <param name="depthLoadOp">The load operation for the depth attachment.</param>
    public VulkanRenderPass3D(
        VulkanContext context,
        IReadOnlyList<Format> colorFormats,
        Format? depthFormat,
        AttachmentLoadOp colorLoadOp = AttachmentLoadOp.Clear,
        AttachmentLoadOp depthLoadOp = AttachmentLoadOp.Clear)
    {
        if (colorFormats.Count == 0)
        {
            throw new ArgumentException("At least one color format is required", nameof(colorFormats));
        }

        _context = context;
        _colorFormats = [.. colorFormats];
        _depthFormat = depthFormat;
        _colorAttachmentCount = colorFormats.Count;

        var vk = context.Vk;
        var device = context.Device;

        int totalAttachments = colorFormats.Count + (depthFormat.HasValue ? 1 : 0);

        // Create attachment descriptions
        var attachments = stackalloc AttachmentDescription[totalAttachments];
        var colorAttachmentRefs = stackalloc AttachmentReference[colorFormats.Count];

        var vulkanColorLoadOp = ToVulkanLoadOp(colorLoadOp);
        for (int i = 0; i < colorFormats.Count; i++)
        {
            attachments[i] = new AttachmentDescription
            {
                Format = colorFormats[i],
                Samples = SampleCountFlags.Count1Bit,
                LoadOp = vulkanColorLoadOp,
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
                LoadOp = ToVulkanLoadOp(depthLoadOp),
                StoreOp = AttachmentStoreOp.Store,
                StencilLoadOp = Silk.NET.Vulkan.AttachmentLoadOp.DontCare,
                StencilStoreOp = AttachmentStoreOp.DontCare,
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
        AccessFlags attachmentWrites = AccessFlags.ColorAttachmentWriteBit;
        if (depthFormat.HasValue)
        {
            attachmentStages |= PipelineStageFlags.EarlyFragmentTestsBit;
            attachmentWrites |= AccessFlags.DepthStencilAttachmentWriteBit;
        }

        var dependency = new SubpassDependency
        {
            SrcSubpass = Vk.SubpassExternal,
            DstSubpass = 0,
            SrcStageMask = attachmentStages,
            SrcAccessMask = 0,
            DstStageMask = attachmentStages,
            DstAccessMask = attachmentWrites
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

        RenderPass renderPass;
        var result = vk.CreateRenderPass(device, &renderPassInfo, null, &renderPass);
        if (result != Result.Success)
        {
            throw new InvalidOperationException($"Failed to create render pass: {result}");
        }
        _renderPass = renderPass;
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
                StencilLoadOp = Silk.NET.Vulkan.AttachmentLoadOp.DontCare,
                StencilStoreOp = AttachmentStoreOp.DontCare,
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
        AccessFlags attachmentWrites = AccessFlags.ColorAttachmentWriteBit;
        if (depthFormat.HasValue)
        {
            attachmentStages |= PipelineStageFlags.EarlyFragmentTestsBit;
            attachmentWrites |= AccessFlags.DepthStencilAttachmentWriteBit;
        }

        var dependency = new SubpassDependency
        {
            SrcSubpass = Vk.SubpassExternal,
            DstSubpass = 0,
            SrcStageMask = attachmentStages,
            SrcAccessMask = 0,
            DstStageMask = attachmentStages,
            DstAccessMask = attachmentWrites
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

        // Prepare textures for rendering
        vulkanFramebuffer.PrepareForRendering();

        // Barriers, copies, and consecutive render passes share one command buffer until an
        // external consumer requires submission.
        _currentCommandBuffer = _context.GetRecordingCommandBuffer();

        // Create clear values for all attachments
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
            clearValues[_colorAttachmentCount].DepthStencil = new ClearDepthStencilValue(clearDepth, 0);
        }

        var renderPassBeginInfo = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _renderPass,
            Framebuffer = vulkanFramebuffer.Handle,
            RenderArea = new Rect2D
            {
                Offset = new Offset2D(0, 0),
                Extent = new Extent2D((uint)vulkanFramebuffer.Width, (uint)vulkanFramebuffer.Height)
            },
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

        var scissor = new Rect2D
        {
            Offset = new Offset2D(0, 0),
            Extent = new Extent2D((uint)framebuffer.Width, (uint)framebuffer.Height)
        };
        _context.Vk.CmdSetScissor(_currentCommandBuffer, 0, 1, &scissor);
    }

    /// <remarks>
    /// Vulkan forbids a transfer or a barrier inside a render pass instance, and appending one to the batch
    /// the pass is still recording is not an option either. Ending the instance, recording the work, and
    /// beginning it again keeps everything on one command buffer in the order it was recorded, which is what
    /// a draw already recorded in this pass needs: it must not end up running after work requested later.
    /// </remarks>
    bool IVulkanRenderPassSuspension.TrySuspend()
    {
        if (!_inRenderPass || _suspended || _currentFramebuffer is null)
            return false;

        _context.Vk.CmdEndRenderPass(_currentCommandBuffer);
        _suspended = true;
        return true;
    }

    void IVulkanRenderPassSuspension.Resume()
    {
        VulkanFramebuffer3D framebuffer = _currentFramebuffer
            ?? throw new InvalidOperationException("A suspended render pass lost the framebuffer it was recording into.");

        // The batch this pass was recording into may have been submitted while it was suspended - that is
        // what a synchronous flush does - so the instance resumes on whatever batch is recording now.
        _currentCommandBuffer = _context.GetRecordingCommandBuffer();

        var renderPassBeginInfo = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = GetOrCreateResumeRenderPass(),
            Framebuffer = framebuffer.Handle,
            RenderArea = new Rect2D
            {
                Offset = new Offset2D(0, 0),
                Extent = new Extent2D((uint)framebuffer.Width, (uint)framebuffer.Height)
            },
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
    /// A synchronous flush during a suspension submits the batch, so the instance resumes on a freshly
    /// allocated command buffer that holds none of the bindings the caller made before the split: the
    /// pipeline, the vertex and index buffers, the descriptor set, and the push constants are all command
    /// buffer state and would otherwise be absent from the next <c>Draw</c>. Every one of them is recorded
    /// as the caller makes it and replayed here; the viewport and scissor, the only other state this pass
    /// records, are re-issued by the caller of this method.
    ///
    /// The order is what the spec requires, not a preference: binding a pipeline can disturb descriptor
    /// bindings made under an incompatible layout, and binding a descriptor set can leave push constant
    /// values undefined, so the pipeline goes first and the push constants last.
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
                _boundDescriptorSetLayout,
                0,
                1,
                &set,
                0,
                null);
        }

        if (_boundVertexBuffer.Handle != 0)
        {
            Silk.NET.Vulkan.Buffer vertexBuffer = _boundVertexBuffer;
            ulong offset = 0;
            vk.CmdBindVertexBuffers(_currentCommandBuffer, 0, 1, &vertexBuffer, &offset);
        }

        if (_boundIndexBuffer.Handle != 0)
        {
            vk.CmdBindIndexBuffer(_currentCommandBuffer, _boundIndexBuffer, 0, IndexType.Uint32);
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
        _boundVertexBuffer = default;
        _boundIndexBuffer = default;
        _boundDescriptorSet = default;
        _boundDescriptorSetLayout = default;
        _pushConstantLayout = default;
        _pushConstantSize = 0;
    }

    public void End()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_inRenderPass)
        {
            throw new InvalidOperationException("Render pass not begun");
        }

        _context.Vk.CmdEndRenderPass(_currentCommandBuffer);
        _context.EndRenderPassScope(this);

        _inRenderPass = false;
        _suspended = false;
        _currentFramebuffer = null;
        ForgetRecordedState();
    }

    public CommandBuffer GetCurrentCommandBuffer()
    {
        if (!_inRenderPass)
        {
            throw new InvalidOperationException("Render pass not begun");
        }
        return _currentCommandBuffer;
    }

    public void BindPipeline(IPipeline3D pipeline)
    {
        if (!_inRenderPass)
        {
            throw new InvalidOperationException("Render pass not begun");
        }

        var vulkanPipeline = _context.RequireOwned<VulkanPipeline3D>(pipeline, nameof(pipeline));
        if (!vulkanPipeline.IsCompatibleWith(this))
        {
            throw new ArgumentException("The pipeline was created for a different render pass.", nameof(pipeline));
        }

        _currentPipeline = vulkanPipeline;
        _context.Vk.CmdBindPipeline(_currentCommandBuffer, PipelineBindPoint.Graphics, vulkanPipeline.Handle);
    }

    public void BindVertexBuffer(IBuffer buffer)
    {
        if (!_inRenderPass)
        {
            throw new InvalidOperationException("Render pass not begun");
        }

        var vulkanBuffer = _context.RequireOwned<VulkanBuffer>(buffer, nameof(buffer));
        var bufferHandle = vulkanBuffer.Handle;
        ulong offset = 0;
        _boundVertexBuffer = bufferHandle;
        _context.Vk.CmdBindVertexBuffers(_currentCommandBuffer, 0, 1, &bufferHandle, &offset);
    }

    public void BindIndexBuffer(IBuffer buffer)
    {
        if (!_inRenderPass)
        {
            throw new InvalidOperationException("Render pass not begun");
        }

        var vulkanBuffer = _context.RequireOwned<VulkanBuffer>(buffer, nameof(buffer));
        _boundIndexBuffer = vulkanBuffer.Handle;
        _context.Vk.CmdBindIndexBuffer(_currentCommandBuffer, vulkanBuffer.Handle, 0, IndexType.Uint32);
    }

    public void BindDescriptorSet(IPipeline3D pipeline, IDescriptorSet descriptorSet)
    {
        if (!_inRenderPass)
        {
            throw new InvalidOperationException("Render pass not begun");
        }

        var vulkanPipeline = _context.RequireOwned<VulkanPipeline3D>(pipeline, nameof(pipeline));
        var vulkanDescriptorSet = _context.RequireOwned<VulkanDescriptorSet>(descriptorSet, nameof(descriptorSet));
        var set = vulkanDescriptorSet.Handle;
        _boundDescriptorSet = set;
        _boundDescriptorSetLayout = vulkanPipeline.PipelineLayoutHandle;
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

    public void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int vertexOffset = 0, uint firstInstance = 0)
    {
        if (!_inRenderPass)
        {
            throw new InvalidOperationException("Render pass not begun");
        }

        _context.Vk.CmdDrawIndexed(_currentCommandBuffer, indexCount, instanceCount, firstIndex, vertexOffset, firstInstance);
    }

    public void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0)
    {
        if (!_inRenderPass)
        {
            throw new InvalidOperationException("Render pass not begun");
        }

        _context.Vk.CmdDraw(_currentCommandBuffer, vertexCount, instanceCount, firstVertex, firstInstance);
    }

    public void SetPushConstants<T>(T data) where T : unmanaged
    {
        if (!_inRenderPass)
        {
            throw new InvalidOperationException("Render pass not begun");
        }

        if (_currentPipeline == null)
        {
            throw new InvalidOperationException("No pipeline bound");
        }

        var size = (uint)sizeof(T);
        if (size > VulkanPipeline3D.MaxPushConstantsSize)
        {
            throw new ArgumentException(
                $"Push constants size {size} exceeds maximum of {VulkanPipeline3D.MaxPushConstantsSize} bytes");
        }

        new ReadOnlySpan<byte>(&data, (int)size).CopyTo(_pushConstantData);
        _pushConstantLayout = _currentPipeline.PipelineLayoutHandle;
        _pushConstantSize = size;

        // The bound layout's range is what decides these, not the caller: an update has to name every stage
        // of every range it overlaps, so naming fewer is undefined behaviour the driver need not report.
        _context.Vk.CmdPushConstants(
            _currentCommandBuffer,
            _currentPipeline.PipelineLayoutHandle,
            VulkanPipeline3D.PushConstantStages,
            0,
            size,
            &data);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

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
