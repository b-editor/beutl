using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Drawing.Pixel;

namespace BEditor.Drawing.Process
{
    internal unsafe readonly struct ConvertProcess<T1, T2> where T2 : unmanaged, IPixel<T2> where T1 : unmanaged, IPixel<T1>, IPixelConvertable<T2>
    {
        private readonly T1* src;
        private readonly T2* dst;

        public ConvertProcess(T1* src, T2* dst)
        {
            this.src = src;
            this.dst = dst;
        }

        public readonly void Invoke(int p)
        {
            src[p].ConvertTo(out dst[p]);
        }
    }
}
