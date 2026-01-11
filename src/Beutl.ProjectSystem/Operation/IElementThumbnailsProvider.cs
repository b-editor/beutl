using Beutl.Media;

namespace Beutl.Operation;

public interface IElementThumbnailsProvider
{
    ElementThumbnailsKind ThumbnailsKind { get; }

    event EventHandler? ThumbnailsInvalidated;

    IAsyncEnumerable<(int Index, int Count, IBitmap Thumbnail)> GetThumbnailStripAsync(
        int maxWidth,
        int maxHeight,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<WaveformChunk> GetWaveformChunksAsync(
        int chunkCount,
        int samplesPerChunk,
        CancellationToken cancellationToken = default);
}

public readonly record struct WaveformChunk(int Index, float MinValue, float MaxValue);

public enum ElementThumbnailsKind
{
    None = 0,
    Video = 2,
    Audio = 3,
}
