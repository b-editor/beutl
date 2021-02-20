using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Graphics
{
    public interface IGraphicsObject : IDisposable
    {
        public ReadOnlyMemory<float> Vertices { get; }
        public int VertexBufferObject { get; }
        public int VertexArrayObject { get; }
        public bool IsDisposed { get; }
    }
}
