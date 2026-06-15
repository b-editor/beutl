using Beutl.Engine;
using Beutl.Graphics.Backend;

namespace Beutl.Graphics3D.Textures;

public abstract partial class TextureSource : EngineObject
{
    public partial class Resource
    {
        /// <summary>
        /// Resolves the texture sampled onto a 3D surface. <paramref name="surfaceDensity"/> (feature 003) is the
        /// device-pixels-per-logical-unit density the consuming surface is rendered at (the 3D
        /// <see cref="IRenderer3D.SurfaceDensity"/>): a re-rasterizable source (e.g.
        /// <see cref="DrawableTextureSource"/> wrapping a vector <c>Drawable</c>) should rasterize at this
        /// density so it stays crisp on a supersampled / high-density surface instead of being frozen at a
        /// fixed author pixel count and GPU-magnified. A decoded-bitmap source ignores it (its pixels are
        /// fixed). The default <c>1f</c> keeps existing implementations source-compatible and byte-identical.
        /// </summary>
        public abstract ITexture2D? GetTexture(IGraphicsContext graphicsContext, float surfaceDensity = 1f);
    }
}
