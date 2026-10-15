using System;

namespace Beutl.Graphics.Backend;

/// <summary>
/// Interface for 2D texture array abstraction.
/// Used for efficiently storing multiple shadow maps in a single texture resource.
/// </summary>
public interface ITextureArray : IDisposable
{
    /// <summary>
    /// Gets the width of each texture layer.
    /// </summary>
    int Width { get; }

    /// <summary>
    /// Gets the height of each texture layer.
    /// </summary>
    int Height { get; }

    /// <summary>
    /// Gets the number of layers in the texture array.
    /// </summary>
    uint ArraySize { get; }

    /// <summary>
    /// Gets the format of the texture.
    /// </summary>
    TextureFormat Format { get; }
}
