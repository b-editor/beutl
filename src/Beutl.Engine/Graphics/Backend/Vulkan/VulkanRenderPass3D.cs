using System;
using System.Collections.Generic;
using Beutl.Media;
using Silk.NET.Vulkan;

namespace Beutl.Graphics.Backend.Vulkan;

/// <summary>
/// Vulkan implementation of <see cref="IRenderPass3D"/> with MRT support.
/// </summary>
internal sealed unsafe class VulkanRenderPass3D : IRenderPass3D
{
    private readonly VulkanContext _context;
    private readonly RenderPass _renderPass;
    private readonly int _colorAttachmentCount;
    private CommandBuffer _currentCommandBuffer;
    private VulkanPipeline3D? _currentPipeline;
    private bool _inRenderPass;
    private bool _disposed;

    /// <summary>
    /// Creates a render pass with the specified color and depth formats.
    /// </summary>
    /// <param name="context">The Vulkan context.</param>
    /// <param name="colorFormats">Formats for each color attachment.</param>
    /// <param name="depthFormat">Format for the depth attachment.</param>
    /// <param name="colorLoadOp">The load operation for color attachments.</param>
    /// <param name="depthLoadOp">The load operation for the depth attachment.</param>
    public VulkanRenderPass3D(
        VulkanContext context,
        IReadOnlyList<Format> colorFormats,
        Format depthFormat = Format.D32Sfloat,
        AttachmentLoadOp colorLoadOp = AttachmentLoadOp.Clear,
        AttachmentLoadOp depthLoadOp = AttachmentLoadOp.Clear)
    {
        if (colorFormats.Count == 0)
        {
            throw new ArgumentException("At least one color format is required", nameof(colorFormats));
        }

        _context = context;
        _colorAttachmentCount = colorFormats.Count;

        var vk = context.Vk;
        var device = context.Device;

        int totalAttachments = colorFormats.Count + 1; // colors + depth

        // Create attachment descriptions
        var attachments = stackalloc AttachmentDescription[totalAttachments];
        var colorAttachmentRefs = stackalloc AttachmentReference[colorFormats.Count];

        var vulkanColorLoadOp = ToVulkanLoadOp(colorLoadOp);
        var vulkanDepthLoadOp = ToVulkanLoadOp(depthLoadOp);

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

        // Depth attachment (last)
        attachments[colorFormats.Count] = new AttachmentDescription
        {
            Format = depthFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = vulkanDepthLoadOp,
            StoreOp = AttachmentStoreOp.Store, // Store depth for shadow mapping
            StencilLoadOp = Silk.NET.Vulkan.AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.DepthStencilAttachmentOptimal,
            FinalLayout = ImageLayout.DepthStencilAttachmentOptimal
        };

        var depthAttachmentRef = new AttachmentReference
        {
            Attachment = (uint)colorFormats.Count,
            Layout = ImageLayout.DepthStencilAttachmentOptimal
        };

        var subpass = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = (uint)colorFormats.Count,
            PColorAttachments = colorAttachmentRefs,
            PDepthStencilAttachment = &depthAttachmentRef
        };

        var dependency = new SubpassDependency
        {
            SrcSubpass = Vk.SubpassExternal,
            DstSubpass = 0,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit,
            SrcAccessMask = 0,
            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit,
            DstAccessMask = AccessFlags.ColorAttachmentWriteBit | AccessFlags.DepthStencilAttachmentWriteBit
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

    public RenderPass Handle => _renderPass;

    public int ColorAttachmentCount => _colorAttachmentCount;

    public void Begin(IFramebuffer3D framebuffer, ReadOnlySpan<Color> clearColors, float clearDepth = 1.0f)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_inRenderPass)
        {
            throw new InvalidOperationException("Render pass already begun");
        }

        var vulkanFramebuffer = (VulkanFramebuffer3D)framebuffer;

        // Allocate command buffer
        _currentCommandBuffer = _context.AllocateCommandBuffer();

