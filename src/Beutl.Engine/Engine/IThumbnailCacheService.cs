using Beutl.Media;
using Beutl.Media.Source;

namespace Beutl.Engine;

public interface IThumbnailCacheService
{
    bool TryGet(string cacheKey, MediaTime time, MediaTime threshold, out Bitmap? bitmap);

    void Save(string cacheKey, MediaTime time, Bitmap bitmap);

    /// <summary>Acquires a lease on a read-only thumbnail. The caller disposes the lease.</summary>
    Ref<Bitmap>? AcquireVideo(string cacheKey, MediaTime time)
        => TryGet(cacheKey, time, MediaTime.Zero, out var bitmap) && bitmap != null ? Ref<Bitmap>.Create(bitmap) : null;

    /// <summary>Caches a read-only thumbnail. The caller retains ownership of its lease.</summary>
    void SaveVideo(string cacheKey, MediaTime time, Ref<Bitmap> bitmap) => Save(cacheKey, time, bitmap.Value);

    bool TryGetWaveform(string cacheKey, MediaTime time, MediaTime threshold, out float minValue, out float maxValue);

    void SaveWaveform(string cacheKey, MediaTime time, float minValue, float maxValue);

    void Invalidate(string cacheKey);
}
