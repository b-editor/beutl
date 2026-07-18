using Beutl.Engine;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;

namespace Beutl.Graphics3D.Textures;

public abstract partial class TextureSource : EngineObject
{
    public partial class Resource
    {
        /// <summary>
        /// Resolves the texture for a 3D surface. <paramref name="surfaceDensity"/> is the surface's device-px-per-logical-unit
        /// density; re-rasterizable sources should rasterize at this density to stay crisp. Default <c>1f</c>.
        /// </summary>
        /// <param name="graphicsContext">The graphics context that owns the returned texture.</param>
        /// <param name="renderIntent">The preview/delivery allocation-failure policy.</param>
        /// <param name="pullPurpose">Whether this lookup may update the retained frame texture cache.</param>
        /// <param name="surfaceDensity">The requested device-pixel density.</param>
        public abstract ITexture2D? GetTexture(
            IGraphicsContext graphicsContext,
            RenderIntent renderIntent,
            RenderPullPurpose pullPurpose,
            float surfaceDensity = 1f);
    }
}