        var beginInfo = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit
        };

        _context.Vk.BeginCommandBuffer(_currentCommandBuffer, &beginInfo);

        // Prepare textures for rendering
        vulkanFramebuffer.PrepareForRendering();

        // Create clear values for all attachments
        int totalClearValues = _colorAttachmentCount + 1;
        var clearValues = stackalloc ClearValue[totalClearValues];

        for (int i = 0; i < _colorAttachmentCount; i++)
        {
            if (i < clearColors.Length)
            {
                clearValues[i].Color = new ClearColorValue(
                    clearColors[i].R / 255f,
                    clearColors[i].G / 255f,
                    clearColors[i].B / 255f,
                    clearColors[i].A / 255f);
            }
            else
            {
                clearValues[i].Color = new ClearColorValue(0, 0, 0, 0);
            }
        }
        clearValues[_colorAttachmentCount].DepthStencil = new ClearDepthStencilValue(clearDepth, 0);

        var renderPassBeginInfo = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _renderPass,
            Framebuffer = vulkanFramebuffer.Handle,
            RenderArea = new Rect2D
            {
                Offset = new Offset2D(0, 0),
                Extent = new Extent2D((uint)framebuffer.Width, (uint)framebuffer.Height)
            },
            ClearValueCount = (uint)totalClearValues,
            PClearValues = clearValues
        };

        _context.Vk.CmdBeginRenderPass(_currentCommandBuffer, &renderPassBeginInfo, SubpassContents.Inline);

        // Set viewport and scissor
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

        _inRenderPass = true;
    }

    public void End()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_inRenderPass)
        {
            throw new InvalidOperationException("Render pass not begun");
        }

        _context.Vk.CmdEndRenderPass(_currentCommandBuffer);
        _context.Vk.EndCommandBuffer(_currentCommandBuffer);

        // Submit command buffer
        _context.SubmitCommandBuffer(_currentCommandBuffer);

        _inRenderPass = false;
        _currentPipeline = null;
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

        var vulkanPipeline = (VulkanPipeline3D)pipeline;
        _currentPipeline = vulkanPipeline;
        _context.Vk.CmdBindPipeline(_currentCommandBuffer, PipelineBindPoint.Graphics, vulkanPipeline.Handle);
    }

    public void BindVertexBuffer(IBuffer buffer)
    {
        if (!_inRenderPass)
        {
            throw new InvalidOperationException("Render pass not begun");
        }

        var vulkanBuffer = (VulkanBuffer)buffer;
        var bufferHandle = vulkanBuffer.Handle;
        ulong offset = 0;
        _context.Vk.CmdBindVertexBuffers(_currentCommandBuffer, 0, 1, &bufferHandle, &offset);
    }

    public void BindIndexBuffer(IBuffer buffer)
    {
        if (!_inRenderPass)
        {
            throw new InvalidOperationException("Render pass not begun");
        }

        var vulkanBuffer = (VulkanBuffer)buffer;
        _context.Vk.CmdBindIndexBuffer(_currentCommandBuffer, vulkanBuffer.Handle, 0, IndexType.Uint32);
    }

    public void BindDescriptorSet(IPipeline3D pipeline, IDescriptorSet descriptorSet)
    {
        if (!_inRenderPass)
        {
            throw new InvalidOperationException("Render pass not begun");
        }

        var vulkanPipeline = (VulkanPipeline3D)pipeline;
        var vulkanDescriptorSet = (VulkanDescriptorSet)descriptorSet;
        var set = vulkanDescriptorSet.Handle;
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

    public void SetPushConstants<T>(T data, ShaderStage stageFlags = ShaderStage.Vertex | ShaderStage.Fragment) where T : unmanaged
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
        if (size > 128)
        {
            throw new ArgumentException($"Push constants size {size} exceeds maximum of 128 bytes");
        }

        ShaderStageFlags vulkanStageFlags = 0;
        if ((stageFlags & ShaderStage.Vertex) != 0)
            vulkanStageFlags |= ShaderStageFlags.VertexBit;
        if ((stageFlags & ShaderStage.Fragment) != 0)
            vulkanStageFlags |= ShaderStageFlags.FragmentBit;

        _context.Vk.CmdPushConstants(
            _currentCommandBuffer,
            _currentPipeline.PipelineLayoutHandle,
            vulkanStageFlags,
            0,
            size,
            &data);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _context.Vk.DestroyRenderPass(_context.Device, _renderPass, null);
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
