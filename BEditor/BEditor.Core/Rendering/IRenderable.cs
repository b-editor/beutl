using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Core.Rendering
{
    public interface IRenderable<T>
    {
        public void Render(IRenderer<T> renderer);
    }
}
