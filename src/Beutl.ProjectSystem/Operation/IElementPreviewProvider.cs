using Beutl.Media;

namespace Beutl.Operation;

public interface IElementPreviewProvider
{
    ElementPreviewKind PreviewKind { get; }

    event EventHandler? PreviewInvalidated;

    IAsyncEnumerable<(int Index, IBitmap Thumbnail)> GetThumbnailStripAsync(
        int count,
        int maxHeight,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<WaveformChunk> GetWaveformChunksAsync(
        int chunkCount,
        int samplesPerChunk,
        CancellationToken cancellationToken = default);
}

public readonly record struct WaveformChunk(int Index, float MinValue, float MaxValue);

public enum ElementPreviewKind
{
    None = 0,
    Video = 2,
    Audio = 3,
}
