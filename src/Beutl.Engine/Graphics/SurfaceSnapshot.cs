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
    /// Takes a snapshot of <paramref name="surface"/> for draws recorded before the returned lease is disposed.
    /// Disposing it disposes the image and then releases the surface's cached snapshot.
    /// </summary>
    public static Lease Take(SKSurface surface) => new(surface, surface.Snapshot());

    /// <summary>
    /// Makes <paramref name="surface"/> drop the snapshot it caches until its next write. Call it once every
    /// image taken from that snapshot has been disposed.
    /// </summary>
    /// <remarks>
    /// GPU surfaces wrap backend textures, and a snapshot of one carries a copy of the whole texture that Skia
    /// runs at the next flush unless the snapshot is gone by then. The surface keeps the snapshot until it is
    /// written again, so without this the copy runs whenever a flush comes first. The export comes from
    /// native/SkiaSharp/surface-content-change.patch.
    /// </remarks>
    public static void Release(SKSurface surface)
    {
        if (surface.Context is null || !IsAvailable)
            return;

        NotifyContentWillChange(surface.Handle, retain: true);
        GC.KeepAlive(surface);
    }

    /// <summary>A snapshot whose disposal also releases the copy its surface caches.</summary>
    public readonly struct Lease(SKSurface surface, SKImage image) : IDisposable
    {
        public SKImage Image { get; } = image;

        public void Dispose()
        {
            Image?.Dispose();
            Release(surface);
        }
    }

    [DllImport("libSkiaSharp", CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "sk_beutl_surface_notify_content_will_change")]
    private static extern void NotifyContentWillChange(nint surface, [MarshalAs(UnmanagedType.I1)] bool retain);
}
