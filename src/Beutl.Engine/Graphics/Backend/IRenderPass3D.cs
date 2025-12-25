using System;
using Beutl.Media;

namespace Beutl.Graphics.Backend;

/// <summary>
/// Interface for 3D render pass abstraction.
/// </summary>
internal interface IRenderPass3D : IDisposable
{
    /// <summary>
    /// Begins the render pass.
    /// </summary>
    /// <param name="framebuffer">The framebuffer to render to.</param>
    /// <param name="clearColor">The color to clear the framebuffer with.</param>
    /// <param name="clearDepth">The depth value to clear the depth buffer with.</param>
    void Begin(IFramebuffer3D framebuffer, Color clearColor, float clearDepth = 1.0f);

    /// <summary>
    /// Ends the render pass.
    /// </summary>
    void End();
}
