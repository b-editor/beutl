using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Drawing.Pixel
{
    [StructLayout(LayoutKind.Sequential)]
    [PixelFormat(3, (0 & (BGRA32.DepthMax - 1)) + ((3 - 1) << BGRA32.ChannelShift))]
    public struct BGR24 : IPixel<BGR24>
    {
        public byte B;
        public byte G;
        public byte R;

        public BGR24 Blend(BGR24 foreground) => foreground;
    }
}
