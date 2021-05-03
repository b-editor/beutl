using System;
using System.Runtime.InteropServices;

namespace BEditor.Media.PCM
{
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct StereoPCM16 : IPCM<StereoPCM16>, IPCMConvertable<StereoPCM32>, IPCMConvertable<StereoPCMFloat>
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
            return new((short)(Left + s.Left), (short)(Right + s.Right));
        }

        public void ConvertFrom(StereoPCM32 src)
        {
            src.ConvertTo(out this);
        }

        public void ConvertFrom(StereoPCMFloat src)
        {
            src.ConvertTo(out this);
        }

        public void ConvertTo(out StereoPCM32 dst)
        {
            dst = new(Left << 16, Right << 16);
        }

        public void ConvertTo(out StereoPCMFloat dst)
        {
            dst = new((float)Left / short.MaxValue, (float)Right / short.MaxValue);
        }

        public override string ToString()
        {
            return $"Left = {Left}, Right = {Right}";
        }
    }
}