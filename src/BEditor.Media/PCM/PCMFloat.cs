using System;
using System.Runtime.InteropServices;

namespace BEditor.Media.PCM
{
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct PCMFloat : IPCM<PCMFloat>, IPCMConvertable<PCM16>, IPCMConvertable<PCM32>
    {
        public float Value;

        public PCMFloat(float value) => Value = value;

        public PCMFloat Add(PCMFloat s)
        {
            return new(Value + s.Value);
        }

        public void ConvertFrom(PCM16 src)
        {
            src.ConvertTo(out this);
        }

        public void ConvertFrom(PCM32 src)
        {
            src.ConvertTo(out this);
        }

        public void ConvertTo(out PCM16 dst)
        {
            dst = new((short)(Value * short.MaxValue));
        }

        public void ConvertTo(out PCM32 dst)
        {
            dst = new((int)(Value * int.MaxValue));
        }

        public static implicit operator float(PCMFloat value) => value.Value;
        public static implicit operator PCMFloat(float value) => new(value);
    }
}