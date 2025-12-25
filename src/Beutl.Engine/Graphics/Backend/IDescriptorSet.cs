using System;

namespace Beutl.Graphics.Backend;

/// <summary>
/// Interface for descriptor set abstraction.
/// </summary>
public interface IDescriptorSet : IDisposable
{
    /// <summary>
    /// Updates a buffer binding.
    /// </summary>
    /// <param name="binding">The binding index.</param>
    /// <param name="buffer">The buffer to bind.</param>
    void UpdateBuffer(int binding, IBuffer buffer);

    /// <summary>
    /// Updates a texture binding with a sampler (for CombinedImageSampler).
    /// </summary>
    /// <param name="binding">The binding index.</param>
    /// <param name="texture">The texture to bind.</param>
    /// <param name="sampler">The sampler to use.</param>
    void UpdateTexture(int binding, ITexture2D texture, ISampler sampler);

    /// <summary>
    /// Binds this descriptor set for use in rendering.
    /// </summary>
    void Bind();
}
