using Beutl.Media;

namespace Beutl.FFmpegWorker.Encoding;

/// <summary>Describes how a rendered frame is fitted into the output frame size.</summary>
internal static class FrameFit
{
    /// <summary>
    /// Gets the arguments of the <c>scale</c> filter, and of the <c>pad</c> filter when one is needed, that fit a
    /// <paramref name="source"/> frame into a <paramref name="destination"/> frame.
    /// </summary>
    /// <remarks>
    /// The whole frame is kept and the rest of the output is black, a letterbox or a pillarbox, so an output of
    /// another aspect ratio does not stretch the picture. A destination that is the source scaled and rounded to
    /// whole pixels, within a pixel on each axis, scales straight to it as before; proxies rely on that, since
    /// they round both extents to even numbers.
    /// </remarks>
    /// <param name="source">The size of the frames the encoder is handed.</param>
    /// <param name="destination">The size of the encoded frames.</param>
    /// <returns>The scale filter arguments, and the pad filter arguments or <see langword="null"/>.</returns>
    public static (string Scale, string? Pad) GetFilterArguments(PixelSize source, PixelSize destination)
    {
        int width = destination.Width;
        int height = destination.Height;
        string scaleToDestination = $"{width}:{height}";
        if (source.Width <= 0 || source.Height <= 0 || width <= 0 || height <= 0)
            return (scaleToDestination, null);

        // Some scale puts both scaled extents within a pixel of the destination's when the ranges of scales each
        // extent allows overlap: (width - 1) / source.Width <= (height + 1) / source.Height and the reverse.
        if ((long)(width - 1) * source.Height <= (long)(height + 1) * source.Width
            && (long)(height - 1) * source.Width <= (long)(width + 1) * source.Height)
        {
            return (scaleToDestination, null);
        }

        // A destination wider than the source keeps its height and gets bars at the sides; otherwise it keeps its
        // width and gets bars above and below.
        bool pillarbox = (long)width * source.Height > (long)height * source.Width;
        long fitted = pillarbox
            ? RoundedDivide((long)height * source.Width, source.Height)
            : RoundedDivide((long)width * source.Height, source.Width);

        // Pad trims a 4:2:0 picture to even extents before placing it, so the picture is kept even instead of
        // losing its last column or row.
        int picture = (int)Math.Max(2, fitted & ~1L);
        (int scaledWidth, int scaledHeight) = pillarbox ? (picture, height) : (width, picture);
        int x = (width - scaledWidth) / 2;
        int y = (height - scaledHeight) / 2;
        return ($"{scaledWidth}:{scaledHeight}", $"{width}:{height}:{x}:{y}:black");
    }

    private static long RoundedDivide(long dividend, long divisor) => (dividend + divisor / 2) / divisor;
}
