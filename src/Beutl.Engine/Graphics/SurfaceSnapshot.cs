using System.Runtime.InteropServices;
using SkiaSharp;

namespace Beutl.Graphics;

/// <summary>Releases the snapshot a GPU surface caches after handing one out for a draw.</summary>
internal static class SurfaceSnapshot
{
    private static readonly Lazy<bool> s_available = new(() =>
    {
        try
        {
            // The export ignores a null surface, so this resolves it without touching Skia.
            NotifyContentWillChange(nint.Zero, retain: true);
            return true;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    });

    /// <summary>Whether the loaded libSkiaSharp can release a surface's cached snapshot.</summary>
    public static bool IsAvailable => s_available.Value;

    /// <summary>
    /// Makes <paramref name="surface"/> drop the snapshot it caches until its next write. Call it once every
    /// image taken from that snapshot has been disposed.
    /// </summary>
    /// <remarks>
    /// A snapshot of a surface that wraps a backend texture, as Metal render targets do, carries a copy of the
    /// whole texture that Skia runs at the next flush unless the snapshot is gone by then, and the surface keeps
    /// it until it is written again. The export comes from native/SkiaSharp/surface-content-change.patch, which
    /// only the macOS runtime carries: Vulkan surfaces wrap render targets, whose snapshots always copy.
    /// </remarks>
    public static void Release(SKSurface surface)
    {
        if (surface.Context is null || !IsAvailable)
            return;

        NotifyContentWillChange(surface.Handle, retain: true);
        GC.KeepAlive(surface);
    }

    [DllImport("libSkiaSharp", CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "sk_beutl_surface_notify_content_will_change")]
    private static extern void NotifyContentWillChange(nint surface, [MarshalAs(UnmanagedType.I1)] bool retain);
}
