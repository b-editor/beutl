using System;
using System.Runtime.InteropServices;

using BEditor.Audio.Platform;

using Vortice.XAudio2;

namespace BEditor.Audio.XAudio2
{
    public class AudioContextImpl : IAudioContextImpl
    {
        internal IXAudio2MasteringVoice MasterVoice { get; }

        public IXAudio2 Device { get; }

        private const uint RPC_E_CHANGED_MODE = 0x80010106;
        private const uint COINIT_MULTITHREADED = 0x0;
        private const uint COINIT_APARTMENTTHREADED = 0x2;

        [DllImport("ole32.dll", SetLastError = true, CallingConvention = CallingConvention.StdCall)]
        private static extern uint CoInitializeEx([In, Optional] IntPtr pvReserved, [In] uint dwCoInit);

        static AudioContextImpl()
        {
            var hr = CoInitializeEx(IntPtr.Zero, COINIT_APARTMENTTHREADED);
            if (hr == RPC_E_CHANGED_MODE)
            {
                CoInitializeEx(IntPtr.Zero, COINIT_MULTITHREADED);
            }
        }

        public AudioContextImpl()
        {
            Device = Vortice.XAudio2.XAudio2.XAudio2Create(ProcessorSpecifier.DefaultProcessor);
            MasterVoice = Device.CreateMasteringVoice(1, 44100, Vortice.Multimedia.AudioStreamCategory.Media);
        }

        public void Dispose()
        {
            MasterVoice.Dispose();
            Device.Dispose();
            GC.SuppressFinalize(this);
        }

        public IAudioBufferImpl CreateBuffer()
        {
            return new AudioBufferImpl();
        }

        public IAudioSourceImpl CreateSource()
        {
            return new AudioSourceImpl(this);
        }
    }
}
