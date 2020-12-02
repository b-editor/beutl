using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Drawing
{
    [StructLayout(LayoutKind.Sequential)]
    public struct BGRA32 : IPixel<BGRA32>
    {
        public byte B;
        public byte G;
        public byte R;
        public byte A;

        public int CvType=> (0 & (DepthMax - 1)) + ((Channels - 1) << ChannelShift);
        public int Channels => 4;

        private const int ChannelMax = 512,
            ChannelShift = 3,
            DepthMax = 1 << ChannelShift;
    }
}
