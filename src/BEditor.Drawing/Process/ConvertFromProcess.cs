
using BEditor.Drawing.Pixel;

namespace BEditor.Drawing.Process
{
    internal readonly unsafe struct ConvertFromProcess<T1, T2>
        where T1 : unmanaged, IPixel<T1>
        where T2 : unmanaged, IPixel<T2>, IPixelConvertable<T1>
    {
        private readonly T1* _src;
        private readonly T2* _dst;

        public ConvertFromProcess(T1* src, T2* dst)
        {
            _src = src;
            _dst = dst;
        }

        public readonly void Invoke(int p)
        {
            _dst[p].ConvertFrom(_src[p]);
        }
    }
}
