using Beutl.Media;

namespace Beutl.Engine;

public interface IThumbnailCacheService
{
    bool TryGet(string cacheKey, TimeSpan time, TimeSpan threshold, out Bitmap? bitmap);

    void Save(string cacheKey, TimeSpan time, Bitmap bitmap);

    bool TryGetWaveform(string cacheKey, TimeSpan time, TimeSpan threshold, out float minValue, out float maxValue);

    void SaveWaveform(string cacheKey, TimeSpan time, float minValue, float maxValue);

    void Invalidate(string cacheKey);
}
