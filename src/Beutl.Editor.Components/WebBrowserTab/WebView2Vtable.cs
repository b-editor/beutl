using System.Runtime.InteropServices;

namespace Beutl.Editor.Components.WebBrowserTab;

// Raw WebView2 COM calls shared by the Windows download handler and ad-block backend.
// A slot is the raw vtable index, IUnknown's three methods included.
internal static unsafe class WebView2Vtable
{
    public static nint Method(nint instance, int slot) => (*(nint**)instance)[slot];

    // Calls a getter that hands its result back through an out pointer, throwing on a failed HRESULT.
    public static nint GetObject(nint instance, int slot)
    {
        nint result;
        Marshal.ThrowExceptionForHR(
            ((delegate* unmanaged[Stdcall]<nint, nint*, int>)Method(instance, slot))(instance, &result));
        return result;
    }
}
