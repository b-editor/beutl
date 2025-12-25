using System;
using Silk.NET.Vulkan;

namespace Beutl.Graphics.Backend.Vulkan;

/// <summary>
/// Vulkan implementation of <see cref="IFramebuffer3D"/>.
/// </summary>
internal sealed unsafe class VulkanFramebuffer3D : IFramebuffer3D
{
    private readonly VulkanContext _context;
    private readonly Framebuffer _framebuffer;
    private readonly ISharedTexture _colorTexture;
    private readonly VulkanTexture2D _depthTexture;
    private readonly bool _ownsDepthTexture;
    private bool _disposed;

    public VulkanFramebuffer3D(
        VulkanContext context,
        RenderPass renderPass,
        ISharedTexture colorTexture,
        VulkanTexture2D? depthTexture = null)
    {
        _context = context;
        _colorTexture = colorTexture;

        // Create depth texture if not provided
        if (depthTexture == null)
        {
            _depthTexture = new VulkanTexture2D(
                context,
                colorTexture.Width,
                colorTexture.Height,
                TextureFormat.Depth32Float,
                ImageUsageFlags.DepthStencilAttachmentBit);
            _ownsDepthTexture = true;

            // Transition depth texture to depth attachment layout
            _depthTexture.TransitionTo(ImageLayout.DepthStencilAttachmentOptimal);
        }
        else
        {
            _depthTexture = depthTexture;
            _ownsDepthTexture = false;
        }

        var vk = context.Vk;
        var device = context.Device;

        // Create framebuffer
        var attachments = stackalloc ImageView[2];
        attachments[0] = new ImageView((ulong)colorTexture.VulkanImageViewHandle);
        attachments[1] = _depthTexture.ImageViewHandle;

        var framebufferInfo = new FramebufferCreateInfo
        {
            SType = StructureType.FramebufferCreateInfo,
            RenderPass = renderPass,
            AttachmentCount = 2,
            PAttachments = attachments,
            Width = (uint)colorTexture.Width,
            Height = (uint)colorTexture.Height,
            Layers = 1
        };

        Framebuffer framebuffer;
        var result = vk.CreateFramebuffer(device, &framebufferInfo, null, &framebuffer);
        if (result != Result.Success)
        {
            if (_ownsDepthTexture)
            {
                _depthTexture.Dispose();
            }
            throw new InvalidOperationException($"Failed to create framebuffer: {result}");
        }
        _framebuffer = framebuffer;
    }

    public int Width => _colorTexture.Width;

    public int Height => _colorTexture.Height;

    public ISharedTexture ColorTexture => _colorTexture;

    public ITexture2D DepthTexture => _depthTexture;

    public Framebuffer Handle => _framebuffer;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _context.Vk.DestroyFramebuffer(_context.Device, _framebuffer, null);

        if (_ownsDepthTexture)
        {
            _depthTexture.Dispose();
        }
    }
}
