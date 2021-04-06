
using BEditor.Drawing.Pixel;

namespace BEditor.Drawing.Process
{
    internal readonly unsafe struct SetOpacityProcess
    {
        private readonly BGRA32* _data;
        private readonly float _opacity;

        public SetOpacityProcess(BGRA32* data, float opacity)
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
