using System;

namespace Beutl.Graphics.Backend;

/// <summary>
/// Interface for cube map texture array abstraction.
/// Used for multiple omnidirectional shadow maps (point light shadows).
/// </summary>
public interface ITextureCubeArray : IDisposable
{
    /// <summary>
    /// Gets the size (width and height) of each cube face.
    /// </summary>
    int Size { get; }

    /// <summary>
    /// Gets the number of cube maps in the array.
    /// </summary>
    uint ArraySize { get; }

    /// <summary>
    /// Gets the format of the texture.
    /// </summary>
    TextureFormat Format { get; }
}
