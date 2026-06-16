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
    }
}
