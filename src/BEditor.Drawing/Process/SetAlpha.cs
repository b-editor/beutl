
using BEditor.Drawing.Pixel;

namespace BEditor.Drawing.Process
{
    internal unsafe readonly struct SetAlphaProcess
    {
        private readonly BGRA32* _data;
        private readonly float _alpha;

        public SetAlphaProcess(BGRA32* data, float alpha)
        {
            _data = data;
            _alpha = alpha;
        }

        public readonly void Invoke(int pos)
        {
            _data[pos].A = (byte)(_data[pos].A * _alpha);
        }
    }
}
