using System;
using System.Runtime.InteropServices;

namespace BEditor.Media.PCM
{
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct StereoPCMFloat : IPCM<StereoPCMFloat>, IPCMConvertable<StereoPCM32>, IPCMConvertable<StereoPCM16>
    {
        public float Left;
        public float Right;

        public StereoPCMFloat(float left, float right)
        {
            Left = left;
            Right = right;
        }

        public StereoPCMFloat Add(StereoPCMFloat s)
        {
            return new(Left + s.Left, Right + s.Right);
        }

        public void ConvertFrom(StereoPCM32 src)
        {
            src.ConvertTo(out this);
        }

        public void ConvertFrom(StereoPCM16 src)
        {
            src.ConvertTo(out this);
        }

        public void ConvertTo(out StereoPCM32 dst)
        {
            dst = new((int)(Left * int.MaxValue), (int)(Right * int.MaxValue));
        }

        public void ConvertTo(out StereoPCM16 dst)
        {
            dst = new((short)(Left * short.MaxValue), (short)(Right * short.MaxValue));
        }

        public override string ToString()
        {
            return $"Left = {Left}, Right = {Right}";
        }
    }
}