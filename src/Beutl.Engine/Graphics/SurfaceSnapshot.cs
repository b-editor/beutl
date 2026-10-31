using System.Runtime.CompilerServices;
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
    public static Lease Take(SKSurface surface)
    {
        lock (s_snapshots)
        {
            SKImage image = surface.Snapshot();
            if (!s_snapshots.TryGetValue(image, out SharedSnapshot? snapshot))
            {
                snapshot = new SharedSnapshot(surface, image);
                s_snapshots.Add(image, snapshot);
            }

            snapshot.References++;
            return new Lease(snapshot);
        }
    }

    // SkiaSharp reuses the managed image for snapshots of unchanged surface content.
    private static readonly ConditionalWeakTable<SKImage, SharedSnapshot> s_snapshots = new();

    internal sealed class SharedSnapshot(SKSurface surface, SKImage image)
    {
        internal SKSurface Surface { get; } = surface;
        internal SKImage Image { get; } = image;
        internal int References;
    }

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

    /// <summary>A shared snapshot whose final disposal releases the copy its surface caches.</summary>
    public sealed class Lease : IDisposable
    {
        private SharedSnapshot? _snapshot;

        internal Lease(SharedSnapshot snapshot) => _snapshot = snapshot;

        public SKImage Image => (_snapshot ?? throw new ObjectDisposedException(nameof(Lease))).Image;

        public void Dispose()
        {
            lock (s_snapshots)
            {
                if (_snapshot is not { } snapshot) return;
                _snapshot = null;
                if (--snapshot.References != 0) return;
                s_snapshots.Remove(snapshot.Image);
                snapshot.Image.Dispose();
                Release(snapshot.Surface);
            }
        }
    }

    [DllImport("libSkiaSharp", CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "sk_beutl_surface_notify_content_will_change")]
    private static extern void NotifyContentWillChange(nint surface, [MarshalAs(UnmanagedType.I1)] bool retain);
}
