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
}
