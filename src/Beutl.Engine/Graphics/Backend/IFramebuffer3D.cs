using System;
using System.Collections.Generic;

namespace Beutl.Graphics.Backend;

/// <summary>
/// Interface for 3D framebuffer with MRT (Multiple Render Targets) support.
/// </summary>
public interface IFramebuffer3D : IDisposable
{
    /// <summary>
    /// Gets the width of the framebuffer.
    /// </summary>
    int Width { get; }

    /// <summary>
    /// Gets the height of the framebuffer.
    /// </summary>
    int Height { get; }

    /// <summary>
    /// Gets all color texture attachments.
    /// </summary>
    IReadOnlyList<ITexture2D> ColorTextures { get; }

    /// <summary>
    /// Gets the depth texture attachment.
    /// </summary>
    ITexture2D DepthTexture { get; }

    /// <summary>
    /// Prepares all textures for sampling.
    /// </summary>
    void PrepareForSampling();

    /// <summary>
    /// Prepares all textures for rendering.
    /// </summary>
    void PrepareForRendering();
}
