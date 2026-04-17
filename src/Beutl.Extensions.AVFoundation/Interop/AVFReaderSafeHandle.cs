using System.Runtime.InteropServices;

namespace Beutl.Extensions.AVFoundation.Interop;

internal sealed class AVFReaderSafeHandle : SafeHandle
{
    public AVFReaderSafeHandle() : base(IntPtr.Zero, ownsHandle: true) { }

    public override bool IsInvalid => handle == IntPtr.Zero;

    protected override bool ReleaseHandle()
    {
        BeutlAVFNative.beutl_avf_reader_close(handle);
        return true;
    }
}
