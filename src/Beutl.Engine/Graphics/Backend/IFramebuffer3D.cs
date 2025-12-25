using System;
using Beutl.Media;

namespace Beutl.Graphics.Backend;

/// <summary>
/// Interface for 3D framebuffer abstraction.
/// </summary>
internal interface IFramebuffer3D : IDisposable
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
    /// Gets the color texture attachment.
    /// </summary>
    ISharedTexture ColorTexture { get; }

    /// <summary>
    /// Gets the depth texture attachment.
    /// </summary>
    ITexture2D DepthTexture { get; }
}
