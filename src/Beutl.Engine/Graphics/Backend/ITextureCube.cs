using System;

namespace Beutl.Graphics.Backend;

/// <summary>
/// Interface for cube map texture abstraction.
/// Used for omnidirectional shadow maps (point light shadows).
/// </summary>
public interface ITextureCube : IDisposable
{
    /// <summary>
    /// Gets the size (width and height) of each cube face.
    /// </summary>
    int Size { get; }

    /// <summary>
    /// Gets the format of the texture.
    /// </summary>
    TextureFormat Format { get; }
}
