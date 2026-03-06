using Beutl.Engine;
using Beutl.Graphics.Backend;

namespace Beutl.Graphics3D.Textures;

public abstract partial class TextureSource : EngineObject
{
    public partial class Resource
    {
        public abstract ITexture2D? GetTexture(IGraphicsContext graphicsContext);
    }
}
