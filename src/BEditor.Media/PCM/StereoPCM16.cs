using System;
using System.Runtime.InteropServices;

namespace BEditor.Media.PCM
{
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct StereoPCM16 : IPCM<StereoPCM16>, IPCMConvertable<StereoPCM32>
    {
        public short Left;
        public short Right;

        public StereoPCM16(short left, short right)
        {
            Left = left;
            Right = right;
        }

        public StereoPCM16 Add(StereoPCM16 s)
        {
            throw new NotImplementedException();
        }

        public void ConvertFrom(StereoPCM32 src)
        {
            Left = (short)(src.Left >> 16);
            Right = (short)(src.Right >> 16);
        }

        public void ConvertTo(out StereoPCM32 dst)
        {
            dst = new(Left << 16, Right << 16);
        }

        public override string ToString()
        {
            return $"Left = {Left}, Right = {Right}";
        }
    }
}