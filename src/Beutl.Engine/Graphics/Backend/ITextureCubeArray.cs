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

    /// <summary>
    /// Gets the native handle of the cube map array texture.
    /// </summary>
    IntPtr NativeHandle { get; }

    /// <summary>
    /// Transitions the entire texture array to be used as a sampled texture in shaders.
    /// </summary>
    void TransitionToSampled();

    /// <summary>
    /// Gets the image view for a specific cube face in a specific array layer (for framebuffer attachment).
    /// </summary>
    /// <param name="arrayIndex">The array layer index.</param>
    /// <param name="faceIndex">The cube face index (0=+X, 1=-X, 2=+Y, 3=-Y, 4=+Z, 5=-Z).</param>
    /// <returns>The native handle of the face's image view.</returns>
    IntPtr GetFaceView(uint arrayIndex, int faceIndex);
}
