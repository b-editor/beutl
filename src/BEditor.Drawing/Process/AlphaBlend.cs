using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Drawing.Pixel;

namespace BEditor.Drawing.Process
{
    internal unsafe readonly struct AlphaBlendProcess<T> where T : unmanaged, IPixel<T>
    {
        private readonly T* dst;
        private readonly T* src;
        private readonly T* mask;

        public AlphaBlendProcess(T* src, T* dst, T* mask)
        {
            this.dst = dst;
            this.src = src;
            this.mask = mask;
        }

        public readonly void Invoke(int pos)
        {
            dst[pos] = src[pos].Blend(mask[pos]);
        }
    }
    internal unsafe readonly struct AddProcess<T> where T : unmanaged, IPixel<T>
    {
        private readonly T* dst;
        private readonly T* src;
        private readonly T* mask;

        public AddProcess(T* src, T* dst, T* mask)
        {
            this.dst = dst;
            this.src = src;
            this.mask = mask;
        }

        public readonly void Invoke(int pos)
        {
            dst[pos] = src[pos].Add(mask[pos]);
        }
    }
    internal unsafe readonly struct SubtractProcess<T> where T : unmanaged, IPixel<T>
    {
        private readonly T* dst;
        private readonly T* src;
        private readonly T* mask;

        public SubtractProcess(T* src, T* dst, T* mask)
        {
            this.dst = dst;
            this.src = src;
            this.mask = mask;
        }

        public readonly void Invoke(int pos)
        {
            dst[pos] = src[pos].Subtract(mask[pos]);
        }
    }
}
