using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Rendering
{
    public interface IRenderer<T>
    {
        public void OnCompleted();
        public void OnFinally();
        public void OnError(Exception error);
        public void OnRender(T value);
    }
}
