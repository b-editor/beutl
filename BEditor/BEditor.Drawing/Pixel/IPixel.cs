using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Drawing.Pixel
{
    public interface IPixel<T> where T : unmanaged, IPixel<T>
    {
        public T Blend(T foreground);
    }
}
