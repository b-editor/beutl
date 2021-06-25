using BEditor.Graphics.Platform;

namespace BEditor.Graphics.Veldrid
{
    public class VeldridPlatform : IPlatform
    {
        public IGraphicsContextImpl CreateContext(int width, int height)
        {
            return new GraphicsContextImpl(width, height);
        }
    }
}
