using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Graphics;

namespace BEditor.Core.Service
{
    public class GraphicsContextService : IGraphicsContextService
    {
        public BaseGraphicsContext CreateContext(int width, int height) => new GraphicsContext(width, height);
    }
}
