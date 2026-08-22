using System;
using System.Collections.Generic;
using Silk.NET.Vulkan;

namespace Beutl.Graphics.Backend.Vulkan;

/// <summary>
/// Vulkan implementation of <see cref="IFramebuffer3D"/> with MRT support.
/// </summary>
internal sealed unsafe class VulkanFramebuffer3D : IFramebuffer3D, IVulkanContextResource
{
    private readonly VulkanContext _context;
    private readonly VulkanRenderPass3D _renderPass;
    private readonly Framebuffer _framebuffer;
    private readonly List<VulkanTexture2D> _colorTextures;
    private readonly VulkanTexture2D? _depthTexture;
    private readonly bool _ownsColorTextures;
    private readonly bool _ownsDepthTexture;
    private readonly int _width;
    private readonly int _height;
    private bool _disposed;

    public VulkanContext OwnerContext => _context;

    /// <summary>
    /// Creates a framebuffer with the specified color textures and optional depth texture.
    /// </summary>
    public VulkanFramebuffer3D(
        VulkanContext context,
        VulkanRenderPass3D renderPass,
        IReadOnlyList<VulkanTexture2D> colorTextures,
        VulkanTexture2D? depthTexture,
        bool ownsColorTextures = false,
        bool ownsDepthTexture = false)
    {
        if (colorTextures.Count == 0)
        {
            throw new ArgumentException("At least one color texture is required", nameof(colorTextures));
        }

        if (colorTextures.Count != renderPass.ColorAttachmentCount)
        {
            throw new ArgumentException(
                "The framebuffer color attachment count must match the render pass.",
                nameof(colorTextures));
        }

        if (renderPass.HasDepthAttachment != (depthTexture is not null))
        {
            throw new ArgumentException(
                "The framebuffer depth attachment must match the render pass.",
                nameof(depthTexture));
        }

        for (int i = 0; i < colorTextures.Count; i++)
        {
            if (colorTextures[i].Format.ToVulkanFormat() != renderPass.ColorFormats[i])
            {
                throw new ArgumentException(
                    $"Color attachment {i} format must match the render pass.",
                    nameof(colorTextures));
            }
        }

        if (depthTexture is not null && depthTexture.Format.ToVulkanFormat() != renderPass.DepthFormat)
        {
            throw new ArgumentException(
                "The depth attachment format must match the render pass.",
                nameof(depthTexture));
        }

        _context = context;
        _renderPass = renderPass;
        _colorTextures = new List<VulkanTexture2D>(colorTextures);
        _depthTexture = depthTexture;
        _ownsColorTextures = ownsColorTextures;
        _ownsDepthTexture = ownsDepthTexture;
        _width = colorTextures[0].Width;
        _height = colorTextures[0].Height;

        foreach (VulkanTexture2D colorTexture in colorTextures)
        {
            ValidateDimensions(colorTexture, _width, _height, nameof(colorTextures));
        }

        if (depthTexture is not null)
        {
            ValidateDimensions(depthTexture, _width, _height, nameof(depthTexture));
        }

        var vk = context.Vk;
        var device = context.Device;

        // Create framebuffer with all attachments
        int attachmentCount = colorTextures.Count + (depthTexture is not null ? 1 : 0);
        var attachments = stackalloc ImageView[attachmentCount];

        for (int i = 0; i < colorTextures.Count; i++)
        {
            attachments[i] = colorTextures[i].ImageViewHandle;
        }
        if (depthTexture is not null)
        {
            attachments[colorTextures.Count] = depthTexture.ImageViewHandle;
        }

        var framebufferInfo = new FramebufferCreateInfo
        {
            SType = StructureType.FramebufferCreateInfo,
            RenderPass = renderPass.Handle,
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

    public ITexture2D? DepthTexture => _depthTexture;

    public Framebuffer Handle => _framebuffer;

    public bool IsCompatibleWith(VulkanRenderPass3D renderPass) => ReferenceEquals(_renderPass, renderPass);

    public void PrepareForSampling()
    {
        foreach (var texture in _colorTextures)
        {
            texture.TransitionTo(ImageLayout.ShaderReadOnlyOptimal);
        }
        _depthTexture?.TransitionTo(ImageLayout.ShaderReadOnlyOptimal);
    }

    public void PrepareForRendering()
    {
        foreach (var texture in _colorTextures)
        {
            texture.TransitionTo(ImageLayout.ColorAttachmentOptimal);
        }
        _depthTexture?.TransitionTo(ImageLayout.DepthStencilAttachmentOptimal);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Framebuffer framebuffer = _framebuffer;
        _context.DeferRelease(() =>
            _context.Vk.DestroyFramebuffer(_context.Device, framebuffer, null));

        if (_ownsColorTextures)
        {
            foreach (var texture in _colorTextures)
            {
                texture.Dispose();
            }
        }

        if (_ownsDepthTexture && _depthTexture is not null)
        {
            _depthTexture.Dispose();
        }
    }

    private static void ValidateDimensions(VulkanTexture2D texture, int width, int height, string paramName)
    {
        if (texture.Width != width || texture.Height != height)
        {
            throw new ArgumentException(
                "All framebuffer attachments must have identical dimensions.",
                paramName);
        }
    }
}
