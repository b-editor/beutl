using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Media.PCM
{
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct PCM32 : IPCM<PCM32>, IPCMConvertable<PCM16>
    {
        public int Value;

        public PCM32(int value) => Value = value;

        public PCM32 Add(PCM32 s) => throw new NotImplementedException();
        public void ConvertFrom(PCM16 src)
        {
            Value = src.Value << 16;
        }
        public void ConvertTo(out PCM16 dst)
        {
            dst = new((short)(Value >> 16));
        }

        public static implicit operator int(PCM32 value) => value.Value;
        public static implicit operator PCM32(int value) => new(value);
    }
}
