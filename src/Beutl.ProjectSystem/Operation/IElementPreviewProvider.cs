using Beutl.Media;

namespace Beutl.Operation;

public interface IElementPreviewProvider
{
    ElementPreviewKind PreviewKind { get; }

    Task<IBitmap?> GetPreviewBitmapAsync(int maxWidth, int maxHeight, CancellationToken cancellationToken = default);

    IAsyncEnumerable<(int Index, IBitmap Thumbnail)> GetThumbnailStripAsync(
        int count,
        int maxHeight,
        CancellationToken cancellationToken = default);
}

public enum ElementPreviewKind
{
    None = 0,
    Image = 1,
    Video = 2,
    Audio = 3,
}
