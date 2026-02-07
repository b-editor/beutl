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

    /// <summary>
    /// Gets the native handle of the cube map texture.
    /// </summary>
    IntPtr NativeHandle { get; }

    /// <summary>
    /// Transitions the texture to be used as a framebuffer attachment.
    /// </summary>
    void TransitionToAttachment();

    /// <summary>
    /// Transitions the texture to be used as a sampled texture in shaders.
    /// </summary>
    void TransitionToSampled();

    /// <summary>
    /// Uploads pixel data to a specific cube face.
    /// </summary>
    /// <param name="faceIndex">The cube face index (0=+X, 1=-X, 2=+Y, 3=-Y, 4=+Z, 5=-Z).</param>
    /// <param name="data">The pixel data to upload.</param>
    void UploadFace(int faceIndex, ReadOnlySpan<byte> data);

    /// <summary>
    /// Gets the image view for a specific cube face (for framebuffer attachment).
    /// </summary>
    /// <param name="faceIndex">The cube face index (0=+X, 1=-X, 2=+Y, 3=-Y, 4=+Z, 5=-Z).</param>
    /// <returns>The native handle of the face's image view.</returns>
    IntPtr GetFaceView(int faceIndex);
}
