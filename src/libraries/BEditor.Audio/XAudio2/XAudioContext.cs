using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

using Vortice.Multimedia;
using Vortice.XAudio2;

namespace BEditor.Audio.XAudio2
{
    public sealed class XAudioContext
    {
        private const uint RPC_E_CHANGED_MODE = 0x80010106;
        private const uint COINIT_MULTITHREADED = 0x0;
        private const uint COINIT_APARTMENTTHREADED = 0x2;

        [DllImport("ole32.dll", SetLastError = true, CallingConvention = CallingConvention.StdCall)]
        private static extern uint CoInitializeEx([In, Optional] IntPtr pvReserved, [In] uint dwCoInit);

        static XAudioContext()
        {
            var hr = CoInitializeEx(IntPtr.Zero, COINIT_APARTMENTTHREADED);
            if (hr == RPC_E_CHANGED_MODE)
            {
                hr = CoInitializeEx(IntPtr.Zero, COINIT_MULTITHREADED);
            }
        }

        public XAudioContext()
        {
            Device = Vortice.XAudio2.XAudio2.XAudio2Create();
            MasteringVoice = Device.CreateMasteringVoice(2, 44100, AudioStreamCategory.GameMedia);
        }

        public IXAudio2 Device { get; }

        public IXAudio2MasteringVoice MasteringVoice { get; }
    }
}
