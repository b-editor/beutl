namespace Beutl.Graphics.Backend;

/// <summary>
/// Internal content-state contract for textures whose backend can record an ordered transparent clear.
/// </summary>
internal interface ITransparentClearableTexture
{
    /// <summary>
    /// Gets whether the most recently recorded write defines the whole texture as transparent.
    /// </summary>
    bool HasTransparentContents { get; }

    /// <summary>
    /// Records a transparent clear unless the current recorded content is already known transparent.
    /// </summary>
    void ClearToTransparent();

    /// <summary>
    /// Records that something other than <see cref="ClearToTransparent"/> has just defined the whole
    /// texture as transparent.
    /// </summary>
    /// <remarks>
    /// A surface cleared through Skia leaves the image transparent too, but the backend cannot see that
    /// write. Without this the texture keeps reporting unknown contents and the next caller that wants a
    /// blank target clears an already-blank image.
    /// </remarks>
    void MarkContentsTransparent();
}
