using Beutl.Media;
using Beutl.Media.Decoding;

namespace Beutl.Engine;

public interface IThumbnailsProvider
{
    ThumbnailsKind ThumbnailsKind { get; }

    event EventHandler? ThumbnailsInvalidated;

    /// <summary>
    /// サムネイルに影響するプロパティをシリアライズしたJSONのSHA256ハッシュを返す。
    /// キャッシュ不要の場合はnullを返す。
    /// </summary>
    string? GetThumbnailsCacheKey() => null;

    IAsyncEnumerable<(int Index, int Count, Bitmap Thumbnail)> GetThumbnailStripAsync(
        FontManager fonts,
        int maxWidth,
        int maxHeight,
        IThumbnailCacheService? cacheService = null,
        CancellationToken cancellationToken = default,
        int startIndex = 0,
        int endIndex = -1, DecoderRegistry? decoders = null);

    IAsyncEnumerable<WaveformChunk> GetWaveformChunksAsync(
        int chunkCount,
        int samplesPerChunk,
        IThumbnailCacheService? cacheService,
        CancellationToken cancellationToken = default, DecoderRegistry? decoders = null);
}

public readonly record struct WaveformChunk(int Index, float MinValue, float MaxValue);

public enum ThumbnailsKind
{
    None = 0,
    Video = 2,
    Audio = 3,
}
