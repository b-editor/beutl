using Beutl.Media;

namespace Beutl.Models;

/// <summary>
/// Bounds how many rendered frames wait for their consumer: the playback clock, or the encoder during export.
/// </summary>
/// <remarks>
/// A waiting frame is a renderer snapshot in RgbaF16, 8 bytes per pixel, held outside the frame cache budget. A
/// fixed frame count therefore holds gigabytes at 4K, so the count comes from a byte budget instead.
/// </remarks>
internal static class ReadAheadBudget
{
    // RgbaF16, the format Renderer.Snapshot() reads back into.
    private const int SnapshotBytesPerPixel = 8;

    /// <summary>
    /// 1 GiB, about what 120 frames of 1080p took while snapshots had 4 bytes per pixel. It leaves 1080p a lead of
    /// 64 frames, two seconds at 30 fps, and holds 16 frames of 4K.
    /// </summary>
    public const long Bytes = 1L << 30;

    /// <summary>The lead kept for frames too large for the budget, so that they still play.</summary>
    public const int MinFrames = 4;

    /// <summary>The size of one snapshot of a render target of <paramref name="size"/>.</summary>
    public static long SnapshotBytes(PixelSize size) => (long)size.Width * size.Height * SnapshotBytesPerPixel;

    /// <summary>
    /// How many frames of <paramref name="frameBytes"/> each fit in <paramref name="budgetBytes"/>, at least
    /// <see cref="MinFrames"/> and at most <paramref name="maxFrames"/>.
    /// </summary>
    public static int FrameCount(long frameBytes, int maxFrames, long budgetBytes = Bytes)
    {
        if (frameBytes <= 0)
            return maxFrames;

        return (int)Math.Clamp(budgetBytes / frameBytes, Math.Min(MinFrames, maxFrames), maxFrames);
    }
}
