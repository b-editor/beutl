using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Drawing.Pixel;

namespace BEditor.Drawing.Process
{
    internal unsafe readonly struct SetColorProcess
    {
        private readonly BGRA32* data;
        private readonly BGRA32 color;

        public SetColorProcess(BGRA32* data, BGRA32 color)
        {
            this.data = data;
            this.color = color;
        }

        public readonly void Invoke(int pos)
        {
            data[pos].B = color.B;
            data[pos].G = color.G;
            data[pos].R = color.R;
        }
    }
}
