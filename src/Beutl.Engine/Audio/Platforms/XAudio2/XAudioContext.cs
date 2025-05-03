using System.Runtime.InteropServices;
using Vortice.Multimedia;
using Vortice.XAudio2;

namespace Beutl.Audio.Platforms.XAudio2;

public sealed partial class XAudioContext : IDisposable
{
    private const uint RPC_E_CHANGED_MODE = 0x80010106;
    private const uint COINIT_MULTITHREADED = 0x0;
    private const uint COINIT_APARTMENTTHREADED = 0x2;
    private bool _isDisposed;

    [LibraryImport("ole32.dll", SetLastError = true)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static partial uint CoInitializeEx(IntPtr pvReserved, uint dwCoInit);

    static XAudioContext()
    {
        uint hr = CoInitializeEx(IntPtr.Zero, COINIT_APARTMENTTHREADED);
        if (hr == RPC_E_CHANGED_MODE)
        {
            _ = CoInitializeEx(IntPtr.Zero, COINIT_MULTITHREADED);
        }
    }

    public XAudioContext()
    {
        Device = Vortice.XAudio2.XAudio2.XAudio2Create();
        MasteringVoice = Device.CreateMasteringVoice(2, 44100, AudioStreamCategory.Other);
    }

    ~XAudioContext()
    {
        Dispose();
    }

    public IXAudio2 Device { get; }

    public IXAudio2MasteringVoice MasteringVoice { get; }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            MasteringVoice.Dispose();
            Device.Dispose();
            _isDisposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
