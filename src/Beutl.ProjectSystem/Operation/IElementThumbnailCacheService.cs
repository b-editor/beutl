using Beutl.Media;

namespace Beutl.Operation;

public interface IElementThumbnailCacheService
{
    bool TryGet(string cacheKey, TimeSpan time, TimeSpan threshold, out IBitmap? bitmap);

    void Save(string cacheKey, TimeSpan time, IBitmap bitmap);

    bool TryGetWaveform(string cacheKey, TimeSpan time, TimeSpan threshold, out float minValue, out float maxValue);

    void SaveWaveform(string cacheKey, TimeSpan time, float minValue, float maxValue);

    void Invalidate(string cacheKey);
}
