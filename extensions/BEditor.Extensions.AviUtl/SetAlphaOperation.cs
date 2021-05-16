using BEditor.Drawing.Pixel;
using BEditor.Drawing.PixelOperation;

namespace BEditor.Extensions.AviUtl
{
    public readonly unsafe struct SetAlphaOperation : IPixelOperation
    {
        private readonly BGRA32* _data;
        private readonly byte _alpha;

        public SetAlphaOperation(BGRA32* data, byte alpha)
        {
            _data = data;
            _alpha = alpha;
        }

        public void Invoke(int pos)
        {
            _data[pos].A = _alpha;
        }
    }
}