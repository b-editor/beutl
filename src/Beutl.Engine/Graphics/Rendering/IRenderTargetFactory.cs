namespace Beutl.Graphics.Rendering;

/// <summary>Creates fresh linear-premultiplied RGBA16F targets requested by a renderer.</summary>
public interface IRenderTargetFactory
{
    /// <summary>Creates a target satisfying the exact allocation requirements.</summary>
    /// <param name="allocation">The size, format, backend, and device/context requirements.</param>
    /// <returns>A new target, or <see langword="null"/> when allocation cannot be satisfied.</returns>
    /// <remarks>
    /// Every non-null return transfers exclusive ownership to the renderer immediately and must be fresh,
    /// unleased, and satisfy the size, format, and context requirements in <paramref name="allocation"/>.
    /// The renderer disposes an invalid non-null return. The factory itself remains caller-owned and is never
    /// disposed by the renderer.
    /// </remarks>
    RenderTarget? Create(RenderTargetAllocationDescriptor allocation);
}
