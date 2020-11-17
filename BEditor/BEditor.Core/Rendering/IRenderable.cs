using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Core.Renderings
{
    public interface IRenderable<T>
    {
        public T Render(IRenderer<T> renderer);

    }
}
