using System;
using Beutl.Media;
using Silk.NET.Vulkan;

namespace Beutl.Graphics.Backend.Vulkan;

/// <summary>
/// Vulkan implementation of <see cref="IRenderPass3D"/>.
/// </summary>
internal sealed unsafe class VulkanRenderPass3D : IRenderPass3D
{
    private readonly VulkanContext _context;
    private readonly RenderPass _renderPass;
    private readonly Format _colorFormat;
    private readonly Format _depthFormat;
    private CommandBuffer _currentCommandBuffer;
    private bool _inRenderPass;
    private bool _disposed;

    public VulkanRenderPass3D(
        VulkanContext context,
        Format colorFormat = Format.R8G8B8A8Unorm,
        Format depthFormat = Format.D32Sfloat)
    {
        _context = context;
        _colorFormat = colorFormat;
        _depthFormat = depthFormat;

        var vk = context.Vk;
        var device = context.Device;

        // Color attachment
        var colorAttachment = new AttachmentDescription
        {
            Format = colorFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.ColorAttachmentOptimal,
            FinalLayout = ImageLayout.ColorAttachmentOptimal
        };

        // Depth attachment
        var depthAttachment = new AttachmentDescription
        {
            Format = depthFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.DontCare,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.DepthStencilAttachmentOptimal,
            FinalLayout = ImageLayout.DepthStencilAttachmentOptimal
        };

        var colorAttachmentRef = new AttachmentReference
        {
            Attachment = 0,
            Layout = ImageLayout.ColorAttachmentOptimal
        };

        var depthAttachmentRef = new AttachmentReference
        {
            Attachment = 1,
            Layout = ImageLayout.DepthStencilAttachmentOptimal
        };

        var subpass = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorAttachmentRef,
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

        var attachments = stackalloc AttachmentDescription[] { colorAttachment, depthAttachment };

        var renderPassInfo = new RenderPassCreateInfo
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 2,
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

    public void Begin(IFramebuffer3D framebuffer, Color clearColor, float clearDepth = 1.0f)
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

        // Prepare color texture for rendering
        vulkanFramebuffer.ColorTexture.PrepareForRender();

        var clearValues = stackalloc ClearValue[2];
        clearValues[0].Color = new ClearColorValue(
            clearColor.R / 255f,
            clearColor.G / 255f,
            clearColor.B / 255f,
            clearColor.A / 255f);
        clearValues[1].DepthStencil = new ClearDepthStencilValue(clearDepth, 0);

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
            ClearValueCount = 2,
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
    }

    public CommandBuffer GetCurrentCommandBuffer()
    {
        if (!_inRenderPass)
        {
            throw new InvalidOperationException("Render pass not begun");
        }
        return _currentCommandBuffer;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _context.Vk.DestroyRenderPass(_context.Device, _renderPass, null);
    }
}
