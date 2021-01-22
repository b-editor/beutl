using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Drawing.Pixel;

namespace BEditor.Drawing.Process
{
    internal unsafe readonly struct SetAlphaProcess
    {
        private readonly BGRA32* data;
        private readonly float alpha;

        public SetAlphaProcess(BGRA32* data, float alpha)
        {
            this.data = data;
            this.alpha = alpha;
        }

        public readonly void Invoke(int pos)
        {
            data[pos].A = (byte)(data[pos].A * alpha);
        }
    }
}
