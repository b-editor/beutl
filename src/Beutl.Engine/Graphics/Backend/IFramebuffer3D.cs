using System;

namespace Beutl.Graphics.Backend;

/// <summary>
/// Interface for a framebuffer with MRT (Multiple Render Targets) and optional depth support.
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
    /// Prepares all textures for sampling.
    /// </summary>
    void PrepareForSampling();
}
