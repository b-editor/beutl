namespace Beutl.Graphics3D.Textures;

/// <summary>
/// How a material slot interprets the samples of its texture.
/// </summary>
public enum TextureContentKind
{
    /// <summary>
    /// A color (albedo, emissive, …) that the shader expects in linear sRGB, so encoded pixels are decoded to linear.
    /// </summary>
    Color,

    /// <summary>
    /// Non-color data (normal, metallic-roughness, ambient occlusion, …) whose stored values reach the shader unchanged.
    /// </summary>
    Data,
}
