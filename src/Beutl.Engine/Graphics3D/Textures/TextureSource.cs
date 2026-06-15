using Beutl.Engine;
using Beutl.Graphics.Backend;

namespace Beutl.Graphics3D.Textures;

public abstract partial class TextureSource : EngineObject
{
    public partial class Resource
    {
        /// <summary>
        /// Resolves the texture sampled onto a 3D surface. <paramref name="surfaceDensity"/> (feature 003) is the
        /// consuming surface's device-px-per-logical-unit density (the 3D <see cref="IRenderer3D.SurfaceDensity"/>):
        /// a re-rasterizable source (e.g. <see cref="DrawableTextureSource"/> wrapping a vector <c>Drawable</c>)
        /// should rasterize at this density to stay crisp on a supersampled surface rather than being GPU-magnified
        /// from a fixed author pixel count. A decoded-bitmap source ignores it. The default <c>1f</c> keeps existing
        /// implementations source-compatible and byte-identical.
        /// </summary>
        public abstract ITexture2D? GetTexture(IGraphicsContext graphicsContext, float surfaceDensity = 1f);
    }
}
