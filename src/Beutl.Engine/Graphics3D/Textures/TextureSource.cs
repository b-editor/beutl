using Beutl.Engine;
using Beutl.Graphics.Backend;

namespace Beutl.Graphics3D.Textures;

public abstract partial class TextureSource : EngineObject
{
    public partial class Resource
    {
        /// <summary>
        /// Resolves the texture for a 3D surface. <paramref name="surfaceDensity"/> is the surface's device-px-per-logical-unit
        /// density; re-rasterizable sources should rasterize at this density to stay crisp. Default <c>1f</c>.
        /// </summary>
        public abstract ITexture2D? GetTexture(IGraphicsContext graphicsContext, float surfaceDensity = 1f);

        /// <summary>
        /// Resolves the texture for a material slot that interprets its samples as <paramref name="contentKind"/>.
        /// Sources whose pixels carry an encoding override this to skip the color conversion for
        /// <see cref="TextureContentKind.Data"/>; the default returns the color texture.
        /// </summary>
        public virtual ITexture2D? GetTexture(IGraphicsContext graphicsContext, float surfaceDensity, TextureContentKind contentKind)
        {
            return GetTexture(graphicsContext, surfaceDensity);
        }
    }
}
