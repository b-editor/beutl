
using BEditor.Drawing.Pixel;

namespace BEditor.Drawing.Process
{
    internal unsafe readonly struct SetColorProcess
    {
        private readonly BGRA32* _data;
        private readonly BGRA32 _color;

        public SetColorProcess(BGRA32* data, BGRA32 color)
        {
            _data = data;
            _color = color;
        }

        public readonly void Invoke(int pos)
        {
            _data[pos].B = _color.B;
            _data[pos].G = _color.G;
            _data[pos].R = _color.R;
        }
    }
}
