using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Graphics.Platform;

namespace BEditor.Graphics.OpenGL
{
    public class OpenGLPlatform : IPlatform
    {
        public IGraphicsContextImpl CreateContext(int width, int height)
        {
            return new GraphicsContextImpl(width, height);
        }
    }
}