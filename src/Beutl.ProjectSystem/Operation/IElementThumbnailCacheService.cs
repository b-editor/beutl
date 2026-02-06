using Beutl.Media;

namespace Beutl.Operation;

public interface IElementThumbnailCacheService
{
    bool TryGet(string cacheKey, TimeSpan time, TimeSpan threshold, out IBitmap? bitmap);

    void Save(string cacheKey, TimeSpan time, IBitmap bitmap);

    void Invalidate(string cacheKey);
}
