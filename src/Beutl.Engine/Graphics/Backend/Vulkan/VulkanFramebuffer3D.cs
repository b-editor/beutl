using System;
using System.Collections.Generic;
using Silk.NET.Vulkan;

namespace Beutl.Graphics.Backend.Vulkan;

/// <summary>
/// Vulkan implementation of <see cref="IFramebuffer3D"/> with MRT support.
/// </summary>
internal sealed unsafe class VulkanFramebuffer3D : IFramebuffer3D
{
    private readonly VulkanContext _context;
    private readonly Framebuffer _framebuffer;
    private readonly List<VulkanTexture2D> _colorTextures;
    private readonly VulkanTexture2D _depthTexture;
    private readonly bool _ownsColorTextures;
    private readonly bool _ownsDepthTexture;
    private readonly int _width;
    private readonly int _height;
    private bool _disposed;

    /// <summary>
    /// Creates a framebuffer with the specified color and depth textures.
    /// </summary>
    public VulkanFramebuffer3D(
        VulkanContext context,
        RenderPass renderPass,
        IReadOnlyList<VulkanTexture2D> colorTextures,
        VulkanTexture2D depthTexture,
        bool ownsColorTextures = false,
        bool ownsDepthTexture = false)
    {
        if (colorTextures.Count == 0)
        {
            throw new ArgumentException("At least one color texture is required", nameof(colorTextures));
        }

        _context = context;
        _colorTextures = new List<VulkanTexture2D>(colorTextures);
        _depthTexture = depthTexture;
        _ownsColorTextures = ownsColorTextures;
        _ownsDepthTexture = ownsDepthTexture;
        _width = colorTextures[0].Width;
        _height = colorTextures[0].Height;

        var vk = context.Vk;
        var device = context.Device;

        // Create framebuffer with all attachments
        int attachmentCount = colorTextures.Count + 1; // colors + depth
        var attachments = stackalloc ImageView[attachmentCount];

        for (int i = 0; i < colorTextures.Count; i++)
        {
            attachments[i] = colorTextures[i].ImageViewHandle;
        }
        attachments[colorTextures.Count] = depthTexture.ImageViewHandle;

        var framebufferInfo = new FramebufferCreateInfo
        {
            SType = StructureType.FramebufferCreateInfo,
            RenderPass = renderPass,
            AttachmentCount = (uint)attachmentCount,
            PAttachments = attachments,
            Width = (uint)_width,
            Height = (uint)_height,
            Layers = 1
        };

        Framebuffer framebuffer;
        var result = vk.CreateFramebuffer(device, &framebufferInfo, null, &framebuffer);
        if (result != Result.Success)
        {
            throw new InvalidOperationException($"Failed to create framebuffer: {result}");
        }
        _framebuffer = framebuffer;
    }

    public int Width => _width;

    public int Height => _height;

    public IReadOnlyList<ITexture2D> ColorTextures => _colorTextures;

    public ITexture2D DepthTexture => _depthTexture;

    public Framebuffer Handle => _framebuffer;

    public void PrepareForSampling()
    {
        foreach (var texture in _colorTextures)
        {
            texture.TransitionTo(ImageLayout.ShaderReadOnlyOptimal);
        }
        _depthTexture.TransitionTo(ImageLayout.ShaderReadOnlyOptimal);
    }

    public void PrepareForRendering()
    {
        foreach (var texture in _colorTextures)
        {
            texture.TransitionTo(ImageLayout.ColorAttachmentOptimal);
        }
        _depthTexture.TransitionTo(ImageLayout.DepthStencilAttachmentOptimal);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _context.Vk.DestroyFramebuffer(_context.Device, _framebuffer, null);

        if (_ownsColorTextures)
        {
            foreach (var texture in _colorTextures)
            {
                texture.Dispose();
            }
        }

        if (_ownsDepthTexture)
        {
            _depthTexture.Dispose();
        }
    }
}
