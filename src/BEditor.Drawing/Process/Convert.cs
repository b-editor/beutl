using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Drawing.Pixel;

namespace BEditor.Drawing.Process
{
    internal unsafe readonly struct ConvertToProcess<T1, T2> where T2 : unmanaged, IPixel<T2> where T1 : unmanaged, IPixel<T1>, IPixelConvertable<T2>
    {
        private readonly T1* src;
        private readonly T2* dst;

        public ConvertToProcess(T1* src, T2* dst)
        {
            this.src = src;
            this.dst = dst;
        }

        public readonly void Invoke(int p)
        {
            src[p].ConvertTo(out dst[p]);
        }
    }
    internal unsafe readonly struct ConvertFromProcess<T1, T2> where T1 : unmanaged, IPixel<T1> where T2 : unmanaged, IPixel<T2>, IPixelConvertable<T1>
    {
        private readonly T1* src;
        private readonly T2* dst;

        public ConvertFromProcess(T1* src, T2* dst)
        {
            this.src = src;
            this.dst = dst;
        }

        public readonly void Invoke(int p)
        {
            dst[p].ConvertFrom(src[p]);
        }
    }
}
