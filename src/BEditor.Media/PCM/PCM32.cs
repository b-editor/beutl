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
        public float Value;

        public PCM32(float value) => Value = value;

        public PCM32 Add(PCM32 s) => throw new NotImplementedException();
        public void ConvertFrom(PCM16 src)
        {

        }
        public void ConvertTo(out PCM16 dst) => throw new NotImplementedException();

        public static implicit operator float(PCM32 value) => value.Value;
        public static implicit operator PCM32(float value) => new(value);
    }
}
