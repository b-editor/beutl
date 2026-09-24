using Beutl.Graphics;
using Beutl.Graphics.Rendering;

namespace Beutl.Graphics3D.Textures;

/// <summary>
/// A texture source whose pixels come from 2D content recorded as a nested render target of the scene that
/// samples it, rather than from a texture it owns.
/// </summary>
internal interface IRecordedTextureSource
{
    /// <summary>The logical rectangle the texture covers.</summary>
    Rect TextureDomain { get; }

    /// <summary>The density the nested target is rendered at for a surface of <paramref name="density"/>.</summary>
    float ResolveDensity(float density);

    /// <summary>Records the content at <paramref name="density"/>, or returns <see langword="null"/> when there is none.</summary>
    RenderNode? RecordContent(float density);
}
