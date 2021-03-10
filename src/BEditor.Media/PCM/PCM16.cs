using System;
using System.Runtime.InteropServices;

namespace BEditor.Media.PCM
{

    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct PCM16 : IPCM<PCM16>, IPCMConvertable<PCM32>
    {
        public short Value;

        public PCM16(short value) => Value = value;

        public PCM16 Add(PCM16 s) => throw new NotImplementedException();

        public void ConvertFrom(PCM32 src)
        {
            Value = (short)(src.Value >> 16);
        }

        public void ConvertTo(out PCM32 dst)
        {
            dst = new(Value << 16);
        }

        public override string ToString()
        {
            return Value.ToString();
        }

        public static implicit operator short(PCM16 value) => value.Value;
        public static implicit operator PCM16(short value) => new(value);
    }
}
