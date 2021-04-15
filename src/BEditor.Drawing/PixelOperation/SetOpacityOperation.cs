
using BEditor.Drawing.Pixel;

namespace BEditor.Drawing.PixelOperation
{
    public readonly unsafe struct SetOpacityOperation : IPixelOperation
    {
        private readonly BGRA32* _data;
        private readonly float _opacity;

        public SetOpacityOperation(BGRA32* data, float opacity)
        {
            _data = data;
            _opacity = opacity;
        }

        public readonly void Invoke(int pos)
        {
            _data[pos].A = (byte)(_data[pos].A * _opacity);
        }
    }
}