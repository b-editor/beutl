using Beutl.Media;

namespace Beutl.Models;

public record FrameCacheOptions(
    FrameCacheScale Scale = FrameCacheScale.Original,
    FrameCacheColorType ColorType = FrameCacheColorType.BGRA,
    FrameCacheDeletionStrategy DeletionStrategy = FrameCacheDeletionStrategy.Old)
{
    public PixelSize? Size { get; init; }

    internal PixelSize GetSize(PixelSize original)
    {
        return Scale switch
        {
            FrameCacheScale.Original => original,
            FrameCacheScale.Manual => Size ?? original,
            FrameCacheScale.Half => PixelSize.FromSize(original.ToSize(0.5f), 1),
            _ => PixelSize.FromSize(original.ToSize(0.5f), 0.5f)
        };
    }

    /// <summary>
    /// Whether entries encoded under these options can be read back under <paramref name="other"/>.
    /// </summary>
    /// <remarks>
    /// Compared by scale mode rather than by the size the modes resolve to: an entry is encoded from
    /// the rendered snapshot, whose size is the device size, while the size these modes resolve
    /// against is the logical frame. Two modes that agree on the logical frame can therefore still
    /// produce entries of different sizes whenever the output scale is not 1.
    /// </remarks>
    internal bool ProducesSameCacheData(FrameCacheOptions other)
    {
        return ColorType == other.ColorType
            && Scale == other.Scale
            && (Scale != FrameCacheScale.Manual || Size == other.Size);
    }
}
