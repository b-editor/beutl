using System.Runtime.InteropServices;

#pragma warning disable SYSLIB1054
#pragma warning disable IDE1006

namespace Beutl.Graphics.Rendering.GlContexts;

internal static class Kernel32
{
    private const string kernel32 = "kernel32.dll";

    static Kernel32()
    {
        CurrentModuleHandle = GetModuleHandle(null);
        if (CurrentModuleHandle == nint.Zero)
        {
            throw new Exception("Could not get module handle.");
        }
    }

    public static nint CurrentModuleHandle { get; }

    [DllImport(kernel32, CallingConvention = CallingConvention.Winapi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    public static extern nint GetModuleHandle([MarshalAs(UnmanagedType.LPTStr)] string? lpModuleName);
}
