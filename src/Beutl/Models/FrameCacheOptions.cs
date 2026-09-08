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
    /// By scale mode, not by the size the modes resolve to: an entry is encoded from the rendered
    /// snapshot, whose size is the device size, while the modes resolve against the logical frame. Two
    /// modes agreeing on the logical frame still differ whenever the output scale is not 1.
    /// </remarks>
    internal bool ProducesSameCacheData(FrameCacheOptions other)
    {
        return ColorType == other.ColorType
            && Scale == other.Scale
            && (Scale != FrameCacheScale.Manual || Size == other.Size);
    }
}
