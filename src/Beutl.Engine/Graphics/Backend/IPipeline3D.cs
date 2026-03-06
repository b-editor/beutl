using System;

namespace Beutl.Graphics.Backend;

/// <summary>
/// Interface for 3D graphics pipeline abstraction.
/// </summary>
public interface IPipeline3D : IDisposable
{
    /// <summary>
    /// Binds this pipeline for use in rendering.
    /// </summary>
    void Bind();
}
