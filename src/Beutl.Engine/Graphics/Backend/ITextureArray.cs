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

    /// <summary>
    /// Gets the native handle of the texture array.
    /// </summary>
    IntPtr NativeHandle { get; }

    /// <summary>
    /// Transitions a specific layer to be used as a framebuffer attachment.
    /// </summary>
    /// <param name="layerIndex">The layer index to transition.</param>
    void TransitionLayerToAttachment(uint layerIndex);

    /// <summary>
    /// Transitions a specific layer to be used as a sampled texture in shaders.
    /// </summary>
    /// <param name="layerIndex">The layer index to transition.</param>
    void TransitionLayerToSampled(uint layerIndex);

    /// <summary>
    /// Transitions all layers to be used as sampled textures in shaders.
    /// </summary>
    void TransitionAllToSampled();

    /// <summary>
    /// Uploads pixel data to a specific layer.
    /// </summary>
    /// <param name="layerIndex">The layer index to upload to.</param>
    /// <param name="data">The pixel data to upload.</param>
    void UploadLayer(uint layerIndex, ReadOnlySpan<byte> data);

    /// <summary>
    /// Gets the image view for a specific layer (for framebuffer attachment).
    /// </summary>
    /// <param name="layerIndex">The layer index.</param>
    /// <returns>The native handle of the layer's image view.</returns>
    IntPtr GetLayerView(uint layerIndex);
}
