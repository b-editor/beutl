using BEditor.Graphics.Platform;

namespace BEditor.Graphics.Skia
{
    public sealed class SkiaPlatform : IPlatform
    {
        public IGraphicsContextImpl CreateContext(int width, int height)
        {
            return new GraphicsContextImpl(width, height);
        }
    }
}