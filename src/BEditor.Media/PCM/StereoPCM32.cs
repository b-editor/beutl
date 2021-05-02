using System;
using System.Runtime.InteropServices;

namespace BEditor.Media.PCM
{
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct StereoPCM32 : IPCM<StereoPCM32>, IPCMConvertable<StereoPCM16>, IPCMConvertable<StereoPCMFloat>
    {
        public int Left;
        public int Right;

        public StereoPCM32(int left, int right)
        {
            Left = left;
            Right = right;
        }

        public StereoPCM32 Add(StereoPCM32 s) => throw new NotImplementedException();

        public void ConvertFrom(StereoPCM16 src)
        {
            src.ConvertTo(out this);
        }

        public void ConvertFrom(StereoPCMFloat src)
        {
            src.ConvertTo(out this);
        }

        public void ConvertTo(out StereoPCM16 dst)
        {
            dst = new((short)(Left >> 16), (short)(Right >> 16));
        }

        public void ConvertTo(out StereoPCMFloat dst)
        {
            dst = new((float)Left / int.MaxValue, (float)Right / int.MaxValue);
        }
    }
}