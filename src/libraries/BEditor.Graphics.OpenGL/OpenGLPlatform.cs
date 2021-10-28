using BEditor.Graphics.Platform;

namespace BEditor.Graphics.OpenGL
{
    public sealed class OpenGLPlatform : IPlatform
    {
        public IGraphicsContextImpl CreateContext(int width, int height)
        {
            return new GraphicsContextImpl(width, height);
        }
    }
}