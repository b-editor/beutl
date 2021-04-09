
using BEditor.Drawing.Pixel;

namespace BEditor.Drawing.Process
{
    public readonly unsafe struct XorProcess : IPixelProcess
    {
        private readonly BGRA32* _src;
        private readonly BGRA32* _dst;

        public XorProcess(BGRA32* src, BGRA32* dst)
        {
            _src = src;
            _dst = dst;
        }

        public readonly void Invoke(int pos)
        {
            _dst[pos].B = (byte)(_src[pos].B ^ 128);
            _dst[pos].G = (byte)(_src[pos].G ^ 128);
            _dst[pos].R = (byte)(_src[pos].R ^ 128);
            _dst[pos].A = _src[pos].A;
        }
    }
}
