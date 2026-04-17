using System.Runtime.InteropServices;

namespace Beutl.Extensions.AVFoundation.Interop;

internal sealed class AVFWriterSafeHandle : SafeHandle
{
    public AVFWriterSafeHandle() : base(IntPtr.Zero, ownsHandle: true) { }

    public override bool IsInvalid => handle == IntPtr.Zero;

    protected override bool ReleaseHandle()
    {
        BeutlAVFNative.beutl_avf_writer_close(handle);
        return true;
    }
}
