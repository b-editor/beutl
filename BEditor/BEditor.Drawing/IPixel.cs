using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Drawing
{
    public interface IPixel<T> where T : unmanaged, IPixel<T>
    {
        public int Channels { get; }
        public int CvType { get; }
    }
}
