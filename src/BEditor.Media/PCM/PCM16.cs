using System;
using System.Runtime.InteropServices;

namespace BEditor.Media.PCM
{
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct PCM16 : IPCM<PCM16>
    {
        public short Value;

        public PCM16(short value) => Value = value;

        public PCM16 Add(PCM16 s) => throw new NotImplementedException();
        public override string ToString()
        {
            return Value.ToString();
        }

        public static implicit operator short(PCM16 value) => value.Value;
        public static implicit operator PCM16(short value) => new(value);
    }
}
