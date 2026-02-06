using Beutl.Media;

namespace Beutl.Operation;

public interface IElementThumbnailsProvider
{
    ElementThumbnailsKind ThumbnailsKind { get; }

    event EventHandler? ThumbnailsInvalidated;

    /// <summary>
    /// サムネイルに影響するプロパティをシリアライズしたJSONのSHA256ハッシュを返す。
    /// キャッシュ不要の場合はnullを返す。
    /// </summary>
    string? GetThumbnailsCacheKey() => null;

    IAsyncEnumerable<(int Index, int Count, IBitmap Thumbnail)> GetThumbnailStripAsync(
        int maxWidth,
        int maxHeight,
        IElementThumbnailCacheService? cacheService = null,
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
